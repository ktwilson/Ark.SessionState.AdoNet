using System;
using System.Configuration.Provider;
using System.Web.SessionState;
using Ark.SessionState.AdoNet.Dialects;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ark.SessionState.AdoNet.Tests
{
    public class SerializerTests
    {
        [Fact]
        public void Round_trips_session_items()
        {
            var items = new SessionStateItemCollection();
            items["name"] = "Kevin";
            items["count"] = 42;
            items["when"] = new DateTime(2026, 1, 2);

            var copy = SessionItemsSerializer.Deserialize(SessionItemsSerializer.Serialize(items));

            Assert.Equal("Kevin", copy["name"]);
            Assert.Equal(42, copy["count"]);
            Assert.Equal(new DateTime(2026, 1, 2), copy["when"]);
        }

        [Fact]
        public void Null_or_empty_data_gives_empty_collection()
        {
            Assert.Empty(SessionItemsSerializer.Deserialize(null));
            Assert.Empty(SessionItemsSerializer.Deserialize(new byte[0]));
        }
    }

    public class DialectTests
    {
        [Theory]
        [InlineData("System.Data.SqlClient", "SqlServer")]
        [InlineData("Microsoft.Data.SqlClient", "SqlServer")]
        [InlineData("Oracle.ManagedDataAccess.Client", "Oracle")]
        [InlineData("Oracle.DataAccess.Client", "Oracle")]
        [InlineData("Npgsql", "PostgreSql")]
        [InlineData("MySql.Data.MySqlClient", "MySql")]
        [InlineData("MySqlConnector", "MySql")]
        [InlineData("System.Data.SQLite", "Sqlite")]
        [InlineData("Microsoft.Data.Sqlite", "Sqlite")]
        [InlineData("System.Data.Odbc", "Odbc")]
        [InlineData("System.Data.OleDb", "OleDb")]
        public void Detects_dialect_from_provider_name(string provider, string expected)
        {
            Assert.Equal(expected, SqlDialects.Detect(provider).Name);
        }

        [Fact]
        public void Explicit_dialect_wins_over_detection()
        {
            Assert.Same(SqlDialects.Oracle, SqlDialects.Resolve("oracle", "System.Data.SqlClient", null));
        }

        [Fact]
        public void Detects_from_factory_type_when_provider_name_missing()
        {
            Assert.Same(SqlDialects.Sqlite, SqlDialects.Resolve(null, null, SqliteFactory.Instance));
        }

        [Fact]
        public void Custom_dialect_type_is_loaded()
        {
            var dialect = SqlDialects.Resolve(typeof(TestDialect).AssemblyQualifiedName, null, null);
            Assert.IsType<TestDialect>(dialect);
        }

        [Fact]
        public void Unknown_dialect_throws()
        {
            Assert.Throws<ProviderException>(() => SqlDialects.Resolve("Nope", null, null));
        }

        [Fact]
        public void Placeholders_follow_dialect()
        {
            Assert.Equal("@Id", SqlDialects.SqlServer.FormatPlaceholder("Id"));
            Assert.Equal(":Id", SqlDialects.Oracle.FormatPlaceholder("Id"));
            Assert.Equal("Id", SqlDialects.Oracle.FormatParameterName("Id"));
            Assert.Equal("?", SqlDialects.Odbc.FormatPlaceholder("Id"));
        }

        public class TestDialect : SqlDialect
        {
            public TestDialect() : base("Test", "@") { }
        }
    }

    public class ConfigurationTests
    {
        [Theory]
        [InlineData("AspNetSessionState", true)]
        [InlineData("dbo.AspNetSessionState", true)]
        [InlineData("SESSION$STATE", true)]
        [InlineData("x; DROP TABLE users", false)]
        [InlineData("a.b.c", false)]
        [InlineData("", false)]
        public void Validates_table_names(string name, bool valid)
        {
            Assert.Equal(valid, SessionStateRepository.IsValidTableName(name));
        }

        [Fact]
        public void Resolves_factory_from_type_name()
        {
            var factory = ProviderFactoryResolver.Resolve(typeof(SqliteFactory).AssemblyQualifiedName, null);
            Assert.Same(SqliteFactory.Instance, factory);
        }

        [Fact]
        public void Resolves_registered_factory_by_invariant_name()
        {
            Assert.NotNull(ProviderFactoryResolver.Resolve(null, "System.Data.SqlClient"));
        }

        [Fact]
        public void Unregistered_provider_gives_helpful_error()
        {
            var ex = Assert.Throws<ProviderException>(() => ProviderFactoryResolver.Resolve(null, "Not.A.Provider"));
            Assert.Contains("providerFactoryType", ex.Message);
        }
    }
}
