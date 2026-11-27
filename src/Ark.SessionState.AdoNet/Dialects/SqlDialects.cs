using System;
using System.Configuration.Provider;
using System.Data.Common;

namespace Ark.SessionState.AdoNet.Dialects
{
    /// <summary>
    /// The built-in dialects, and the logic that picks one for a configured provider.
    /// </summary>
    public static class SqlDialects
    {
        public static readonly ISqlDialect SqlServer = new SqlDialect("SqlServer", "@");
        public static readonly ISqlDialect Oracle = new OracleDialect();
        public static readonly ISqlDialect PostgreSql = new SqlDialect("PostgreSql", "@");
        public static readonly ISqlDialect MySql = new SqlDialect("MySql", "@");
        public static readonly ISqlDialect Sqlite = new SqlDialect("Sqlite", "@");
        public static readonly ISqlDialect Odbc = new PositionalSqlDialect("Odbc");
        public static readonly ISqlDialect OleDb = new PositionalSqlDialect("OleDb");

        /// <summary>
        /// Resolves the dialect to use.
        /// </summary>
        /// <param name="configured">
        /// The <c>dialect</c> attribute: empty or "auto" to detect, a built-in name
        /// (SqlServer, Oracle, PostgreSql, MySql, Sqlite, Odbc, OleDb), or the
        /// assembly-qualified name of an <see cref="ISqlDialect"/> implementation.
        /// </param>
        /// <param name="providerName">The connection string's providerName, if any.</param>
        /// <param name="factory">The resolved provider factory.</param>
        public static ISqlDialect Resolve(string configured, string providerName, DbProviderFactory factory)
        {
            if (!string.IsNullOrWhiteSpace(configured) &&
                !configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return FromName(configured.Trim());
            }

            return Detect(providerName) ?? Detect(factory?.GetType().FullName) ?? throw new ProviderException(
                $"Could not detect the SQL dialect for provider '{providerName ?? factory?.GetType().FullName}'. " +
                "Set the 'dialect' attribute (SqlServer, Oracle, PostgreSql, MySql, Sqlite, Odbc, OleDb, " +
                "or the type name of a custom ISqlDialect).");
        }

        /// <summary>Returns the dialect for a provider invariant name or factory type name, or null.</summary>
        public static ISqlDialect Detect(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            bool Has(string s) => name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

            if (Has("Oracle")) return Oracle;
            if (Has("Npgsql") || Has("Postgre")) return PostgreSql;
            if (Has("MySql") || Has("MariaDb")) return MySql;
            if (Has("Sqlite")) return Sqlite;
            if (Has("Odbc")) return Odbc;
            if (Has("OleDb")) return OleDb;
            if (Has("SqlClient")) return SqlServer;
            return null;
        }

        private static ISqlDialect FromName(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "sqlserver":
                case "mssql":
                    return SqlServer;
                case "oracle":
                    return Oracle;
                case "postgresql":
                case "postgres":
                case "npgsql":
                    return PostgreSql;
                case "mysql":
                case "mariadb":
                    return MySql;
                case "sqlite":
                    return Sqlite;
                case "odbc":
                    return Odbc;
                case "oledb":
                    return OleDb;
            }

            var type = Type.GetType(name, throwOnError: false);
            if (type == null)
                throw new ProviderException($"Unknown dialect '{name}'. Use a built-in name or an assembly-qualified ISqlDialect type name.");
            if (!typeof(ISqlDialect).IsAssignableFrom(type))
                throw new ProviderException($"Type '{type.FullName}' does not implement {nameof(ISqlDialect)}.");

            return (ISqlDialect)Activator.CreateInstance(type);
        }
    }
}
