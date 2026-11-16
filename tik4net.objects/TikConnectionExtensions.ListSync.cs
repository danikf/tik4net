using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects.Tracking;
using tik4net.Helpers;

namespace tik4net.Objects
{
    public static partial class TikConnectionExtensions
    {
        /// <summary>
        /// List version of <see cref="Save"/> method. Saves differences between given <paramref name="modifiedList"/> and <paramref name="unmodifiedList"/>.
        /// Typical usage is: Load, create list clone, modify list, save diferences.
        /// </summary>
        /// <example>
        /// var list = connection.LoadList{FirewallAddressList}(connection.CreateParameter("list", listName), connection.CreateParameter("address", ipAddress));
        /// var listClonedBackup = list.CloneEntityList(); //creates clone of all entities in list
        /// list.Add(new FirewallAddressList() {Address = ipAddress, List = listName, }); //insert
        /// list[0].Comment = "test comment"; //update
        /// list.RemoveAt(1); //delete
        /// connection.SaveListDifferences(list, listClonedBackup);
        /// </example>
        /// <typeparam name="TEntity">Saved entity type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="modifiedList">List with modifications. Its ORDER is applied when the entity is ordered — see remarks.</param>
        /// <param name="unmodifiedList">Original (cloned) unmodified list.</param>
        /// <remarks>
        /// <para>
        /// On an <b>ordered</b> entity (<see cref="TikEntityAttribute.IsOrdered"/> — firewall filter, mangle,
        /// NAT…) the router's rows are left in the order of <paramref name="modifiedList"/>, by issuing the
        /// <c>/move</c> commands the difference needs. Reordering the list is therefore a change like any
        /// other: moving a rule without touching a field still rewrites the chain. On an unordered entity no
        /// move is issued and the list order means nothing.
        /// </para>
        /// <para>
        /// This matters because a rule in the wrong position is a rule that does the wrong thing while every
        /// field on it reads correct. The pass moves each row at most once, leaves in place the longest run of rows
        /// already in order, and creates a new entity in place (<c>place-before</c>) instead of appending it and
        /// moving it. Rows of the table that are not in the lists keep their positions, so the lists may be a
        /// filtered part of the table (one chain, one comment tag) — and because what follows the list is not
        /// read, nothing is placed after its current last row: when the last row changes, that can cost one
        /// move more than an unrestricted reorder (sending one of four rules to the end is two moves).
        /// </para>
        /// <para>
        /// <b>Dynamic rows</b> (<c>dynamic=true</c>, e.g. the fasttrack counter rule) are never deleted or moved —
        /// RouterOS refuses both — so leaving one out of <paramref name="modifiedList"/> is harmless. A field change
        /// to one is still sent, and the router's refusal surfaces.
        /// </para>
        /// <para>
        /// Commands go out as deletes, updates, then the ordering pass (moves and creates). There is no
        /// transaction: a failure part-way leaves what was sent applied, and the first error propagates. Reload
        /// and save again to continue from where the router is.
        /// </para>
        /// <para>
        /// Both lists are materialized internally; the entities in <paramref name="modifiedList"/> are the same
        /// instances throughout, which is what lets a later create be placed in front of a row created a step
        /// earlier, by the <c>.id</c> that create wrote onto it.
        /// </para>
        /// </remarks>
        /// <seealso cref="TikEntityObjectsExtensions.CloneEntity"/>
        /// <seealso cref="TikEntityObjectsExtensions.CloneEntityList"/>
        /// <seealso cref="Save"/>
        /// <seealso cref="Move"/>
        /// <seealso cref="TikConnectionAsyncExtensions.SaveListDifferencesAsync"/>
        public static void SaveListDifferences<TEntity>(this ITikConnection connection, IEnumerable<TEntity> modifiedList, IEnumerable<TEntity> unmodifiedList)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var plan = PlanListDifferences(modifiedList, unmodifiedList);
            ValidateListDifferences(connection, plan);

            foreach (var entity in plan.Deletes)
                Delete(connection, entity);
            foreach (var entity in plan.Updates)
                Save(connection, entity);
            if (plan.OrderSteps != null)
                TikListSync.ApplyOrder(connection, plan.Metadata, plan.Desired, plan.OrderSteps, null, null);
            else
                foreach (var entity in plan.Creates)
                    Save(connection, entity);
        }

        /// <summary>
        /// <see cref="TikConnectionSetup.ValidateWrites"/> over a whole <see cref="SaveListDifferences"/> pass before its
        /// first write (see <see cref="ValidateListWrites"/>). A FullUpdate row, whose fields need a read of the router,
        /// is checked when it is saved.
        /// </summary>
        internal static void ValidateListDifferences<TEntity>(ITikConnection connection, ListDifferencesPlan<TEntity> plan)
            where TEntity : new()
        {
            var metadata = plan.Metadata;
            var creates = plan.OrderSteps != null
                ? plan.Desired.Where(entity => IsCreate(metadata, ResolveSaveId(entity, metadata)))
                : plan.Creates;
            ValidateListWrites(connection, metadata, UpdatesWithTheirFilters(connection, plan), creates, null);
        }

        // The fields each update of a SaveListDifferences pass sends, as Save decides them; a FullUpdate row is left out.
        private static IEnumerable<(TEntity Entity, IEnumerable<string>? Filter)> UpdatesWithTheirFilters<TEntity>(
            ITikConnection connection, ListDifferencesPlan<TEntity> plan)
        {
            foreach (var entity in plan.Updates)
            {
                var (kind, filter) = ResolveUpdateFilter(connection, entity, plan.Metadata, TikSaveMode.Default);
                if (kind == UpdateFilterKind.UseFilter)
                    yield return (entity, filter);
            }
        }

        /// <summary>
        /// <see cref="TikConnectionSetup.ValidateWrites"/> over a list writer's updates and creates before its first
        /// write, shared by <see cref="SaveListDifferences"/> and <see cref="TikListMerge{TEntity}"/>: every command is
        /// built — which checks it — and thrown away, so a row that would be refused stops the pass before any delete,
        /// update or move has gone out. Each update comes with the fields it sends, and every create with
        /// <paramref name="createFilter"/> (<c>null</c>: every field it holds). The sequences are not read when the
        /// connection does not validate.
        /// </summary>
        internal static void ValidateListWrites<TEntity>(ITikConnection connection, TikEntityMetadata metadata,
            IEnumerable<(TEntity Entity, IEnumerable<string>? Filter)> updates, IEnumerable<TEntity> creates,
            IEnumerable<string>? createFilter)
        {
            if (!(connection is ITikMenuSchemaConnection described) || !described.ValidateWrites)
                return;
            foreach (var (entity, filter) in updates)
                BuildUpdateCommands(connection, entity, metadata, filter, ResolveSaveId(entity, metadata));
            foreach (var entity in creates)
                BuildCreateCommand(connection, entity, metadata, createFilter);
        }

        /// <summary>
        /// The verbs a list writer's pass needs, checked before its first command, shared by
        /// <see cref="SaveListDifferences"/> and <see cref="TikListMerge{TEntity}"/>. Only the verbs the pass actually
        /// uses: demanding all of them would refuse a pure-delete pass on <c>/ppp/active</c>, which is precisely what
        /// the menu does allow. <c>move</c> is checked at the end of the ordering pass, so a field-only update on an
        /// ordered menu without <c>/move</c> is not refused.
        /// </summary>
        internal static void EnsureListVerbs(TikEntityMetadata metadata, bool creates, bool deletes, bool updates)
        {
            if (creates)
                EnsureSupported(metadata, TikEntityOperations.Add);
            if (deletes)
                EnsureSupported(metadata, TikEntityOperations.Remove);
            if (updates)
                EnsureSupported(metadata, TikEntityOperations.Set);
        }

        /// <summary>
        /// What a <see cref="SaveListDifferences"/> pass does, decided before anything is sent — shared by the sync
        /// and async halves, which only differ in how they send it.
        /// </summary>
        internal sealed class ListDifferencesPlan<TEntity>
        {
            public ListDifferencesPlan(TikEntityMetadata metadata) { Metadata = metadata; }
            public TikEntityMetadata Metadata { get; }
            public List<TEntity> Deletes { get; } = new List<TEntity>();
            public List<TEntity> Updates { get; } = new List<TEntity>();
            /// <summary>Unordered menu: the rows to create, in list order.</summary>
            public List<TEntity> Creates { get; } = new List<TEntity>();
            /// <summary>Ordered menu: the rows the ordering pass places, in desired order.</summary>
            public List<TEntity> Desired { get; } = new List<TEntity>();
            /// <summary>Ordered menu: the ordering pass (moves, and the creates in place); null when unordered.</summary>
            public IReadOnlyList<TikListSyncStep>? OrderSteps { get; set; }
        }

        internal static ListDifferencesPlan<TEntity> PlanListDifferences<TEntity>(IEnumerable<TEntity> modifiedList, IEnumerable<TEntity> unmodifiedList)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureHasIdProperty(metadata);
            var idProperty = metadata.IdProperty!; // non-null: EnsureHasIdProperty just verified it
            var plan = new ListDifferencesPlan<TEntity>(metadata);

            // Materialized once: the ordering pass walks the modified list a second time, and it has to be the
            // same instances — a create writes the new .id onto the entity it was given, and a later step
            // anchors on it; a re-enumerated lazy sequence would hand back fresh instances without one.
            var modifiedItems = modifiedList.ToList();
            var unmodifiedItems = unmodifiedList.ToList();

            // entity: TEntity is only constrained to new(), so the compiler treats it as possibly null;
            // every entity here is an actual instance from modifiedList/unmodifiedList. The .id read itself
            // (GetEntityValue's result) is non-null by construction of the Where/dictionary-key filters below.
            var entitiesToCreate = modifiedItems.Where(entity => string.IsNullOrEmpty(idProperty.GetEntityValue(entity!))).ToList(); // new items in modifiedList

            Dictionary<string, TEntity> modifiedEntities = modifiedItems
                .Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity!)))
                .ToDictionaryEx(entity => idProperty.GetEntityValue(entity!)!); //all entities from modified list with ids
            Dictionary<string, TEntity> unmodifiedEntities = unmodifiedItems
                // no id filter: every entity of the unmodified list was loaded from the router, so has an id
                .ToDictionaryEx(entity => idProperty.GetEntityValue(entity!)!); //all entities from unmodified list with ids

            //DELETE — a row the router made itself (dynamic) is not the caller's to delete: RouterOS refuses it
            // ("cannot remove builtin"), and leaving it out of the list is the normal way to not care about it.
            plan.Deletes.AddRange(unmodifiedEntities
                .Where(pair => !modifiedEntities.ContainsKey(pair.Key) && !TikListSync.IsDynamic(metadata, pair.Value))
                .Select(pair => pair.Value));

            //UPDATE — a change to a dynamic row is still sent: the router's refusal is the honest answer to it.
            plan.Updates.AddRange(unmodifiedEntities
                .Where(pair => modifiedEntities.ContainsKey(pair.Key) && !modifiedEntities[pair.Key].EntityEquals(pair.Value))
                .Select(pair => modifiedEntities[pair.Key]));

            // A pass that reaches for several verbs could get halfway through a partially-supported menu — rows
            // deleted, then a create refused — so what THIS diff asks for is checked before the first command.
            EnsureListVerbs(metadata, entitiesToCreate.Count > 0, plan.Deletes.Count > 0, plan.Updates.Count > 0);

            //CREATE + ORDER
            if (metadata.IsOrdered)
            {
                // A dynamic row keeps its position whatever the list says: the router refuses to move it, or
                // anything in front of it.
                plan.Desired.AddRange(modifiedItems.Where(entity => !TikListSync.IsDynamic(metadata, entity)));
                plan.OrderSteps = TikListSyncPlanner.PlanOrder(
                    plan.Desired.Select(entity => idProperty.GetEntityValue(entity!)).Select(id => string.IsNullOrEmpty(id) ? null : id).ToList(),
                    unmodifiedItems.Select(entity => idProperty.GetEntityValue(entity!)!));
            }
            else
                plan.Creates.AddRange(entitiesToCreate);

            return plan;
        }
    }
}
