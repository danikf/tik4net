using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    public static class TikEntityMetadataCache
    {
        private static readonly object _lockObj = new object();
        private static Dictionary<Type, TikEntityMetadata> _cache = new Dictionary<Type, TikEntityMetadata>();

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
