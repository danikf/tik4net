using System;
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
    /// <para>
    /// The store is a <see cref="ConcurrentDictionary{TKey,TValue}"/> rather than a plain
    /// <see cref="Dictionary{TKey,TValue}"/> because the fast path reads it <b>outside</b> the lock. A plain
    /// dictionary is not safe for a concurrent read against a write: a read arriving while another thread's
    /// <c>Add</c> is resizing the buckets can return the wrong entry or spin forever, and neither failure
    /// looks like a threading bug when it finally surfaces. The lock is kept on the build path so the
    /// metadata is constructed exactly once — <c>GetOrAdd</c> alone would let two threads both build one and
    /// hand different instances to different callers.
    /// </para>
    /// </remarks>
    /// <seealso cref="TikEntityAttribute"/>
    /// <seealso cref="TikPropertyAttribute"/>
    /// <seealso cref="TikEntityMetadata"/>
    public static class TikEntityMetadataCache
    {
        private static readonly object _lockObj = new object();
        private static readonly ConcurrentDictionary<Type, TikEntityMetadata> _cache
            = new ConcurrentDictionary<Type, TikEntityMetadata>();

        /// <summary>
        /// Gets (or creates new) <typeparamref name="TEntity"/> metadata via reflection of its attributes.
        /// </summary>
        /// <typeparam name="TEntity">Type of the entity.</typeparam>
        /// <returns>Entity metadata used by entity mapper.</returns>
        public static TikEntityMetadata GetMetadata<TEntity>()
        {
            Type key = typeof(TEntity);
            TikEntityMetadata? result;

            if (!_cache.TryGetValue(key, out result))
            {
                lock (_lockObj)
                {
                    if (!_cache.TryGetValue(key, out result))
                    {
                        result = new TikEntityMetadata(typeof(TEntity));
                        _cache[key] = result;
                    }
                }
            }
            return result!; // every branch above sets result before this point
        }        
    }
}
