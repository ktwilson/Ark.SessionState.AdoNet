using System;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Ark.SessionState.AdoNet.Dialects;

namespace Ark.SessionState.AdoNet
{
    internal enum SessionLookupStatus
    {
        /// <summary>No session, or it had expired (and was removed).</summary>
        NotFound,
        /// <summary>The session exists but another request holds its lock.</summary>
        Locked,
        /// <summary>The session was read (and, for exclusive reads, locked).</summary>
        Found
    }

    internal sealed class SessionLookupResult
    {
        public SessionLookupStatus Status { get; set; }
        public byte[] Items { get; set; }
        public int Timeout { get; set; }
        public int LockId { get; set; }
        public TimeSpan LockAge { get; set; }
        public int Flags { get; set; }

        public static readonly SessionLookupResult NotFound = new SessionLookupResult { Status = SessionLookupStatus.NotFound };
    }

    /// <summary>
    /// All database access for the session store, using only ADO.NET base classes.
    /// Times are stored as UTC (with <see cref="DateTimeKind.Unspecified"/> so that
    /// providers such as Npgsql accept them for timestamp-without-time-zone columns).
    /// </summary>
    internal sealed class SessionStateRepository
    {
        /// <summary>SessionStateActions.InitializeItem.</summary>
        public const int InitializeItemFlag = 1;

        private static readonly Regex TableNamePattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_$#]*(\.[A-Za-z_][A-Za-z0-9_$#]*)?$", RegexOptions.CultureInvariant);

        private readonly DbProviderFactory _factory;
        private readonly string _connectionString;
        private readonly ISqlDialect _dialect;
        private readonly string _applicationName;
        private readonly int? _commandTimeout;
        private readonly Func<DateTime> _utcNow;

        private readonly string _sqlAcquireLock;
        private readonly string _sqlSelect;
        private readonly string _sqlDeleteIfExpired;
        private readonly string _sqlClearInitializeFlag;
        private readonly string _sqlInsert;
        private readonly string _sqlUpdateAndRelease;
        private readonly string _sqlSelectTimeout;
        private readonly string _sqlRelease;
        private readonly string _sqlResetTimeout;
        private readonly string _sqlRemove;
        private readonly string _sqlDeleteAllExpired;

        public SessionStateRepository(
            DbProviderFactory factory,
            string connectionString,
            ISqlDialect dialect,
            string tableName,
            string applicationName,
            int? commandTimeout = null,
            Func<DateTime> utcNow = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            _applicationName = applicationName ?? throw new ArgumentNullException(nameof(applicationName));
            _commandTimeout = commandTimeout;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);

            if (!IsValidTableName(tableName))
                throw new ArgumentException($"Invalid table name '{tableName}'. Use letters, digits and underscores, optionally schema-qualified (schema.table).", nameof(tableName));

            var t = tableName;
            var key = $"SessionId = {P("SessionId")} AND ApplicationName = {P("ApplicationName")}";

            // NOTE: parameters must be added in the order they appear in each statement,
            // so that positional placeholders (ODBC/OLE DB) bind correctly.
            _sqlAcquireLock = $"UPDATE {t} SET Locked = 1, LockDate = {P("LockDate")}, LockId = LockId + 1 WHERE {key} AND Locked = 0 AND Expires > {P("Now")}";
            _sqlSelect = $"SELECT Expires, SessionItems, LockId, LockDate, Flags, Timeout, Locked FROM {t} WHERE {key}";
            _sqlDeleteIfExpired = $"DELETE FROM {t} WHERE {key} AND Expires <= {P("Now")}";
            _sqlClearInitializeFlag = $"UPDATE {t} SET Flags = 0 WHERE {key}";
            _sqlInsert = $"INSERT INTO {t} (SessionId, ApplicationName, Created, Expires, LockDate, LockId, Timeout, Locked, SessionItems, Flags) " +
                         $"VALUES ({P("SessionId")}, {P("ApplicationName")}, {P("Created")}, {P("Expires")}, {P("LockDate")}, 0, {P("Timeout")}, 0, {P("SessionItems")}, {P("Flags")})";
            _sqlUpdateAndRelease = $"UPDATE {t} SET Expires = {P("Expires")}, SessionItems = {P("SessionItems")}, Timeout = {P("Timeout")}, Locked = 0 WHERE {key} AND LockId = {P("LockId")}";
            _sqlSelectTimeout = $"SELECT Timeout FROM {t} WHERE {key}";
            _sqlRelease = $"UPDATE {t} SET Expires = {P("Expires")}, Locked = 0 WHERE {key} AND LockId = {P("LockId")}";
            _sqlResetTimeout = $"UPDATE {t} SET Expires = {P("Expires")} WHERE {key}";
            _sqlRemove = $"DELETE FROM {t} WHERE {key} AND LockId = {P("LockId")}";
            _sqlDeleteAllExpired = $"DELETE FROM {t} WHERE Expires <= {P("Now")}";
        }

        public static bool IsValidTableName(string tableName) =>
            !string.IsNullOrEmpty(tableName) && TableNamePattern.IsMatch(tableName);

        /// <summary>
        /// Reads a session. When <paramref name="exclusive"/> is true the session is
        /// locked for this caller (if it isn't already locked by someone else).
        /// </summary>
        public SessionLookupResult Get(string id, bool exclusive)
        {
            var now = Now();

            using (var conn = Open())
            {
                var acquired = false;
                if (exclusive)
                {
                    using (var cmd = Command(conn, _sqlAcquireLock))
                    {
                        Add(cmd, "LockDate", DbType.DateTime, now);
                        AddKey(cmd, id);
                        Add(cmd, "Now", DbType.DateTime, now);
                        acquired = cmd.ExecuteNonQuery() > 0;
                    }
                }

                DateTime expires, lockDate;
                byte[] items;
                int lockId, flags, timeout;
                bool locked;

                using (var cmd = Command(conn, _sqlSelect))
                {
                    AddKey(cmd, id);
                    using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (!reader.Read())
                            return SessionLookupResult.NotFound;

                        expires = reader.GetDateTime(0);
                        items = reader.IsDBNull(1) ? null : (byte[])reader.GetValue(1);
                        lockId = Convert.ToInt32(reader.GetValue(2));
                        lockDate = reader.GetDateTime(3);
                        flags = Convert.ToInt32(reader.GetValue(4));
                        timeout = Convert.ToInt32(reader.GetValue(5));
                        locked = Convert.ToInt32(reader.GetValue(6)) != 0;
                    }
                }

                if (expires <= now)
                {
                    using (var cmd = Command(conn, _sqlDeleteIfExpired))
                    {
                        AddKey(cmd, id);
                        Add(cmd, "Now", DbType.DateTime, now);
                        cmd.ExecuteNonQuery();
                    }
                    return SessionLookupResult.NotFound;
                }

                if (exclusive ? !acquired : locked)
                {
                    return new SessionLookupResult
                    {
                        Status = SessionLookupStatus.Locked,
                        LockId = lockId,
                        LockAge = now > lockDate ? now - lockDate : TimeSpan.Zero
                    };
                }

                if ((flags & InitializeItemFlag) != 0)
                {
                    using (var cmd = Command(conn, _sqlClearInitializeFlag))
                    {
                        AddKey(cmd, id);
                        cmd.ExecuteNonQuery();
                    }
                }

                return new SessionLookupResult
                {
                    Status = SessionLookupStatus.Found,
                    Items = items,
                    Timeout = timeout,
                    LockId = lockId,
                    Flags = flags
                };
            }
        }

        /// <summary>
        /// Inserts a new, unlocked session, first removing any expired row with the same id.
        /// </summary>
        public void Insert(string id, byte[] items, int timeoutMinutes, int flags)
        {
            var now = Now();

            using (var conn = Open())
            {
                using (var cmd = Command(conn, _sqlDeleteIfExpired))
                {
                    AddKey(cmd, id);
                    Add(cmd, "Now", DbType.DateTime, now);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = Command(conn, _sqlInsert))
                {
                    AddKey(cmd, id);
                    Add(cmd, "Created", DbType.DateTime, now);
                    Add(cmd, "Expires", DbType.DateTime, now.AddMinutes(timeoutMinutes));
                    Add(cmd, "LockDate", DbType.DateTime, now);
                    Add(cmd, "Timeout", DbType.Int32, timeoutMinutes);
                    AddBinary(cmd, "SessionItems", items);
                    Add(cmd, "Flags", DbType.Int32, flags);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Saves session items and releases the lock held with <paramref name="lockId"/>.</summary>
        public bool UpdateAndRelease(string id, int lockId, byte[] items, int timeoutMinutes)
        {
            using (var conn = Open())
            using (var cmd = Command(conn, _sqlUpdateAndRelease))
            {
                Add(cmd, "Expires", DbType.DateTime, Now().AddMinutes(timeoutMinutes));
                AddBinary(cmd, "SessionItems", items);
                Add(cmd, "Timeout", DbType.Int32, timeoutMinutes);
                AddKey(cmd, id);
                Add(cmd, "LockId", DbType.Int32, lockId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>Releases the lock held with <paramref name="lockId"/> and slides the expiry.</summary>
        public bool Release(string id, int lockId)
        {
            using (var conn = Open())
            {
                var timeout = ReadTimeout(conn, id);
                if (timeout == null)
                    return false;

                using (var cmd = Command(conn, _sqlRelease))
                {
                    Add(cmd, "Expires", DbType.DateTime, Now().AddMinutes(timeout.Value));
                    AddKey(cmd, id);
                    Add(cmd, "LockId", DbType.Int32, lockId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        /// <summary>Slides the expiry without changing the lock.</summary>
        public bool ResetTimeout(string id)
        {
            using (var conn = Open())
            {
                var timeout = ReadTimeout(conn, id);
                if (timeout == null)
                    return false;

                using (var cmd = Command(conn, _sqlResetTimeout))
                {
                    Add(cmd, "Expires", DbType.DateTime, Now().AddMinutes(timeout.Value));
                    AddKey(cmd, id);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        /// <summary>Deletes the session if <paramref name="lockId"/> matches (Session.Abandon).</summary>
        public bool Remove(string id, int lockId)
        {
            using (var conn = Open())
            using (var cmd = Command(conn, _sqlRemove))
            {
                AddKey(cmd, id);
                Add(cmd, "LockId", DbType.Int32, lockId);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>Deletes every expired session in the table (all applications).</summary>
        public int DeleteExpired()
        {
            using (var conn = Open())
            using (var cmd = Command(conn, _sqlDeleteAllExpired))
            {
                Add(cmd, "Now", DbType.DateTime, Now());
                return cmd.ExecuteNonQuery();
            }
        }

        private int? ReadTimeout(DbConnection conn, string id)
        {
            using (var cmd = Command(conn, _sqlSelectTimeout))
            {
                AddKey(cmd, id);
                var value = cmd.ExecuteScalar();
                return value == null || value is DBNull ? (int?)null : Convert.ToInt32(value);
            }
        }

        private DateTime Now() => DateTime.SpecifyKind(_utcNow(), DateTimeKind.Unspecified);

        private string P(string name) => _dialect.FormatPlaceholder(name);

        private DbConnection Open()
        {
            var conn = _factory.CreateConnection()
                       ?? throw new InvalidOperationException($"{_factory.GetType().FullName}.CreateConnection() returned null.");
            try
            {
                conn.ConnectionString = _connectionString;
                conn.Open();
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }

        private DbCommand Command(DbConnection conn, string sql)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            if (_commandTimeout.HasValue)
                cmd.CommandTimeout = _commandTimeout.Value;
            _dialect.PrepareCommand(cmd);
            return cmd;
        }

        private void AddKey(DbCommand cmd, string id)
        {
            Add(cmd, "SessionId", DbType.AnsiString, id);
            Add(cmd, "ApplicationName", DbType.AnsiString, _applicationName);
        }

        private DbParameter Add(DbCommand cmd, string name, DbType type, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = _dialect.FormatParameterName(name);
            p.DbType = type;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
            return p;
        }

        private void AddBinary(DbCommand cmd, string name, byte[] value)
        {
            var p = Add(cmd, name, DbType.Binary, value);
            _dialect.PrepareBinaryParameter(p);
        }
    }
}
