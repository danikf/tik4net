using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects.Tracking;

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
        private readonly List<MergeField> _fields = new List<MergeField>();
        private readonly List<TikEntityPropertyAccessor> _justForInsertFields = new List<TikEntityPropertyAccessor>();

        internal TikListMerge(ITikConnection connection, IEnumerable<TEntity> expected, IEnumerable<TEntity> original)
        {
            _connection = connection;
            _metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            _expected = expected;
            _original = original;
        }

        /// <summary>A merged field: its property, and what makes (expected value, current value) into the value the row gets.</summary>
        private sealed class MergeField
        {
            public MergeField(TikEntityPropertyAccessor accessor, Func<object?, object?, object?>? merge)
            {
                Accessor = accessor;
                Merge = merge;
            }

            public TikEntityPropertyAccessor Accessor { get; }
            public Func<object?, object?, object?>? Merge { get; }
        }

        // The mapped property the expression names. Anything else is refused here, where the mistake is, and not at Save.
        private TikEntityPropertyAccessor ResolveField<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        {
            MemberExpression? memberExpression = fieldExpression.Body as MemberExpression;

            if (memberExpression == null)
                throw new ArgumentException("Given expression must be MemberExpression.", nameof(fieldExpression));
            if (!(memberExpression.Expression is ParameterExpression))
                throw new ArgumentException("The field '" + fieldExpression.Body + "' is not a property of "
                    + typeof(TEntity).Name + " itself: name the mapped property.", nameof(fieldExpression));

            return _metadata.Properties.FirstOrDefault(p => p.PropertyName == memberExpression.Member.Name)
                ?? throw new ArgumentException("The property '" + memberExpression.Member.Name + "' of " + typeof(TEntity).Name
                    + " has no [TikProperty]: a merge can only compare and write a mapped field.", nameof(fieldExpression));
        }

        // A field declared again replaces its earlier declaration, in its place.
        private TikListMerge<TEntity> AddField(TikEntityPropertyAccessor accessor, Func<object?, object?, object?>? merge)
        {
            var field = new MergeField(accessor, merge);
            int declared = _fields.FindIndex(f => f.Accessor == accessor);
            if (declared >= 0)
                _fields[declared] = field;
            else
                _fields.Add(field);
            return this;
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
        /// <exception cref="ArgumentException">The expression does not name a mapped property of the entity.</exception>
        /// <remarks>
        /// A field compares by the form the router is sent, so a list compares in order: the first DNS server a DHCP client
        /// gets is its primary. The exceptions are a list of an enum's members (<c>connection-state</c>, <c>tcp-flags</c>),
        /// which RouterOS keeps as a bitmask and prints in its own order, and a list declared
        /// <see cref="TikPropertyAttribute.RouterSortsMembers"/> (the radio chain lists), which RouterOS prints sorted: the
        /// same members in any order are no change. A plain <c>string</c> field that holds such a set has to be spelled in
        /// the router's order, or the merge updates the row on every run.
        /// </remarks>
        public TikListMerge<TEntity> Field<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
            => AddField(ResolveField(fieldExpression), null);

        /// <summary>
        /// Defines a merged field whose value the row gets from <paramref name="merge"/> — <c>(expected, current) =&gt;
        /// result</c> — instead of from the expected row as it is. It decides the comparison too: a row whose merged value
        /// equals its current one is not updated. It runs once per row that is in both lists.
        /// </summary>
        /// <remarks>
        /// By default a field takes the expected value, so an expected row that does not carry a field
        /// (<see cref="TikFieldState.Absent"/>) unsets it on the router. When the expected rows come from another RouterOS
        /// version, which may simply lack the field, keep the current value instead:
        /// <code>.Field(e =&gt; e.Comment, (expected, current) =&gt; expected.IfAbsent(current))</code>
        /// When the router does not print a field on some rows because it does not apply to them (mangle
        /// <c>passthrough</c> on a <c>jump</c>), an expected value never matches there and every run updates the row;
        /// leave such rows alone:
        /// <code>.Field(e =&gt; e.Passthrough, (expected, current) =&gt; expected.IfPrintedIn(current))</code>
        /// </remarks>
        /// <typeparam name="TProperty">Field property.</typeparam>
        /// <param name="fieldExpression">Field extraction expression. example: (entity=}entity.Name)</param>
        /// <param name="merge">The value the row gets, from the expected and the current value.</param>
        /// <returns>this (fluent like API)</returns>
        /// <exception cref="ArgumentException">The expression does not name a mapped property of the entity.</exception>
        public TikListMerge<TEntity> Field<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression,
            Func<TProperty, TProperty, TProperty> merge)
            => AddField(ResolveField(fieldExpression), (expected, current) => merge((TProperty)expected!, (TProperty)current!));

        /// <summary>
        /// Defines field that will be used just when creating new instance of entity (<see cref="MergeOperation.Insert"/>) (not used for update and compare).
        /// </summary>
        /// <typeparam name="TProperty">Field property.</typeparam>
        /// <param name="fieldExpression">Field extraction expression. example: (entity=}entity.Name)</param>
        /// <returns>this (fluent like API)</returns>
        /// <exception cref="ArgumentException">The expression does not name a mapped property of the entity.</exception>
        public TikListMerge<TEntity> JustForInsertField<TProperty>(Expression<Func<TEntity, TProperty>> fieldExpression)
        {
            _justForInsertFields.Add(ResolveField(fieldExpression));

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

        // What each field gets on `current` from `expected`, in the order of _fields: the expected value, or what the
        // field's merge makes of it. Worked out once per row, so a merge callback runs once.
        private object?[] MergedValues(TEntity current, TEntity expected)
        {
            var values = new object?[_fields.Count];
            for (int i = 0; i < values.Length; i++)
            {
                var field = _fields[i];
                object? value = field.Accessor.GetRaw(expected!);
                values[i] = field.Merge != null ? field.Merge(value, field.Accessor.GetRaw(current!)) : value;
            }
            return values;
        }

        private bool HasMergedValues(TEntity row, object?[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (!_fields[i].Accessor.MergeEquals(_fields[i].Accessor.GetRaw(row!), values[i]))
                    return false;
            return true;
        }

        private void WriteMergedValues(TEntity destination, object?[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                var accessor = _fields[i].Accessor;
                object? merged = values[i];
                // The merge's rule is that an Absent expected value unsets the field, while Save leaves an Absent one
                // alone - so it is written as the assigned null it means here. Onto a row that does not print the field
                // it stays Absent: there is nothing to unset, and IfPrintedIn's "does not apply to this row" says so.
                if (merged is ITikField wrapped && wrapped.State == TikFieldState.Absent
                    && accessor.GetRaw(destination!) is ITikField current && current.State != TikFieldState.Absent)
                    merged = accessor.AssignedNull();
                accessor.SetRaw(destination!, merged);
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
        /// Items which are present in both 'expected' and 'original' will be compared and updated (if are different - see <see cref="Field{TProperty}(Expression{Func{TEntity, TProperty}})"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be deleted from mikrotik router.
        /// </summary>
        /// <returns>The rows on the router after the save, as far as this merge knows them (see the remarks).</returns>
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called, or the entity does
        /// not offer a verb the merge needs (see the remarks). Nothing has been sent.</exception>
        /// <remarks>
        /// <para>
        /// <b>The result.</b> The rows of 'original' that were not deleted, with their new values, and the rows that were
        /// inserted, with their ids. A row whose delete the filter vetoed and a dynamic row left out of 'expected' are
        /// there, because they stay on the router; a row whose insert the filter vetoed is not. The rows of 'expected'
        /// come first, in that order, and the rows only in 'original' follow them, in theirs. That is not the
        /// router's order when those rows sit among the others: reload when the position matters.
        /// </para>
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
        /// router is. A verb the entity does not offer (<see cref="TikEntityAttribute.SupportedOperations"/>) and,
        /// with <see cref="TikConnectionSetup.ValidateWrites"/>, a field the router does not take are refused before
        /// the first command — and by <see cref="Simulate(out int, out int, out int, out int)"/>, which refuses what
        /// <c>Save</c> would. A missing <c>move</c> is refused at the end of the ordering pass: the creates are sent,
        /// the moves are skipped, and every row exists with only its order wrong.
        /// </para>
        /// </remarks>
        /// <exception cref="TikUnknownFieldException">With <see cref="TikConnectionSetup.ValidateWrites"/>: an update or
        /// insert carries a field the router does not take. Nothing has been sent.</exception>
        /// <seealso cref="Simulate(out int, out int, out int, out int)"/>
        /// <seealso cref="SaveAsync"/>
        public IEnumerable<TEntity> Save()
        {
            var plan = Plan();
            Preflight(plan);

            foreach (var entity in plan.Deletes)
            {
                LogDml(MergeOperation.Delete, entity, default(TEntity));
                _connection.Delete(entity);
            }

            foreach (var update in plan.Updates)
            {
                LogDml(MergeOperation.Update, update.Row, update.Expected);
                WriteMergedValues(update.Row, update.Values);
                _connection.Save(update.Row, plan.MergedFieldNames);
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
        /// <returns>The rows on the router after the save, as far as this merge knows them (see the remarks on <see cref="Save"/>).</returns>
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called, or the entity does
        /// not offer a verb the merge needs (see the remarks on <see cref="Save"/>). Nothing has been sent.</exception>
        /// <exception cref="TikUnknownFieldException">With <see cref="TikConnectionSetup.ValidateWrites"/>: an update or
        /// insert carries a field the router does not take. Nothing has been sent.</exception>
        /// <exception cref="OperationCanceledException">Cancelled between two commands.</exception>
        public async Task<IEnumerable<TEntity>> SaveAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var plan = Plan();
            Preflight(plan);

            foreach (var entity in plan.Deletes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDml(MergeOperation.Delete, entity, default(TEntity));
                await _connection.DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
            }

            foreach (var update in plan.Updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDml(MergeOperation.Update, update.Row, update.Expected);
                WriteMergedValues(update.Row, update.Values);
                await _connection.SaveAsync(update.Row, plan.MergedFieldNames, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        /// Items which are present in both 'expected' and 'original' will be compared and counted as updated (if are different - see <see cref="Field{TProperty}(Expression{Func{TEntity, TProperty}})"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be counted as deleted.
        /// </summary>
        /// <param name="insertCnt">Number of items to be created.</param>
        /// <param name="updateCnt">Number of items to be updated.</param>
        /// <param name="deleteCnt">Number of items to be deleted.</param>
        /// <param name="moveCnt">Number of <c>/move</c> commands <see cref="Save"/> will send (see the remarks on
        /// <see cref="Save"/> for how few). A created item is placed by its create (<c>place-before</c>) and is not
        /// counted here.</param>
        /// <returns>The rows <see cref="Save"/> would return. An updated row is a copy with its new values, so 'original'
        /// stays as it was, and a row to be inserted has no <c>.id</c> yet.</returns>
        /// <exception cref="InvalidOperationException"><see cref="WithKey"/> has not been called, or the entity does
        /// not offer a verb <see cref="Save"/> would need.</exception>
        /// <exception cref="TikUnknownFieldException">With <see cref="TikConnectionSetup.ValidateWrites"/>: an update or
        /// insert carries a field the router does not take, which <see cref="Save"/> would refuse.</exception>
        /// <remarks>Sends no write, and calls neither log callback; the operation filter is consulted as by
        /// <see cref="Save"/>. It refuses what <see cref="Save"/> would refuse before its first command; with
        /// <see cref="TikConnectionSetup.ValidateWrites"/> that may read the menu's description from the router once
        /// per connection.</remarks>
        /// <seealso cref="Save"/>
        public IEnumerable<TEntity> Simulate(out int insertCnt, out int updateCnt, out int deleteCnt, out int moveCnt)
        {
            var plan = Plan();
            Preflight(plan);
            insertCnt = plan.Inserts.Count;
            updateCnt = plan.Updates.Count;
            deleteCnt = plan.Deletes.Count;
            moveCnt = plan.OrderSteps?.Count(s => s.Kind == TikListSyncStepKind.Move) ?? 0;

            var result = plan.Result.ToList();
            foreach (var update in plan.Updates)
                result[update.ResultIndex] = UpdatedCopy(update);
            return result;
        }

        /// <summary>
        /// Calculate update operations on mikrotik router.
        /// Items which are present in 'expected' and are not present in 'original' will be counted as created.
        /// Items which are present in both 'expected' and 'original' will be compared and counted as updated (if are different - see <see cref="Field{TProperty}(Expression{Func{TEntity, TProperty}})"/>, <see cref="WithKey"/>).
        /// Items which are not present in 'expected' and are present in 'original' will be counted as deleted.
        /// </summary>
        /// <param name="insertCnt">Number of items to be created.</param>
        /// <param name="updateCnt">Number of items to be updated.</param>
        /// <param name="deleteCnt">Number of items to be deleted.</param>
        /// <returns>The rows <see cref="Save"/> would return (see <see cref="Simulate(out int, out int, out int, out int)"/>).</returns>
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

        /// <summary>
        /// What <see cref="Save"/> refuses before its first command, with the rule <c>SaveListDifferences</c> uses: the
        /// verbs this plan needs, and with <see cref="TikConnectionSetup.ValidateWrites"/> every update and insert built
        /// and thrown away. An update is checked on a copy carrying the merged values, because the merge writes them
        /// onto the caller's row only when it saves it, and a refusal must leave that row as it was.
        /// </summary>
        private void Preflight(MergePlan plan)
        {
            TikConnectionExtensions.EnsureListVerbs(_metadata, plan.Inserts.Count > 0, plan.Deletes.Count > 0, plan.Updates.Count > 0);
            TikConnectionExtensions.ValidateListWrites(_connection, _metadata,
                plan.Updates.Select(update => (UpdatedCopy(update), (IEnumerable<string>?)plan.MergedFieldNames)),
                plan.Inserts, plan.CreateFieldNames);
        }

        // The row as the update leaves it, without touching the row itself.
        private TEntity UpdatedCopy(MergeUpdate update)
        {
            var copy = update.Row.CloneEntity();
            TikChangeTracker.For(_connection).ShareSnapshot(update.Row!, copy!);   // cleared fields and loaded values, as the row has them
            WriteMergedValues(copy, update.Values);
            return copy;
        }

        /// <summary>One row the merge updates.</summary>
        private sealed class MergeUpdate
        {
            public MergeUpdate(TEntity row, TEntity expected, object?[] values, int resultIndex)
            {
                Row = row;
                Expected = expected;
                Values = values;
                ResultIndex = resultIndex;
            }

            public TEntity Row { get; }
            public TEntity Expected { get; }
            /// <summary>What the row gets, in the order of the merged fields.</summary>
            public object?[] Values { get; }
            /// <summary>Where the row sits in <see cref="MergePlan.Result"/>.</summary>
            public int ResultIndex { get; }
        }

        /// <summary>What <see cref="Save"/> sends, decided before anything is — shared by Save, SaveAsync and Simulate.</summary>
        private sealed class MergePlan
        {
            public List<TEntity> Deletes { get; } = new List<TEntity>();
            public List<MergeUpdate> Updates { get; } = new List<MergeUpdate>();
            public List<TEntity> Inserts { get; } = new List<TEntity>();
            /// <summary>Ordered entity: the rows the ordering pass places, in expected order (inserts included).</summary>
            public List<TEntity> Desired { get; } = new List<TEntity>();
            public IReadOnlyList<TikListSyncStep>? OrderSteps { get; set; }
            /// <summary>The rows on the router once this is sent; see the remarks on <see cref="Save"/>.</summary>
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
            plan.MergedFieldNames = _fields.Select(f => f.Accessor.FieldName).ToArray();
            plan.CreateFieldNames = plan.MergedFieldNames.Concat(_justForInsertFields.Select(f => f.FieldName)).ToArray();

            // Materialized once: each is walked more than once, and a lazy source would read the router twice or hand back
            // fresh instances.
            var expected = _expected.ToList();
            var original = _original.ToList();
            Dictionary<string, TEntity> expectedByKey = expected.ToDictionaryEx(keyExtractor);
            Dictionary<string, TEntity> originalByKey = original.ToDictionaryEx(keyExtractor);

            //Delete — from the end of the list to the beginning (just for a better show in WinBox). A dynamic row is
            // not the merge's to delete: RouterOS refuses it ("cannot remove builtin").
            var kept = new List<TEntity>();
            foreach (var entity in Enumerable.Reverse(original))
            {
                if (expectedByKey.ContainsKey(keyExtractor(entity)))
                    continue;
                if (!TikListSync.IsDynamic(_metadata, entity) && _filterCallback(MergeOperation.Delete, entity, default(TEntity)))
                    plan.Deletes.Add(entity);
                else
                    kept.Insert(0, entity);
            }

            //Insert + Update, in expected order
            var desiredKeys = new List<string?>();
            foreach (var expectedEntity in expected)
            {
                string key = keyExtractor(expectedEntity);
                if (originalByKey.TryGetValue(key, out var originalEntity))
                {
                    var values = MergedValues(originalEntity, expectedEntity);
                    if (!HasMergedValues(originalEntity, values)
                        && _filterCallback(MergeOperation.Update, originalEntity, expectedEntity))
                        plan.Updates.Add(new MergeUpdate(originalEntity, expectedEntity, values, plan.Result.Count));

                    plan.Result.Add(originalEntity);
                    if (!TikListSync.IsDynamic(_metadata, originalEntity)) // the router refuses to move it, or anything in front of it
                    {
                        plan.Desired.Add(originalEntity);
                        desiredKeys.Add(key);
                    }
                }
                else if (_filterCallback(MergeOperation.Insert, default(TEntity), expectedEntity))
                {
                    plan.Inserts.Add(expectedEntity);
                    plan.Desired.Add(expectedEntity);
                    desiredKeys.Add(null);
                    plan.Result.Add(expectedEntity);
                }
            }
            plan.Result.AddRange(kept);

            if (_metadata.IsOrdered)
                plan.OrderSteps = TikListSyncPlanner.PlanOrder(desiredKeys, original.Select(keyExtractor));

            return plan;
        }
    }
}
