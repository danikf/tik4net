using System.Diagnostics.CodeAnalysis;

namespace tik4net.Objects
{
    /// <summary>Describes an entity's menu from the router's own grammar.</summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikEntityMenuSchemaExtensions
    {
        /// <summary>
        /// <see cref="TikMenuSchemaExtensions.DescribeMenu(ITikConnection, string)"/> for the menu
        /// <typeparamref name="TEntity"/> maps (<c>IpRoute</c> → <c>/ip/route</c>).
        /// </summary>
        /// <remarks>On WinBox native the entity's WinBox labels name the fields as the API does, which a path alone
        /// cannot (<see cref="TikMenuSchemaSource.WinboxCatalog"/>).</remarks>
        public static TikMenuSchema DescribeMenu<TEntity>(this ITikConnection connection)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            return TikMenuSchemaExtensions.DescribeMenu(connection, metadata.EntityPath, metadata.WinboxLabelsMarker);
        }
    }
}
