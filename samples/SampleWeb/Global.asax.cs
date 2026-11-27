using System;
using System.Configuration;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Web;
using System.Web.Hosting;
using Ark.SessionState.AdoNet.Dialects;

namespace SampleWeb
{
    public class Global : HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            if (string.Equals(ConfigurationManager.AppSettings["Sample:CreateSchema"], "true", StringComparison.OrdinalIgnoreCase))
                SchemaSetup.EnsureTable();
        }
    }

    /// <summary>
    /// Sample-only convenience: creates the session table (and, for SQL Server, the database)
    /// if it's missing. In a real app, run the script from the sql folder once instead.
    /// </summary>
    internal static class SchemaSetup
    {
        public static void EnsureTable()
        {
            var settings = ConfigurationManager.ConnectionStrings["SessionState"];
            var factory = DbProviderFactories.GetFactory(settings.ProviderName);
            var dialect = SqlDialects.Detect(settings.ProviderName) ?? SqlDialects.SqlServer;

            if (dialect == SqlDialects.SqlServer)
                EnsureSqlServerDatabase(settings.ConnectionString);

            using (var conn = factory.CreateConnection())
            {
                conn.ConnectionString = settings.ConnectionString;
                conn.Open();

                if (TableExists(conn))
                    return;

                var script = File.ReadAllText(HostingEnvironment.MapPath($"~/App_Data/sql/{ScriptName(dialect)}"));
                foreach (var statement in script.Split(';'))
                {
                    var sql = StripComments(statement);
                    if (sql.Length == 0)
                        continue;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static bool TableExists(DbConnection conn)
        {
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM AspNetSessionState";
                    cmd.ExecuteScalar();
                    return true;
                }
            }
            catch (DbException)
            {
                return false;
            }
        }

        private static void EnsureSqlServerDatabase(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var database = builder.InitialCatalog;
            if (string.IsNullOrEmpty(database))
                return;

            builder.InitialCatalog = "master";
            using (var conn = new SqlConnection(builder.ConnectionString))
            using (var cmd = conn.CreateCommand())
            {
                conn.Open();
                cmd.CommandText = "IF DB_ID(@name) IS NULL EXEC('CREATE DATABASE [' + @name + ']')";
                cmd.Parameters.AddWithValue("@name", database);
                cmd.ExecuteNonQuery();
            }
        }

        private static string ScriptName(ISqlDialect dialect)
        {
            switch (dialect.Name)
            {
                case "Oracle": return "oracle.sql";
                case "PostgreSql": return "postgresql.sql";
                case "MySql": return "mysql.sql";
                case "Sqlite": return "sqlite.sql";
                default: return "sqlserver.sql";
            }
        }

        private static string StripComments(string sql)
        {
            var lines = sql.Split('\n');
            var kept = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("--"))
                    kept.AppendLine(line.TrimEnd('\r'));
            }
            return kept.ToString().Trim();
        }
    }
}
