using System;
using System.Configuration.Provider;
using System.Data.Common;
using System.Reflection;

namespace Ark.SessionState.AdoNet
{
    /// <summary>
    /// Finds the ADO.NET <see cref="DbProviderFactory"/> to use.
    /// </summary>
    internal static class ProviderFactoryResolver
    {
        public const string DefaultProviderName = "System.Data.SqlClient";

        /// <param name="factoryTypeName">
        /// Optional assembly-qualified name of a DbProviderFactory type (e.g.
        /// "Npgsql.NpgsqlFactory, Npgsql"). Its static <c>Instance</c> field is used.
        /// This avoids having to register the provider under &lt;system.data&gt;.
        /// </param>
        /// <param name="providerName">
        /// A provider invariant name registered in machine.config or web.config
        /// (&lt;system.data&gt;&lt;DbProviderFactories&gt;). Defaults to System.Data.SqlClient.
        /// </param>
        public static DbProviderFactory Resolve(string factoryTypeName, string providerName)
        {
            if (!string.IsNullOrWhiteSpace(factoryTypeName))
                return FromType(factoryTypeName.Trim());

            var invariantName = string.IsNullOrWhiteSpace(providerName) ? DefaultProviderName : providerName.Trim();
            try
            {
                return DbProviderFactories.GetFactory(invariantName);
            }
            catch (ArgumentException ex)
            {
                throw new ProviderException(
                    $"ADO.NET provider '{invariantName}' is not registered. Register it under " +
                    "<system.data><DbProviderFactories> in web.config, or set the provider's " +
                    "'providerFactoryType' attribute to the factory's assembly-qualified type name.", ex);
            }
        }

        private static DbProviderFactory FromType(string typeName)
        {
            Type type;
            try
            {
                type = Type.GetType(typeName, throwOnError: true);
            }
            catch (Exception ex)
            {
                throw new ProviderException($"Could not load provider factory type '{typeName}'.", ex);
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            var value = type.GetField("Instance", flags)?.GetValue(null)
                        ?? type.GetProperty("Instance", flags)?.GetValue(null, null);

            if (value is DbProviderFactory factory)
                return factory;

            throw new ProviderException(
                $"Type '{type.FullName}' must be a DbProviderFactory with a public static 'Instance' field or property.");
        }
    }
}
