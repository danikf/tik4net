using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    public static class EntityObjectsExtensions
    {
        public static IEnumerable<TEntity> CloneEntityList<TEntity>(this IEnumerable<TEntity> originalList)
            where TEntity: new()
        {
            List<TEntity> result = originalList.Select(entity => CloneEntity(entity)).ToList();            

            return result;
        }

        public static TEntity CloneEntity<TEntity>(this TEntity entity) 
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            TEntity result = new TEntity();

            //copy all "field" properties
            foreach(var property in metadata.Properties)
            {
                property.SetEntityValue(result, property.GetEntityValue(entity));
            }

            return result;                      
        }

        public static bool EntityEquals<TEntity>(this TEntity entity1, TEntity entity2)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            foreach (var property in metadata.Properties)
            {
                string prop1 = property.GetEntityValue(entity1);
                string prop2 = property.GetEntityValue(entity2);

                if (!string.Equals(prop1, prop2))
                    return false;
            }

            return true;
        }
    }
}
