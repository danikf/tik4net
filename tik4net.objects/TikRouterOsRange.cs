using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace tik4net.Objects
{
    /// <summary>
    /// Which RouterOS releases have a mapped menu or field, by its <see cref="TikEntityAttribute.MinRouterOs"/> and
    /// <see cref="TikEntityAttribute.MaxRouterOs"/> (and the same pair on <see cref="TikPropertyAttribute"/>).
    /// </summary>
    /// <remarks>
    /// The bounds are what the lab measured, so a release outside them can still have the menu — <c>"7"</c> means "in
    /// 7.x, not in 6.49", not "since 7.0". A bound is compared on as many parts as it has: <c>"7"</c> takes every 7.x,
    /// <c>"7.22"</c> every 7.22.x and later.
    /// </remarks>
    public static class TikRouterOsRange
    {
        /// <summary>
        /// Whether <paramref name="routerOs"/> is within <paramref name="minRouterOs"/> and <paramref name="maxRouterOs"/>
        /// (either <c>null</c> for no bound).
        /// </summary>
        /// <param name="minRouterOs">The oldest release known to have it (<c>"7"</c>, <c>"7.22"</c>), or <c>null</c>.</param>
        /// <param name="maxRouterOs">The newest release known to have it (<c>"6"</c>, <c>"7.21"</c>), or <c>null</c>.</param>
        /// <param name="routerOs">The router's version (<c>/system/resource</c> <c>version</c>, without its channel).</param>
        public static bool Includes(string? minRouterOs, string? maxRouterOs, Version routerOs)
        {
            Guard.ArgumentNotNull(routerOs, nameof(routerOs));
            return (minRouterOs == null || Compare(routerOs, minRouterOs) >= 0)
                && (maxRouterOs == null || Compare(routerOs, maxRouterOs) <= 0);
        }

        /// <summary>
        /// Whether <paramref name="routerOs"/> has the menu of <typeparamref name="TEntity"/> and, when
        /// <paramref name="propertyName"/> is given, that property's field.
        /// </summary>
        /// <param name="routerOs">The router's version.</param>
        /// <param name="propertyName">A property of <typeparamref name="TEntity"/> (<c>nameof(FirewallFilter.Realm)</c>), or
        /// <c>null</c> for the menu alone.</param>
        /// <exception cref="ArgumentException"><typeparamref name="TEntity"/> is not an entity, or has no such mapped
        /// property.</exception>
        [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
        [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
        public static bool Has<TEntity>(Version routerOs, string? propertyName = null)
        {
            var entity = typeof(TEntity).GetCustomAttribute<TikEntityAttribute>()
                ?? throw new ArgumentException(typeof(TEntity).Name + " has no [TikEntity].", nameof(TEntity));
            if (!Includes(entity.MinRouterOs, entity.MaxRouterOs, routerOs))
                return false;
            if (propertyName == null)
                return true;
            var field = typeof(TEntity).GetProperty(propertyName)?.GetCustomAttribute<TikPropertyAttribute>()
                ?? throw new ArgumentException(typeof(TEntity).Name + " has no mapped property " + propertyName + ".", nameof(propertyName));
            return Includes(field.MinRouterOs, field.MaxRouterOs, routerOs);
        }

        // On the bound's own parts: "7" against 7.24.5 compares 7 with 7.
        private static int Compare(Version routerOs, string bound)
        {
            string[] parts = bound.Split('.');
            int[] version = { routerOs.Major, routerOs.Minor, routerOs.Build };
            for (int i = 0; i < parts.Length; i++)
            {
                int part = int.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture);
                int have = i < version.Length ? Math.Max(0, version[i]) : 0;
                if (have != part)
                    return have.CompareTo(part);
            }
            return 0;
        }
    }
}
