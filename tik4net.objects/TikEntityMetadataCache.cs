using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Cache for extracted metadata about mikrotik entities for entity mapper.
    /// Main reason is to improve performance via caching slow reflection operations.
    /// </summary>
    /// <remarks>
    /// Thread-safe: a type's metadata is built at most once, however many threads ask for it at once, and
    /// every caller gets the same instance.
    /// </remarks>
    /// <seealso cref="TikEntityAttribute"/>
    /// <seealso cref="TikPropertyAttribute"/>
    /// <seealso cref="TikEntityMetadata"/>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class TikEntityMetadataCache
    {
        private static readonly ConcurrentDictionary<Type, Lazy<TikEntityMetadata>> _cache
            = new ConcurrentDictionary<Type, Lazy<TikEntityMetadata>>();

        /// <summary>
        /// Gets (or creates new) <typeparamref name="TEntity"/> metadata via reflection of its attributes.
        /// </summary>
        /// <typeparam name="TEntity">Type of the entity.</typeparam>
        /// <returns>Entity metadata used by entity mapper.</returns>
        public static TikEntityMetadata GetMetadata<TEntity>()
            => _cache.GetOrAdd(typeof(TEntity), type => new Lazy<TikEntityMetadata>(() => new TikEntityMetadata(type))).Value;
    }
}
