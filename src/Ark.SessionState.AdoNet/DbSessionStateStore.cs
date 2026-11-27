using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Web;
using System.Web.Hosting;
using System.Web.SessionState;
using Ark.SessionState.AdoNet.Dialects;

namespace Ark.SessionState.AdoNet
{
    /// <summary>
    /// ASP.NET custom session state store that keeps sessions in any database reachable
    /// through ADO.NET (SQL Server, Oracle, PostgreSQL, MySQL/MariaDB, SQLite, ODBC, OLE DB...).
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;sessionState mode="Custom" customProvider="DbSessionState"&gt;
    ///   &lt;providers&gt;
    ///     &lt;add name="DbSessionState"
    ///          type="Ark.SessionState.AdoNet.DbSessionStateStore, Ark.SessionState.AdoNet"
    ///          connectionStringName="SessionState" /&gt;
    ///   &lt;/providers&gt;
    /// &lt;/sessionState&gt;
    /// </code>
    /// </example>
    public sealed class DbSessionStateStore : SessionStateStoreProviderBase
    {
        public const string DefaultTableName = "AspNetSessionState";
        public const int DefaultCleanupIntervalMinutes = 10;

        private const string DefaultName = "DbSessionStateStore";
        private const string EventSource = "Ark.SessionState.AdoNet";
        private const string EventLogName = "Application";
        private const string ExceptionMessage = "An exception occurred in the session state store. Please contact your administrator.";

        private SessionStateRepository _repository;

        /// <summary>Distinguishes sessions of different applications sharing one table.</summary>
        public string ApplicationName { get; private set; }

        /// <summary>The table holding session rows.</summary>
        public string TableName { get; private set; }

        /// <summary>The SQL dialect in use (resolved from configuration).</summary>
        public ISqlDialect Dialect { get; private set; }

        /// <summary>
        /// If true, database exceptions are written to the Windows event log and a generic
        /// <see cref="ProviderException"/> is thrown. If false, they are thrown as-is.
        /// </summary>
        public bool WriteExceptionsToEventLog { get; set; }

        /// <summary>
        /// Reads the provider's attributes from &lt;sessionState&gt;&lt;providers&gt;&lt;add .../&gt;.
        /// </summary>
        /// <remarks>
        /// Attributes:
        /// <list type="bullet">
        /// <item><c>connectionStringName</c> (required) - an entry in &lt;connectionStrings&gt;. Its <c>providerName</c> selects the ADO.NET provider (default System.Data.SqlClient).</item>
        /// <item><c>providerFactoryType</c> - assembly-qualified DbProviderFactory type; use instead of registering the provider under &lt;system.data&gt;.</item>
        /// <item><c>dialect</c> - auto (default), SqlServer, Oracle, PostgreSql, MySql, Sqlite, Odbc, OleDb, or a custom ISqlDialect type.</item>
        /// <item><c>tableName</c> - default AspNetSessionState; may be schema-qualified.</item>
        /// <item><c>applicationName</c> - default is the app's virtual path.</item>
        /// <item><c>cleanupInterval</c> - minutes between expired-session sweeps, default 10; 0 disables.</item>
        /// <item><c>commandTimeout</c> - seconds; default is the provider's default.</item>
        /// <item><c>writeExceptionsToEventLog</c> - true/false, default false.</item>
        /// </list>
        /// </remarks>
        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrEmpty(name))
                name = DefaultName;

            if (string.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "ADO.NET session state store provider");
            }

            base.Initialize(name, config);

            var connectionStringName = Take(config, "connectionStringName");
            var providerFactoryType = Take(config, "providerFactoryType");
            var dialectName = Take(config, "dialect");
            var tableName = Take(config, "tableName");
            var applicationName = Take(config, "applicationName");
            var cleanupInterval = Take(config, "cleanupInterval");
            var commandTimeout = Take(config, "commandTimeout");
            var writeToEventLog = Take(config, "writeExceptionsToEventLog");
            config.Remove("description");

            if (config.Count > 0)
                throw new ProviderException($"Unrecognized attribute '{config.GetKey(0)}' on session state provider '{name}'.");

            if (string.IsNullOrWhiteSpace(connectionStringName))
                throw new ProviderException("The 'connectionStringName' attribute is required.");

            var settings = ConfigurationManager.ConnectionStrings[connectionStringName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
                throw new ProviderException($"Connection string '{connectionStringName}' was not found or is blank.");

            ApplicationName = !string.IsNullOrWhiteSpace(applicationName)
                ? applicationName.Trim()
                : HostingEnvironment.ApplicationVirtualPath ?? "/";

            TableName = string.IsNullOrWhiteSpace(tableName) ? DefaultTableName : tableName.Trim();
            if (!SessionStateRepository.IsValidTableName(TableName))
                throw new ProviderException($"Invalid tableName '{TableName}'. Use letters, digits and underscores, optionally schema-qualified (schema.table).");

            WriteExceptionsToEventLog = ParseBool(writeToEventLog, "writeExceptionsToEventLog");
            var cleanupMinutes = ParseInt(cleanupInterval, "cleanupInterval", DefaultCleanupIntervalMinutes);
            var timeoutSeconds = string.IsNullOrWhiteSpace(commandTimeout) ? (int?)null : ParseInt(commandTimeout, "commandTimeout", 0);

            DbProviderFactory factory = ProviderFactoryResolver.Resolve(providerFactoryType, settings.ProviderName);
            Dialect = SqlDialects.Resolve(dialectName, settings.ProviderName, factory);

            _repository = new SessionStateRepository(
                factory, settings.ConnectionString, Dialect, TableName, ApplicationName, timeoutSeconds);

            if (cleanupMinutes > 0)
            {
                var repository = _repository;
                ExpiredSessionCleaner.EnsureScheduled(
                    key: settings.ConnectionString + "|" + TableName,
                    interval: TimeSpan.FromMinutes(cleanupMinutes),
                    cleanup: () => repository.DeleteExpired(),
                    onError: ex => Log(ex, "DeleteExpired"));
            }
        }

        public override SessionStateStoreData GetItem(HttpContext context, string id,
            out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id,
            out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        private SessionStateStoreData GetSessionStoreItem(bool exclusive, HttpContext context, string id,
            out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            locked = false;
            lockAge = TimeSpan.Zero;
            lockId = null;
            actions = SessionStateActions.None;

            var result = Execute(exclusive ? nameof(GetItemExclusive) : nameof(GetItem),
                () => _repository.Get(id, exclusive));

            switch (result.Status)
            {
                case SessionLookupStatus.Locked:
                    locked = true;
                    lockAge = result.LockAge;
                    lockId = result.LockId;
                    return null;

                case SessionLookupStatus.Found:
                    lockId = result.LockId;
                    actions = (SessionStateActions)result.Flags;
                    return actions == SessionStateActions.InitializeItem
                        ? CreateNewStoreData(context, result.Timeout)
                        : new SessionStateStoreData(
                            SessionItemsSerializer.Deserialize(result.Items),
                            SessionStateUtility.GetSessionStaticObjects(context),
                            result.Timeout);

                default:
                    return null;
            }
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id,
            SessionStateStoreData item, object lockId, bool newItem)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var items = item.Items as SessionStateItemCollection;
            if (item.Items != null && items == null)
                throw new ProviderException($"{nameof(DbSessionStateStore)} only supports {nameof(SessionStateItemCollection)} session items.");

            var data = SessionItemsSerializer.Serialize(items);

            Execute(nameof(SetAndReleaseItemExclusive), () =>
            {
                if (newItem)
                    _repository.Insert(id, data, item.Timeout, flags: 0);
                else
                    _repository.UpdateAndRelease(id, ToLockId(lockId), data, item.Timeout);
                return true;
            });
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            Execute(nameof(ReleaseItemExclusive), () => _repository.Release(id, ToLockId(lockId)));
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            Execute(nameof(RemoveItem), () => _repository.Remove(id, ToLockId(lockId)));
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            Execute(nameof(ResetItemTimeout), () => _repository.ResetTimeout(id));
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            Execute(nameof(CreateUninitializedItem), () =>
            {
                _repository.Insert(id, items: null, timeoutMinutes: timeout, flags: SessionStateRepository.InitializeItemFlag);
                return true;
            });
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(
                new SessionStateItemCollection(),
                SessionStateUtility.GetSessionStaticObjects(context),
                timeout);
        }

        /// <summary>Deletes all expired sessions now. Returns the number of rows removed.</summary>
        public int DeleteExpiredSessions() => Execute("DeleteExpired", () => _repository.DeleteExpired());

        // Session_End is not supported: expiry is detected lazily or by the cleanup sweep.
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback) => false;

        public override void InitializeRequest(HttpContext context) { }

        public override void EndRequest(HttpContext context) { }

        public override void Dispose() { }

        private static int ToLockId(object lockId)
        {
            if (lockId == null)
                throw new ArgumentNullException(nameof(lockId));
            return lockId is int i ? i : Convert.ToInt32(lockId, CultureInfo.InvariantCulture);
        }

        private T Execute<T>(string action, Func<T> operation)
        {
            if (_repository == null)
                throw new InvalidOperationException($"{nameof(DbSessionStateStore)} has not been initialized.");

            try
            {
                return operation();
            }
            catch (DbException ex) when (WriteExceptionsToEventLog)
            {
                Log(ex, action);
                throw new ProviderException(ExceptionMessage, ex);
            }
        }

        private void Log(Exception ex, string action)
        {
            var message = $"An exception occurred communicating with the session state database.{Environment.NewLine}{Environment.NewLine}" +
                          $"Action: {action}{Environment.NewLine}{Environment.NewLine}Exception: {ex}";

            Trace.TraceError(message);

            if (!WriteExceptionsToEventLog)
                return;

            try
            {
                // Creating an event source needs admin rights; fall back to an existing one.
                var source = EventLog.SourceExists(EventSource) ? EventSource : "Application";
                EventLog.WriteEntry(source, message, EventLogEntryType.Error);
            }
            catch
            {
                // Event log not writable (permissions, partial trust). Trace output above still has it.
            }
        }

        private static string Take(NameValueCollection config, string key)
        {
            var value = config[key];
            config.Remove(key);
            return value;
        }

        private static bool ParseBool(string value, string attribute)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (bool.TryParse(value.Trim(), out var result))
                return result;
            throw new ProviderException($"Attribute '{attribute}' must be true or false.");
        }

        private static int ParseInt(string value, string attribute, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0)
                return result;
            throw new ProviderException($"Attribute '{attribute}' must be a non-negative integer.");
        }
    }
}
