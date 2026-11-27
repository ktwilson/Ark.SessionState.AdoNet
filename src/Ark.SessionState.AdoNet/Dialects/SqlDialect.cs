using System;
using System.Data.Common;

namespace Ark.SessionState.AdoNet.Dialects
{
    /// <summary>
    /// A dialect that uses named parameters with a prefix character (e.g. <c>@name</c>).
    /// Works as-is for SQL Server, PostgreSQL (Npgsql), MySQL/MariaDB and SQLite.
    /// </summary>
    public class SqlDialect : ISqlDialect
    {
        private readonly string _placeholderPrefix;
        private readonly bool _prefixParameterName;

        public SqlDialect(string name, string placeholderPrefix, bool prefixParameterName = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _placeholderPrefix = placeholderPrefix ?? throw new ArgumentNullException(nameof(placeholderPrefix));
            _prefixParameterName = prefixParameterName;
        }

        public string Name { get; }

        public virtual string FormatPlaceholder(string parameterName) => _placeholderPrefix + parameterName;

        public virtual string FormatParameterName(string parameterName) =>
            _prefixParameterName ? _placeholderPrefix + parameterName : parameterName;

        public virtual void PrepareCommand(DbCommand command) { }

        public virtual void PrepareBinaryParameter(DbParameter parameter) { }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Positional <c>?</c> placeholders, for System.Data.Odbc and System.Data.OleDb.
    /// </summary>
    public class PositionalSqlDialect : SqlDialect
    {
        public PositionalSqlDialect(string name) : base(name, "@") { }

        public override string FormatPlaceholder(string parameterName) => "?";
    }

    /// <summary>
    /// Oracle (ODP.NET managed/unmanaged/core, or any provider using <c>:name</c>).
    /// Turns on <c>BindByName</c> and binds session items as a BLOB so values larger
    /// than 32 KB work. Both are done by reflection so there is no compile-time
    /// dependency on an Oracle driver.
    /// </summary>
    public class OracleDialect : SqlDialect
    {
        public OracleDialect() : base("Oracle", ":", prefixParameterName: false) { }

        public override void PrepareCommand(DbCommand command)
        {
            var property = command.GetType().GetProperty("BindByName");
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                property.SetValue(command, true, null);
        }

        public override void PrepareBinaryParameter(DbParameter parameter)
        {
            var property = parameter.GetType().GetProperty("OracleDbType");
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
                return;

            if (Enum.IsDefined(property.PropertyType, "Blob"))
                property.SetValue(parameter, Enum.Parse(property.PropertyType, "Blob"), null);
        }
    }
}
