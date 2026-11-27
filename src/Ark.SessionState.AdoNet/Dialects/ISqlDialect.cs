using System.Data.Common;

namespace Ark.SessionState.AdoNet.Dialects
{
    /// <summary>
    /// Describes the small set of differences between ADO.NET providers that the
    /// session store needs to know about. The SQL itself is plain ANSI SQL; a dialect
    /// only controls how parameters are written and lets a provider tweak commands.
    /// </summary>
    /// <remarks>
    /// Implement this interface (with a public parameterless constructor) to support a
    /// database the built-in dialects don't cover, then set the provider's
    /// <c>dialect</c> attribute to the type's assembly-qualified name.
    /// Parameters are always added to commands in the order they appear in the SQL,
    /// so positional (<c>?</c>) placeholders work.
    /// </remarks>
    public interface ISqlDialect
    {
        /// <summary>A short display name, e.g. "SqlServer".</summary>
        string Name { get; }

        /// <summary>The placeholder written into SQL text for a parameter, e.g. <c>@SessionId</c>, <c>:SessionId</c> or <c>?</c>.</summary>
        string FormatPlaceholder(string parameterName);

        /// <summary>The value assigned to <see cref="DbParameter.ParameterName"/>.</summary>
        string FormatParameterName(string parameterName);

        /// <summary>Called for every command before parameters are added.</summary>
        void PrepareCommand(DbCommand command);

        /// <summary>Called for the binary (session items) parameter after its DbType and value are set.</summary>
        void PrepareBinaryParameter(DbParameter parameter);
    }
}
