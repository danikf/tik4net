using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Provides support to merge actual state on mikrotik (list of entities) with expected state (list of entities).
    /// Provides fluent like api to setup merge operation.
    /// <see cref="Save"/> method should be called to perform modifications on mikrotik router.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    public class TikListMerge<TEntity>
                    where TEntity : new()
    {
        /// <summary>
        /// Operation performed during merge process with single entity.
        /// </summary>
        public enum MergeOperation
        {
            /// <summary>
            /// Create new entity
            /// </summary>
            Insert,
            /// <summary>
            /// Update existing entity
            /// </summary>
            Update,
            /// <summary>
            /// Delete existing entity
            /// </summary>
            Delete,
        };

        private readonly ITikConnection _connection;
        private readonly IEnumerable<TEntity> _expected;
        private readonly IEnumerable<TEntity> _original;
        private readonly TikEntityMetadata _metadata;
        // Required fluent setup: SaveInternal dereferences this unconditionally, so it must be set via
        // WithKey before Save/Simulate is called - nullable here only because the compiler cannot see that
        // ordering; there is no runtime guard against calling Save without WithKey first.
        private Func<TEntity, string>? _keyExtractor;
        private Action<MergeOperation, TEntity?, TEntity?>? _dmlLogCallback; //<MergeOperation, oldEntity, newEntity>
        private Action<TEntity, int, int>? _moveLogCallback; //<Entity, oldIndex, newIndex>
        // oldEntity/newEntity are null for the side that doesn't exist: Insert has no oldEntity, Delete has no newEntity.
        private Func<MergeOperation, TEntity?, TEntity?, bool> _filterCallback = (operation, oldE, newE) => true; //default filter - process all
        private readonly List<MemberExpression> _fields = new List<MemberExpression>();
        private readonly List<MemberExpression> _justForInsertFields = new List<MemberExpression>();

        internal TikListMerge(ITikConnection connection, IEnumerable<TEntity> expected, IEnumerable<TEntity> original)
        {
            _connection = connection;
            _metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            _expected = expected;
            _original = original;
        }

        private static MemberExpression EnsureBodyIsMemberExpression<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        {
            MemberExpression? memberExpression = fieldExpression.Body as MemberExpression;

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
        /// Register DML log callback {operation, oldValue, newValue} - called on each DML operation.
        /// </summary>
        /// <param name="dmlLogCallback">log callback called on each DML operation - {operation, oldValue, newValue}</param>
        /// <returns>this (fluent like API)</returns>
        public TikListMerge<TEntity> WithDmlLogCallback(Action<MergeOperation, TEntity?, TEntity?> dmlLogCallback)
        {
            _dmlLogCallback = dmlLogCallback;

            return this;
        }

        /// <summary>
        /// Register move log callback {entity, oldIndex, newIndex} - called on each move operation.
        /// </summary>
        /// <param name="moveLogCallback">log callback called on each move operation - {entity, oldIndex, newIndex}</param>
        /// <returns>this (fluent like API)</returns>
        public TikListMerge<TEntity> WithMoveLogCallback(Action<TEntity, int, int> moveLogCallback)
        {
            _moveLogCallback = moveLogCallback;

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

        /// <summary>
        /// Defines field that will be used just when creating new instance of entity (<see cref="MergeOperation.Insert"/>) (not used for update and compare).
        /// </summary>
        /// <typeparam name="TProperty">Field property.</typeparam>
        /// <param name="fieldExpression">Field extraction expression. example: (entity=}entity.Name)</param>
        /// <returns>this (fluent like API)</returns>
        public TikListMerge<TEntity> JustForInsertField<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        {
            _justForInsertFields.Add(EnsureBodyIsMemberExpression(fieldExpression));

            return this;
        }

        /// <summary>
        /// Register filter callback {operation, oldValue, newValue} - called on each DML operation. Operation will be performed only if true is returned. Otherwise DML operation will be skipped.
        /// </summary>
        /// <param name="filterCallback">log callback called on each DML operation - {operation, oldValue, newValue}. Operation will be performed only if true is returned. Otherwise DML operation will be skipped.</param>
        /// <returns>this (fluent like API)</returns>
        public TikListMerge<TEntity> WithOperationFilter(Func<MergeOperation, TEntity?, TEntity?, bool> filterCallback)
        {
            _filterCallback = filterCallback;

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

        private IEnumerable<string> ResolveFieldsFieldNames()
        {
            foreach(var field in _fields)
            {
                var attr = ((PropertyInfo)field.Member).GetCustomAttribute<TikPropertyAttribute>(true);
                yield return attr.FieldName;
            }
        }

        private IEnumerable<string> ResolveJustForInsertFieldNames()
        {
            foreach (var field in _justForInsertFields)
            {
                var attr = ((PropertyInfo)field.Member).GetCustomAttribute<TikPropertyAttribute>(true);
                yield return attr.FieldName;
            }
        }


        private void LogDml(MergeOperation operation, TEntity? oldEntity, TEntity? newEntity)
        {
            if (_dmlLogCallback != null)
                _dmlLogCallback(operation, oldEntity, newEntity);
        }

        private void LogMove(TEntity entity, int oldIndex, int newIndex)
        {
            if (_moveLogCallback != null)
                _moveLogCallback(entity, oldIndex, newIndex);
        }

        /// <summary>
        /// Performs update operations on mikrotik router.
        /// Items which are present in 'expected' and are not present in 'original' will be created on mikrotik router.
        /// Items which are present in both 'expected' and 'original' will be compared and updated (if are different - see <see cref="Field"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be deleted from mikrotik router.
        /// </summary>
        /// <returns>List of final entities on mikrotik router after save operation (with ids).</returns>
        /// <seealso cref="Simulate(out int, out int, out int, out int)"/>
        public IEnumerable<TEntity> Save()
        {
            int insertCnt, updateCnt, deleteCnt, moveCnt;

            return SaveInternal(false, out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
        }

        /// <summary>
        /// Calculate update operations on mikrotik router.
        /// Items which are present in 'expected' and are not present in 'original' will be counted as created.
        /// Items which are present in both 'expected' and 'original' will be compared and counted as updated (if are different - see <see cref="Field"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be counted as deleted.
        /// </summary>
        /// <param name="insertCnt">Number of items to be created.</param>
        /// <param name="updateCnt">Number of items to be updated.</param>
        /// <param name="deleteCnt">Number of items to be deleted.</param>
        /// <param name="moveCnt">Number of items to be moved (note: inserted items are always inserted at the end of list and moved to the right place by move operation).</param>
        /// <returns>Expected list of final entities on mikrotik router after save operation.</returns>
        /// <seealso cref="Save"/>
        public IEnumerable<TEntity> Simulate(out int insertCnt, out int updateCnt, out int deleteCnt, out int moveCnt)
        {
            return SaveInternal(true, out insertCnt, out updateCnt, out deleteCnt, out moveCnt);
        }

        /// <summary>
        /// Calculate update operations on mikrotik router.
        /// Items which are present in 'expected' and are not present in 'original' will be counted as created.
        /// Items which are present in both 'expected' and 'original' will be compared and counted as updated (if are different - see <see cref="Field"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be counted as deleted.
        /// </summary>
        /// <param name="insertCnt">Number of items to be created.</param>
        /// <param name="updateCnt">Number of items to be updated.</param>
        /// <param name="deleteCnt">Number of items to be deleted.</param>
        /// <returns>Expected list of final entities on mikrotik router after save operation.</returns>
        /// <remarks>
        /// This overload hides the move count. On an ordered entity (<c>IsOrdered</c> — firewall filter, mangle,
        /// NAT…) <see cref="Save"/> still reorders the rows, so a run that only changes the ORDER reports 0/0/0
        /// here and is easily mistaken for "nothing to do". Prefer
        /// <see cref="Simulate(out int, out int, out int, out int)"/> for those entities.
        /// </remarks>
        /// <seealso cref="Save"/>
        public IEnumerable<TEntity> Simulate(out int insertCnt, out int updateCnt, out int deleteCnt)
        {
            int tmp;
            return SaveInternal(true, out insertCnt, out updateCnt, out deleteCnt, out tmp);
        }




        private IEnumerable<TEntity> SaveInternal(bool simulateOnly, out int insertCnt, out int updateCnt, out int deleteCnt, out int moveCnt)
        {
            insertCnt = 0;
            updateCnt = 0;
            deleteCnt = 0;
            moveCnt = 0;

            //TODO ensure all fields set                
            List<TEntity> result = new List<TEntity>();
            // _keyExtractor!: required fluent setup - see the field's declaration comment.
            Dictionary<string, TEntity> expectedDict = _expected.ToDictionaryEx(_keyExtractor!);
            Dictionary<string, TEntity> originalDict = _original.ToDictionaryEx(_keyExtractor!);

            // Keys in the order the router currently holds them, kept up to date as this merge deletes, inserts
            // and moves. Shared with SaveListDifferences, which reorders the same way — see TikOrderTracker for
            // why the check has to be against the current order rather than the starting one.
            var currentOrder = new TikOrderTracker(_original.Select(_keyExtractor!));

            //Delete
            foreach (var originalEntityPair in originalDict.Reverse()) //delete from end to begining of the list (just for better show in WinBox)
            {
                if (!expectedDict.ContainsKey(originalEntityPair.Key)) //present in original + not present in expected => delete
                {
                    if (_filterCallback(MergeOperation.Delete, originalEntityPair.Value, default(TEntity)))
                    {
                        if (!simulateOnly)
                        {
                            LogDml(MergeOperation.Delete, originalEntityPair.Value, default(TEntity));
                            _connection.Delete(originalEntityPair.Value);
                        }
                        currentOrder.Remove(originalEntityPair.Key);
                        deleteCnt++;
                    }
                }
            }

            //Insert+Update
            var mergedFieldNames = ResolveFieldsFieldNames().ToArray();
            var insertedFieldNames = ResolveJustForInsertFieldNames().ToArray();
            foreach (var expectedEntityPair in expectedDict.Reverse()) //from last to first ( <= move is indexed as moveBeforeEntity)
            {
                TEntity originalEntity;
                TEntity resultEntity;
                if (originalDict.TryGetValue(expectedEntityPair.Key, out originalEntity))
                { //Update //present in both expected and original => update or NOOP
                  //copy .id from original to expected & save
                    if (!EntityFieldEquals(originalEntity, expectedEntityPair.Value)) //modified
                    {
                        if (_filterCallback(MergeOperation.Update, originalEntity, expectedEntityPair.Value))
                        {
                            if (!simulateOnly)
                            {
                                LogDml(MergeOperation.Update, originalEntity, expectedEntityPair.Value);
                                UpdateEntityFields(originalEntity, expectedEntityPair.Value);
                                _connection.Save(originalEntity, mergedFieldNames);
                            }
                            updateCnt++;
                        }
                    }
                    resultEntity = originalEntity;
                }
                else
                { //Insert //present in expected and not present in original => insert
                    if (_filterCallback(MergeOperation.Insert, default(TEntity), expectedEntityPair.Value))
                    {
                        if (!simulateOnly)
                        {
                            LogDml(MergeOperation.Insert, default(TEntity), expectedEntityPair.Value);
                            _connection.Save(expectedEntityPair.Value, mergedFieldNames.Concat(insertedFieldNames));
                        }
                        currentOrder.Append(expectedEntityPair.Key); //the router appends new items - the move below puts it in place
                        insertCnt++;
                    }
                    resultEntity = expectedEntityPair.Value;
                }

                //Move entity to the right position
                if (_metadata.IsOrdered)
                {
                    if (result.Count > 0) // last one in the list (first taken) should be just added/leavedOnPosition and the next should be moved before the one which was added immediatelly before <=> result[0]
                    {
                        string movedKey = _keyExtractor!(resultEntity);
                        string anchorKey = _keyExtractor!(result[0]);
                        int movedIdx, anchorIdx;

                        // only if is in different position (is not immediately before result[0] right now)
                        if (currentOrder.NeedsMove(movedKey, anchorKey, out movedIdx, out anchorIdx))
                        {
                            if (!simulateOnly)
                            {
                                LogMove(resultEntity, movedIdx, anchorIdx);
                                _connection.Move(resultEntity, result[0]); //before lastly added entity (foreach in reversed order)
                            }
                            moveCnt++;

                            currentOrder.ApplyMove(movedKey, anchorKey);
                        }
                    }
                }

                result.Insert(0, resultEntity); //foreach in reversed order => put as first in result list
            }

            return result;
        }
    }
}
