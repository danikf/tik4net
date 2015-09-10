using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    /// <summary>
    /// Provides support to merge actual state on mikrotik (list of entities) with expected state (list of entities).
    /// Provides fluent like api to setup merge operation.
    /// <see cref="Save"/> method should be called to perform modifications on mikrotik router.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    public class TikListMerge<TEntity>
    {
        private readonly ITikConnection _connection;
        private readonly IEnumerable<TEntity> _expected;
        private readonly IEnumerable<TEntity> _original;
        private readonly TikEntityMetadata _metadata;
        private Func<TEntity, string> _keyExtractor;
        private readonly List<MemberExpression> _fields = new List<MemberExpression>();

        internal TikListMerge(ITikConnection connection, IEnumerable<TEntity> expected, IEnumerable<TEntity> original)
        {
            _connection = connection;
            _metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            _expected = expected;
            _original = original;
        }

        private static MemberExpression EnsureBodyIsMemberExpression<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        {
            MemberExpression memberExpression = fieldExpression.Body as MemberExpression;

            if (memberExpression == null)
                throw new ArgumentException("Given expression must be MemberExpression.", "fieldExpression");

            return memberExpression;
        }

        //private static TikPropertyAttribute EnsureTikProperty<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        //{
        //    var memberExpression = EnsureBodyIsMemberExpression(fieldExpression);

        //    TikPropertyAttribute attr = memberExpression.Type.CustomAttributes.OfType<TikPropertyAttribute>().Single(); //TODO check and better exception
        //    return attr;
        //}


        /// <summary>
        /// Defines string representation of entity key (entities are the same, if extracted key has the same value).
        /// </summary>
        /// <param name="keyExtractor">Func to extract key value from entity</param>
        /// <returns>this (fluent like API)</returns>
        public TikListMerge<TEntity> WithKey(Func<TEntity, string> keyExtractor)
        {
            _keyExtractor = keyExtractor;
            return this;
        }

        /// <summary>
        /// Defines field that will be merged (only defined fields will be compared and updated).
        /// </summary>
        /// <typeparam name="TProperty">Field property.</typeparam>
        /// <param name="fieldExpression">Field extraction expression. example: (entity=}entity.Name)</param>
        /// <returns>this (fluent like API)</returns>
        public TikListMerge<TEntity> Field<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        {
            _fields.Add(EnsureBodyIsMemberExpression(fieldExpression));

            return this;
        }

        private void UpdateEntityFields(TEntity destination, TEntity source)
        {
            foreach (var field in _fields)
            {
                PropertyInfo propInfo = ((PropertyInfo)field.Member);
                object sourceValue = propInfo.GetValue(source);
                propInfo.SetValue(destination, sourceValue);
            }
        }

        private bool EntityFieldEquals(TEntity entity1, TEntity entity2)
        {
            foreach (var field in _fields)
            {
                PropertyInfo propInfo = ((PropertyInfo)field.Member);

                object val1 = propInfo.GetValue(entity1);
                object val2 = propInfo.GetValue(entity2);

                if (Convert.ToString(val1) != Convert.ToString(val2))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Performs update operations on mikrotik router.
        /// Items which are present in 'expected' and are not present in 'original' will be created on mikrotik router.
        /// Items which are present in both 'expected' and 'original' will be compared and updated (if are different - see <see cref="Field"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be deleted from mikrotik router.
        /// </summary>
        /// <returns>List of final entities on mikrotik router after save operation.</returns>
        public IEnumerable<TEntity> Save()
        {
            //TODO ensure all fields set                
            List<TEntity> result = new List<TEntity>();
            Dictionary<string, TEntity> expectedDict = _expected.ToDictionary(_keyExtractor);
            Dictionary<string, TEntity> originalDict = _original.ToDictionary(_keyExtractor);

            foreach (var expectedEntityPair in expectedDict)
            {
                TEntity originalEntity;
                if (originalDict.TryGetValue(expectedEntityPair.Key, out originalEntity))
                { //Update //present in both expected and original => update or NOOP
                  //copy .id from original to expected & save
                    if (!EntityFieldEquals(originalEntity, expectedEntityPair.Value)) //modified
                    {
                        UpdateEntityFields(originalEntity, expectedEntityPair.Value);
                        _connection.Save(originalEntity);
                    }
                    result.Add(originalEntity);
                }
                else
                { //Insert //present in expected and not present in original => insert
                    _connection.Save(expectedEntityPair.Value);
                    result.Add(expectedEntityPair.Value);
                }
            }

            //Delete
            foreach (var originalEntityPair in originalDict)
            {
                if (!expectedDict.ContainsKey(originalEntityPair.Key)) //present in original + not present in expected => delete
                    _connection.Delete(originalEntityPair.Value);
            }

            return result;
        }
    }
}
