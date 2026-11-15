using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects.Tracking;

namespace tik4net.Objects
{
    public static partial class TikConnectionExtensions
    {
        /// <summary>
        /// Throws when the entity's menu does not offer <paramref name="operations"/>, naming the verb and
        /// the path — "R/O entity" was true of the four verbs together and of none of them separately.
        /// </summary>
        /// <remarks>
        /// Internal rather than private: <c>SaveAsync</c>/<c>DeleteAsync</c> in
        /// <see cref="TikConnectionAsyncExtensions"/> guard the same way, and a second copy of the rule is a
        /// second rule the moment either is touched.
        /// </remarks>
        internal static void EnsureSupported(TikEntityMetadata entityMetadata, TikEntityOperations operations)
        {
            if (!entityMetadata.Supports(operations))
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "Entity '{0}' does not support the '{1}' operation (it declares SupportedOperations = {2}). "
                    + "The RouterOS menu does not offer that verb - see TikEntityAttribute.SupportedOperations.",
                    entityMetadata.EntityPath, VerbOf(operations), entityMetadata.SupportedOperations));
        }

        /// <summary>The RouterOS verb an operation flag stands for, for the message above.</summary>
        private static string VerbOf(TikEntityOperations operations)
        {
            switch (operations)
            {
                case TikEntityOperations.Add: return "add";
                case TikEntityOperations.Set: return "set";
                case TikEntityOperations.Remove: return "remove";
                case TikEntityOperations.Move: return "move";
                default: return operations.ToString();
            }
        }

        private static void EnsureSupportsOrdering(TikEntityMetadata entityMetadata)
        {
            if (!entityMetadata.IsOrdered)
                throw new InvalidOperationException("Can not move entity without ordering support.");
        }

        private static void EnsureHasIdProperty(TikEntityMetadata metadata)
        {
            if (!metadata.HasIdProperty)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Can not update/delete non-singleton entity which doesn't contains property for '{0}' field.", TikSpecialProperties.Id));
        }

        /// <summary>
        /// Saves entity to mikrotik router. Does insert (/add) when entity has empty id and update(/set + /unset) when id is present).
        /// <para>
        /// A <see cref="TikField{T}"/> says by its state what an update does with it: <see cref="TikField{T}.Absent"/> is
        /// never sent, even where the load read a value; an assigned <c>null</c> is unset (a <c>comment</c> is cleared
        /// with <c>comment=""</c>), whether or not this connection loaded the entity, unless the load read the field as not
        /// printed; a value is set when it differs from the load. After a partial <c>.proplist</c> load, a field outside
        /// the list that the caller assigned is sent as well.
        /// </para>
        /// <para>
        /// <b>Load and save an entity on the SAME connection.</b> The change tracker is scoped to the
        /// <see cref="ITikConnection"/> it was loaded on, so an entity loaded on one connection and saved on
        /// another has no snapshot on the saving connection — and under
        /// <see cref="TikSaveMode.OnlyChanges"/> (the default) "no snapshot" means <b>every writable
        /// field is sent</b>, not just the ones you changed. Any field the loading transport spells
        /// differently from the saving one is then written back in the loading transport's spelling.
        /// <see cref="TikSaveMode.FullUpdate"/> does not have this problem — it resolves the difference by
        /// re-reading the row over the SAVING connection — but the two-connection pattern is not a supported
        /// way to use the mapper either way.
        /// </para>
        /// </summary>
        /// <typeparam name="TEntity">Saved entity type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="entity">Saved entity.</param>
        /// <param name="usedFieldsFilter">List of field names (on mikrotik) which should be modified. If is not null, only listed fields will be modified.</param>
        /// <param name="saveMode">Controls which fields are sent — see <see cref="TikSaveMode"/>.</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Command is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        public static void Save<TEntity>(this ITikConnection connection, TEntity entity,
            IEnumerable<string>? usedFieldsFilter = null,
            TikSaveMode saveMode = TikSaveMode.Default)
            where TEntity:new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            string? id = ResolveSaveId(entity, metadata);

            // Guarded per branch, not before: a menu can offer one of the two verbs and not the other
            // (/routing/ospf/neighbor has set and no add), so which one Save is about has to be decided
            // first. Still ahead of every router round-trip on both paths.
            if (IsCreate(metadata, id))
            {
                EnsureSupported(metadata, TikEntityOperations.Add);
                var createCmd = BuildCreateCommand(connection, entity, metadata, usedFieldsFilter);
                FinishCreate(connection, entity, metadata, createCmd.ExecuteScalar());
                return;
            }

            EnsureSupported(metadata, TikEntityOperations.Set);

            if (NeedsFilterResolution(metadata, usedFieldsFilter))
            {
                var resolution = ResolveUpdateFilter(connection, entity, metadata, saveMode);
                if (resolution.Kind == UpdateFilterKind.NothingChanged)
                    return; // nothing changed — skip the API call
                // id: non-null for a non-singleton here — IsCreate above already ruled out the "empty id" case.
                // A singleton has no id and is read back as the one row it is.
                usedFieldsFilter = resolution.Kind == UpdateFilterKind.NeedsUnmodifiedEntity
                    ? FullUpdateFields(connection, entity, metadata, metadata.IsSingleton
                        ? connection.LoadSingle<TEntity>()
                        : connection.LoadById<TEntity>(id!))
                    : resolution.Filter;
            }

            var update = BuildUpdateCommands(connection, entity, metadata, usedFieldsFilter, id);
            foreach (var unsetCmd in update.UnsetCommands)
                unsetCmd.ExecuteNonQuery();
            if (update.SetCommand != null)
                update.SetCommand.ExecuteNonQuery();
            TikChangeTracker.For(connection).ResetSnapshot(entity, metadata);
        }

        // ── Save, split into the parts that decide and the parts that talk ─────────────────────────
        //
        // internal, not private, and split this way for one reason: SaveAsync in
        // TikConnectionAsyncExtensions is the same decisions with awaits in place of the three calls that
        // reach the router. Save's rules are subtle — what counts as a create, what OnlyChanges does when
        // the entity was never loaded, which fields are unset rather than set, and that the unsets go first
        // — and a second copy of them would be a second set of rules the moment either is touched.

        /// <summary>
        /// The fields the caller cleared, which an update should <c>/unset</c> on the router: a <see cref="TikField{T}"/>
        /// holding an assigned <c>null</c>, unless the load saw the field unprinted.
        /// </summary>
        /// <remarks>
        /// A <see cref="TikField{T}"/> says by its state what the caller meant: <see cref="TikFieldState.Absent"/> is
        /// nothing said, an assigned <c>null</c> is an unset. It is an unset without a snapshot (an entity this
        /// connection never loaded) and outside a partial <c>.proplist</c> too, where nothing says whether the router
        /// holds a value — the caller asked for it, and the router takes an unset of an unset field. Only a field the
        /// load read as unprinted is left alone, since there is evidence that there is nothing to clear.
        /// </remarks>
        private static ISet<string> ResolveClearedFields<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata)
        {
            var cleared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var snapshot = TikChangeTracker.For(connection).GetSnapshot(entity!);

            foreach (var property in metadata.Properties)
            {
                if (property.IsReadOnly || property.GetEntityValue(entity!) != null || property.IsAbsent(entity!))
                    continue;
                if (snapshot == null || !snapshot.IsTracked(property.FieldName)
                    || (snapshot.TryGetValue(property.FieldName, out string? previous) && previous != null))
                    cleared.Add(property.FieldName);
            }

            return cleared;
        }

        internal static string? ResolveSaveId<TEntity>(TEntity entity, TikEntityMetadata metadata)
        {
            if (metadata.IsSingleton)
                return null;
            EnsureHasIdProperty(metadata);
            // IdProperty is non-null here: EnsureHasIdProperty just verified it. The .id value itself can
            // legitimately be null/empty for a never-loaded entity — that null is IsCreate's create signal.
            return metadata.IdProperty!.GetEntityValue(entity!);
        }

        internal static bool IsCreate(TikEntityMetadata metadata, string? id)
            => !metadata.IsSingleton && string.IsNullOrEmpty(id);

        // A singleton too: saving a loaded one with every writable field would write back the values the load
        // filled in for fields this router does not have (RouterOS 6's /tool/e-mail has no tls, vrf, …), and
        // the router refuses the whole set with "unknown parameter".
        internal static bool NeedsFilterResolution(TikEntityMetadata metadata, IEnumerable<string>? usedFieldsFilter)
            => usedFieldsFilter == null;

        internal static ITikCommand BuildCreateCommand<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata, IEnumerable<string>? usedFieldsFilter)
        {
            ITikCommand createCmd = connection.CreateCommand(metadata.EntityPath + "/add", TikCommandParameterFormat.NameValue);
            AddWinboxLabels(createCmd, metadata);

            foreach (var property in metadata.Properties
                .Where(pm => !pm.IsReadOnly)
                .Where(pm => usedFieldsFilter == null || usedFieldsFilter.Contains(pm.FieldName, StringComparer.OrdinalIgnoreCase)))
            {
                // Null is Absent or an assigned null: on a row that does not exist yet, both leave the choice to the router.
                string? value = property.GetEntityValue(entity!);
                if (value != null)
                    createCmd.AddParameter(property.WriteName(entity!), value);
            }

            TikWriteValidation.Validate(connection, metadata, new[] { createCmd });
            return createCmd;
        }

        internal static void FinishCreate<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata, string? id)
        {
            if (metadata.HasIdProperty)
                metadata.IdProperty!.SetEntityValue(entity!, id); // update saved id into entity
            TikChangeTracker.For(connection).TakeSnapshot(entity, metadata);
        }

        internal enum UpdateFilterKind
        {
            /// <summary>Use <c>Filter</c> as-is. <c>null</c> means "send every writable field".</summary>
            UseFilter,
            /// <summary>The change tracker says the entity is unmodified — do not call the router at all.</summary>
            NothingChanged,
            /// <summary>FullUpdate: the caller must load the unmodified entity and diff against it.</summary>
            NeedsUnmodifiedEntity,
        }

        /// <summary>
        /// Decides which fields an update should send. Pure except for reading the change tracker — the one
        /// outcome that needs I/O is returned as <see cref="UpdateFilterKind.NeedsUnmodifiedEntity"/> rather
        /// than performed here, so the caller does that load synchronously or asynchronously as it pleases.
        /// </summary>
        internal static (UpdateFilterKind Kind, IEnumerable<string>? Filter) ResolveUpdateFilter<TEntity>(
            ITikConnection connection, TEntity entity, TikEntityMetadata metadata, TikSaveMode saveMode)
        {
            var effectiveSaveMode = saveMode == TikSaveMode.Default ? TikDefaults.SaveMode : saveMode;
            if (effectiveSaveMode != TikSaveMode.OnlyChanges)
                return (UpdateFilterKind.NeedsUnmodifiedEntity, null); // FullUpdate: round-trip load to compute diff (3.x behavior)

            var tracker = TikChangeTracker.For(connection);
            var snapshot = tracker.GetSnapshot(entity!);
            if (snapshot == null)
            {
                // no snapshot → entity was not loaded via Load* → send all writable fields
                return (UpdateFilterKind.UseFilter, null);
            }

            var changes = tracker.GetChanges(entity, metadata);
            if (changes.Count == 0)
                return (UpdateFilterKind.NothingChanged, null);
            return (UpdateFilterKind.UseFilter, changes.Keys);
        }

        /// <summary>
        /// FullUpdate's field set: what differs from a fresh read of the row, minus the fields the router moves by
        /// itself (<see cref="TikEntityPropertyAccessor.ChangesOnItsOwn"/>) that the caller has not touched since
        /// the load. Those always differ from a fresh read, and sending them writes the loaded value back — the
        /// clock set to the time it was read at. Without a snapshot nothing says what the caller touched, so every
        /// difference is sent, as before.
        /// </summary>
        internal static IEnumerable<string> FullUpdateFields<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata, TEntity unmodifiedEntity)
        {
            var different = entity.GetDifferentFields(unmodifiedEntity).ToList();
            var snapshot = TikChangeTracker.For(connection).GetSnapshot(entity!);
            if (snapshot == null)
                return different;

            return different.Where(field =>
            {
                var property = metadata.Properties.FirstOrDefault(p => p.FieldName == field);
                if (property == null || !property.ChangesOnItsOwn || !snapshot.IsTracked(field))
                    return true;
                return !snapshot.TryGetValue(field, out string? loaded) || loaded != property.GetEntityValue(entity!);
            }).ToList();
        }

        /// <summary>
        /// Refuses a text <c>set</c> of a <see cref="TikListSetBehaviour.KeepsUnnamedHalf"/> list that RouterOS would
        /// merge: the loaded value has members of a kind (plain, negated) the new value has none of, and the router keeps
        /// the half a text <c>set</c> does not name. Without a loaded value there is nothing to compare, and the set goes.
        /// </summary>
        private static void EnsureTheSetIsExact<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityPropertyAccessor property, string value)
        {
            var snapshot = TikChangeTracker.For(connection).GetSnapshot(entity!);
            if (snapshot == null || !snapshot.TryGetValue(property.FieldName, out string? loaded) || loaded == null)
                return;
            var before = TikValueListWire.Halves(loaded);
            var after = TikValueListWire.Halves(value);
            string? kept = before.Plain && !after.Plain ? "plain" : before.Negated && !after.Negated ? "negated" : null;
            if (kept == null)
                return;
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, 
                "Property '{0}({1})': RouterOS's text set replaces only the members it names, so setting '{2}' on a row holding "
                + "'{3}' would keep its {4} members. Name at least one {4} member, or write it over a transport with structured "
                + "writes (WinBox native).",
                property.PropertyName, property.FieldName, value, loaded, kept));
        }

        /// <summary>
        /// The value a text <c>set</c> of a <see cref="TikListSetBehaviour.KeepsUnnamedMembers"/> list sends: the new
        /// value, then every member the loaded value had and the new one does not name, denied — so the router, which keeps
        /// the members a <c>set</c> leaves out, ends up holding exactly the new value. Sent on every transport: a value
        /// naming every member is exact whichever way it is written. Refused without a loaded value, which is the only
        /// statement of the members this router has.
        /// </summary>
        internal static string CompleteTheSet<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityPropertyAccessor property, string value)
        {
            var snapshot = TikChangeTracker.For(connection).GetSnapshot(entity!);
            if (snapshot == null || !snapshot.TryGetValue(property.FieldName, out string? loaded) || loaded == null)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "Property '{0}({1})': RouterOS's text set changes only the members it names, and without the row's loaded "
                    + "value there is no telling which members to deny, so setting '{2}' would keep whatever else the row holds. "
                    + "Load the row (LoadById, LoadList, a merge) and change its value; nothing was sent.",
                    property.PropertyName, property.FieldName, value));

            var members = SetMembers(value).ToList();
            var named = new HashSet<string>(members.Select(MemberName), StringComparer.Ordinal);
            var denied = SetMembers(loaded).Select(MemberName).Where(named.Add).Select(name => TikNegation.Bang + name);
            return string.Join(",", members.Concat(denied));
        }

        private static IEnumerable<string> SetMembers(string text)
            => text.Split(',').Where(element => element.Length != 0 && element != TikNegation.Bang);

        private static string MemberName(string element)
        {
            TikNegation.TrySplit(element, out string name);
            return name;
        }

        /// <summary>
        /// Builds the update traffic: the <c>/unset</c> commands (which must run first) and the single
        /// <c>/set</c>, or <c>null</c> when the update turned out to carry no fields.
        /// </summary>
        internal static (ITikCommand? SetCommand, IList<ITikCommand> UnsetCommands) BuildUpdateCommands<TEntity>(
            ITikConnection connection, TEntity entity, TikEntityMetadata metadata,
            IEnumerable<string>? usedFieldsFilter, string? id)
        {
            ITikCommand setCmd = connection.CreateCommand(metadata.EntityPath + "/set", TikCommandParameterFormat.NameValue);
            AddWinboxLabels(setCmd, metadata);
            List<string> fieldsToUnset = new List<string>();
            var clearedFields = ResolveClearedFields(connection, entity, metadata);

            foreach (var property in metadata.Properties
                .Where(pm => !pm.IsReadOnly)
                .Where(pm => usedFieldsFilter == null || usedFieldsFilter.Contains(pm.FieldName, StringComparer.OrdinalIgnoreCase)))
            {
                if (clearedFields.Contains(property.FieldName))
                {
                    // Loaded with a value, set to null → unset. Except a comment: RouterOS clears one with
                    // comment="" on every menu (an empty comment is no comment - TikEmptyComment), while menus
                    // such as /interface have no unset verb at all ("no such command").
                    if (string.Equals(property.FieldName, "comment", StringComparison.OrdinalIgnoreCase))
                        setCmd.AddParameter(property.WriteName(entity!), "");
                    else
                        fieldsToUnset.Add(property.WriteName(entity!));
                }
                else
                {
                    string? value = property.GetEntityValue(entity!);
                    // Null that is NOT a clearing: the caller never said anything about this field. Sending it would put
                    // the word on the wire with no value, and unsetting it would destroy what the router holds on the
                    // strength of silence.
                    if (value != null)
                    {
                        if (property.SetBehaviour == TikListSetBehaviour.KeepsUnnamedHalf && !connection.Supports(TikConnectionCapability.StructuredWrites))
                            EnsureTheSetIsExact(connection, entity, property, value);
                        if (property.SetBehaviour == TikListSetBehaviour.KeepsUnnamedMembers)
                            value = CompleteTheSet(connection, entity, property, value);
                        setCmd.AddParameter(property.WriteName(entity!), value); //full update (all values)
                    }
                }
            }

            // this should also work (see http://forum.mikrotik.com/viewtopic.php?t=28821 )
            //ip/route/unset
            //=.id = *1
            //= value-name=routing-mark
            var unsetCommands = new List<ITikCommand>();
            foreach (string fld in fieldsToUnset)
            {
                ITikCommand unsetCmd = connection.CreateCommand(metadata.EntityPath + "/unset", TikCommandParameterFormat.NameValue);
                AddWinboxLabels(unsetCmd, metadata);
                // non-null: BuildUpdateCommands is only reached past Save's IsCreate check, which already requires a
                // non-empty id for every non-singleton entity.
                if (!metadata.IsSingleton)
                    unsetCmd.AddParameter(TikSpecialProperties.Id, id!, TikCommandParameterFormat.NameValue);
                unsetCmd.AddParameter(TikSpecialProperties.UnsetValueName, fld);
                unsetCommands.Add(unsetCmd);
            }

            // Before anything is sent: the unsets run first, so a refused set must not follow a sent unset.
            TikWriteValidation.Validate(connection, metadata,
                unsetCommands.Concat(setCmd.Parameters.Any() ? new[] { setCmd } : Array.Empty<ITikCommand>()));

            if (!setCmd.Parameters.Any())
                return (null, unsetCommands);

            if (!metadata.IsSingleton)
                setCmd.AddParameter(TikSpecialProperties.Id, id!, TikCommandParameterFormat.NameValue); // non-null: see the note above
            return (setCmd, unsetCommands);
        }
    }
}
