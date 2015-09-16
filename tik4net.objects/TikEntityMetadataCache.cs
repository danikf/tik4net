using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Cache for extracted metadata about mikrotik entities for entity mapper.
    /// Main reason is to improve performance via caching slow reflection operations.
    /// </summary>
    /// <remarks>Cache is thread-safe.</remarks>
    /// <seealso cref="TikEntityAttribute"/>
    /// <seealso cref="TikPropertyAttribute"/>
    /// <seealso cref="TikEntityMetadata"/>
    public static class TikEntityMetadataCache
    {
        private static readonly object _lockObj = new object();
        private static Dictionary<Type, TikEntityMetadata> _cache = new Dictionary<Type, TikEntityMetadata>();

        /// <summary>
        /// Gets (or creates new) <typeparamref name="TEntity"/> metadata via reflection of its attributes.
        /// </summary>
        /// <typeparam name="TEntity">Type of the entity.</typeparam>
        /// <returns>Entity metadata used by entity mapper.</returns>
        public static TikEntityMetadata GetMetadata<TEntity>()
        {
            Type key = typeof(TEntity);
            TikEntityMetadata result;

            if (!_cache.TryGetValue(key, out result))
            {
                lock (_lockObj)
                {
                    if (!_cache.TryGetValue(key, out result))
                    {
                        result = new TikEntityMetadata(typeof(TEntity));
                        _cache.Add(key, result);

                    }
                }
            }
            return result;
        }        
    }
}
