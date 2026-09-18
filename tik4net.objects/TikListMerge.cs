using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Objects
{
    /// <summary>
    /// Provides support to merge actual state on mikrotik (list of entities) with expected state (list of entities).
    /// Provides fluent like api to setup merge operation.
    /// <see cref="Save"/> method should be called to perform modifications on mikrotik router.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
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
        // Required fluent setup: SaveInternal refuses to run until WithKey has set it.
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
                object? sourceValue = propInfo.GetValue(source);
                propInfo.SetValue(destination, sourceValue);
            }
        }

        private bool EntityFieldEquals(TEntity entity1, TEntity entity2)
        {
            foreach (var field in _fields)
            {
                PropertyInfo propInfo = ((PropertyInfo)field.Member);

                object? val1 = propInfo.GetValue(entity1);
                object? val2 = propInfo.GetValue(entity2);

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
                yield return attr!.FieldName; // _fields only ever holds members carrying this attribute
            }
        }

        private IEnumerable<string> ResolveJustForInsertFieldNames()
        {
            foreach (var field in _justForInsertFields)
            {
                var attr = ((PropertyInfo)field.Member).GetCustomAttribute<TikPropertyAttribute>(true);
                yield return attr!.FieldName; // _justForInsertFields only ever holds members carrying this attribute
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
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called.</exception>
        /// <remarks>
        /// <para>
        /// <b>Order.</b> On an ordered entity (<c>IsOrdered</c> — firewall filter, mangle, NAT, simple queues…) the
        /// rows end up in the order of 'expected'. Each row moves at most once, the longest run already in order
        /// stays put, and a new row is created in place (<c>place-before</c>) rather than appended and moved. Rows
        /// of the table that are not in the merge keep their positions; the merged rows are placed among them, and
        /// nothing is placed after the section's current last row, because what follows it is not read — when
        /// the last row changes, that can cost one move more than an unrestricted reorder.
        /// </para>
        /// <para>
        /// <b>Dynamic rows</b> (<c>dynamic=true</c>, e.g. the fasttrack counter rule) are never deleted or moved:
        /// RouterOS refuses both. A field change to one is still sent, and the router's refusal surfaces.
        /// </para>
        /// <para>
        /// <b>Merging part of a table.</b> Only rows in 'original' are ever deleted, so load 'original' with the
        /// same filter that describes what 'expected' covers — one chain, one comment tag. When 'expected' is
        /// deliberately partial ("add and update these, delete nothing"), veto the deletes with
        /// <see cref="WithOperationFilter"/>:
        /// <code>
        /// connection.CreateMerge(expected, original)
        ///     .WithKey(r => r.Comment)
        ///     .Field(r => r.Action)
        ///     .WithOperationFilter((operation, oldEntity, newEntity) => operation != MergeOperation.Delete)
        ///     .Save();
        /// </code>
        /// </para>
        /// <para>
        /// <b>A failure part-way</b> leaves the commands already sent in place — RouterOS has no transaction. The
        /// first error propagates; reload 'original' and run the merge again, and it continues from wherever the
        /// router is.
        /// </para>
        /// </remarks>
        /// <seealso cref="Simulate(out int, out int, out int, out int)"/>
        /// <seealso cref="SaveAsync"/>
        public IEnumerable<TEntity> Save()
        {
            var plan = Plan();

            foreach (var entity in plan.Deletes)
            {
                LogDml(MergeOperation.Delete, entity, default(TEntity));
                _connection.Delete(entity);
            }

            foreach (var update in plan.Updates)
            {
                LogDml(MergeOperation.Update, update.Key, update.Value);
                UpdateEntityFields(update.Key, update.Value);
                _connection.Save(update.Key, plan.MergedFieldNames);
            }

            if (plan.OrderSteps != null)
                TikListSync.ApplyOrder(_connection, _metadata, plan.Desired, plan.OrderSteps, plan.CreateFieldNames, LogStep);
            else
                foreach (var entity in plan.Inserts)
                {
                    LogDml(MergeOperation.Insert, default(TEntity), entity);
                    _connection.Save(entity, plan.CreateFieldNames);
                }

            return plan.Result;
        }

        /// <summary>
        /// Async <see cref="Save"/>: the same commands in the same order, sent through the connection's async
        /// surface. Cancellation is checked before each command; the commands already sent stay applied (see the
        /// remarks on <see cref="Save"/>).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of final entities on mikrotik router after save operation (with ids).</returns>
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called.</exception>
        /// <exception cref="OperationCanceledException">Cancelled between two commands.</exception>
        public async Task<IEnumerable<TEntity>> SaveAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = Plan();

            foreach (var entity in plan.Deletes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDml(MergeOperation.Delete, entity, default(TEntity));
                await _connection.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
            }

            foreach (var update in plan.Updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDml(MergeOperation.Update, update.Key, update.Value);
                UpdateEntityFields(update.Key, update.Value);
                await _connection.SaveAsync(update.Key, plan.MergedFieldNames, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (plan.OrderSteps != null)
                await TikListSync.ApplyOrderAsync(_connection, _metadata, plan.Desired, plan.OrderSteps, plan.CreateFieldNames,
                    LogStep, cancellationToken).ConfigureAwait(false);
            else
                foreach (var entity in plan.Inserts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LogDml(MergeOperation.Insert, default(TEntity), entity);
                    await _connection.SaveAsync(entity, plan.CreateFieldNames, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

            return plan.Result;
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
        /// <param name="moveCnt">Number of <c>/move</c> commands <see cref="Save"/> will send (see the remarks on
        /// <see cref="Save"/> for how few). A created item is placed by its create (<c>place-before</c>) and is not
        /// counted here.</param>
        /// <returns>Expected list of final entities on mikrotik router after save operation.</returns>
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called.</exception>
        /// <remarks>Sends nothing, and calls neither log callback; the operation filter is consulted as by <see cref="Save"/>.</remarks>
        /// <seealso cref="Save"/>
        public IEnumerable<TEntity> Simulate(out int insertCnt, out int updateCnt, out int deleteCnt, out int moveCnt)
        {
            var plan = Plan();
            insertCnt = plan.Inserts.Count;
            updateCnt = plan.Updates.Count;
            deleteCnt = plan.Deletes.Count;
            moveCnt = plan.OrderSteps?.Count(s => s.Kind == TikListSyncStepKind.Move) ?? 0;
            return plan.Result;
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
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called.</exception>
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
            return Simulate(out insertCnt, out updateCnt, out deleteCnt, out tmp);
        }

        private void LogStep(TikListSyncStep step, TEntity entity)
        {
            if (step.Kind == TikListSyncStepKind.Create)
                LogDml(MergeOperation.Insert, default(TEntity), entity);
            else
                LogMove(entity, step.OldIndex, step.NewIndex);
        }

        /// <summary>What <see cref="Save"/> sends, decided before anything is — shared by Save, SaveAsync and Simulate.</summary>
        private sealed class MergePlan
        {
            public List<TEntity> Deletes { get; } = new List<TEntity>();
            /// <summary>original → expected.</summary>
            public List<KeyValuePair<TEntity, TEntity>> Updates { get; } = new List<KeyValuePair<TEntity, TEntity>>();
            public List<TEntity> Inserts { get; } = new List<TEntity>();
            /// <summary>Ordered entity: the rows the ordering pass places, in expected order (inserts included).</summary>
            public List<TEntity> Desired { get; } = new List<TEntity>();
            public IReadOnlyList<TikListSyncStep>? OrderSteps { get; set; }
            public List<TEntity> Result { get; } = new List<TEntity>();
            public string[] MergedFieldNames { get; set; } = new string[0];
            public string[] CreateFieldNames { get; set; } = new string[0];
        }

        private MergePlan Plan()
        {
            // The key is the only setup the merge cannot work without: it decides insert vs. update. No Field is a
            // legitimate merge (inserts and deletes only), so that is not refused.
            var keyExtractor = _keyExtractor
                ?? throw new InvalidOperationException(
                    "TikListMerge<" + typeof(TEntity).Name + ">: call WithKey(...) before Save or Simulate — "
                    + "the key decides which router row and which expected row are the same row.");

            var plan = new MergePlan();
            plan.MergedFieldNames = ResolveFieldsFieldNames().ToArray();
            plan.CreateFieldNames = plan.MergedFieldNames.Concat(ResolveJustForInsertFieldNames()).ToArray();

            Dictionary<string, TEntity> expectedDict = _expected.ToDictionaryEx(keyExtractor);
            Dictionary<string, TEntity> originalDict = _original.ToDictionaryEx(keyExtractor);

            //Delete — from the end of the list to the beginning (just for a better show in WinBox). A dynamic row is
            // not the merge's to delete: RouterOS refuses it ("cannot remove builtin").
            foreach (var originalEntityPair in originalDict.Reverse())
            {
                if (!expectedDict.ContainsKey(originalEntityPair.Key)
                    && !TikListSync.IsDynamic(_metadata, originalEntityPair.Value)
                    && _filterCallback(MergeOperation.Delete, originalEntityPair.Value, default(TEntity)))
                    plan.Deletes.Add(originalEntityPair.Value);
            }

            //Insert + Update, in expected order
            var desiredKeys = new List<string?>();
            foreach (var expectedEntityPair in expectedDict)
            {
                if (originalDict.TryGetValue(expectedEntityPair.Key, out var originalEntity))
                {
                    if (!EntityFieldEquals(originalEntity, expectedEntityPair.Value)
                        && _filterCallback(MergeOperation.Update, originalEntity, expectedEntityPair.Value))
                        plan.Updates.Add(new KeyValuePair<TEntity, TEntity>(originalEntity, expectedEntityPair.Value));

                    plan.Result.Add(originalEntity);
                    if (!TikListSync.IsDynamic(_metadata, originalEntity)) // the router refuses to move it, or anything in front of it
                    {
                        plan.Desired.Add(originalEntity);
                        desiredKeys.Add(expectedEntityPair.Key);
                    }
                }
                else
                {
                    if (_filterCallback(MergeOperation.Insert, default(TEntity), expectedEntityPair.Value))
                    {
                        plan.Inserts.Add(expectedEntityPair.Value);
                        plan.Desired.Add(expectedEntityPair.Value);
                        desiredKeys.Add(null);
                    }
                    plan.Result.Add(expectedEntityPair.Value);
                }
            }

            if (_metadata.IsOrdered)
                plan.OrderSteps = TikListSyncPlanner.PlanOrder(desiredKeys, _original.Select(keyExtractor));

            return plan;
        }
    }
}
