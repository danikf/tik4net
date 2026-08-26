using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Objects.Tracking;

namespace tik4net.Objects
{
    /// <summary>
    /// Main mapper extension - extends <see cref="ITikConnection"/>.
    /// Supports CRUD and move functions.
    /// <para>
    /// <list type="bullet">
    /// <listheader>Load:</listheader>
    /// <item><see cref="LoadAll"/></item>
    /// <item><see cref="LoadById"/></item>
    /// <item><see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/></item>
    /// <item><see cref="LoadWithDuration"/></item>
    /// <item><see cref="LoadWithCallback"/></item>
    /// <item><see cref="LoadListenWithCallback"/></item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// <list type="bullet">
    /// <listheader>Save:</listheader>
    /// <item><see cref="Save"/> (Insert/Update)</item>
    /// <item><see cref="SaveListDifferences"/> (Insert/Update/Delete)</item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// <list type="bullet">
    /// <listheader>Delete:</listheader>
    /// <item><see cref="Delete"/></item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// <list type="bullet">
    /// <listheader>Move:</listheader>
    /// <item><see cref="Move"/></item>
    /// <item><see cref="MoveToEnd"/></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class TikConnectionExtensions
    {
        #region -- LOAD --
        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> without filter.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <returns>Loaded list of entities.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        public static IEnumerable<TEntity> LoadAll<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection);
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> optionaly with filter, ensures that result contains exactly one row.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>Loaded single entity.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity LoadSingle<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = command.LoadList<TEntity>().ToList();

            return ExactlyOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> without filter, ensures that result contains exactly one row.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>Loaded single entity or null.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity? LoadSingleOrDefault<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = command.LoadList<TEntity>().ToList();

            return AtMostOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Loads entity with specified id. Returns null if not found.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="id">Entity id.</param>
        /// <returns>Loaded entity or null.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity LoadById<TEntity>(this ITikConnection connection, string id)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, connection.CreateParameter(TikSpecialProperties.Id, id));
            var candidates = command.LoadList<TEntity>().ToList();

            return ExactlyOne(connection, command, candidates, metadata);
        }

        /// <summary>
        /// Loads entity with specified name. Returns null if not found.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="name">Entity name.</param>
        /// <returns>Loaded entity or null.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity LoadByName<TEntity>(this ITikConnection connection, string name)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, connection.CreateParameter("name", name));
            var candidates = command.LoadList<TEntity>().ToList();

            return ExactlyOne(connection, command, candidates, metadata);
        }

        /// <summary>
        /// Loads entity list. Could be filtered with <paramref name="filterParameters"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        /// <seealso cref="TikCommandExtensions.LoadList{TEntity}(ITikCommand)"/>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = command.LoadList<TEntity>().ToList();
            RegisterSnapshots(connection, entities, metadata, command);
            return entities;
        }

        /// <summary>
        /// Calls command and reads all returned rows for given <paramref name="durationSec"/> period.
        /// After this period calls cancell to mikrotik router and returns all loaded rows.
        /// Throws exception if any 'trap' row occurs.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="durationSec">Loading period.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        /// <seealso cref="TikCommandExtensions.LoadWithDuration{TEntity}(ITikCommand, int)"/>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        public static IEnumerable<TEntity> LoadWithDuration<TEntity>(this ITikConnection connection, int durationSec, params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");

            var command = CreateLoadCommandWithFilter<TEntity>(connection, parameters);

            return command.LoadWithDuration<TEntity>(durationSec);
        }


        /// <summary>
        /// Calls command and starts backgroud reading thread. After that returns control to calling thread.
        /// All read rows are returned as callbacks (<paramref name="onLoadItemCallback"/>, <paramref name="onExceptionCallback"/>) from loading thread.
        /// REMARKS: if you want to propagate loaded values to GUI, you should use some kind of synchronization or Invoke, because 
        /// callbacks are called from non-ui thread.
        /// The running load can be terminated by <see cref="ITikCommand.Cancel"/> or <see cref="ITikCommand.CancelAndJoin()"/> call. 
        /// Command is returned as result of the method.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="onLoadItemCallback">Callback called for each loaded !re row</param>
        /// <param name="onExceptionCallback">Callback called when error occurs (!trap row is returned)</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns><see cref="ITikCommand"/> which is already running the async load operation. You can cancel the running operation by <see cref="ITikCommand.Cancel"/> method call.</returns>
        /// <seealso cref="TikCommandExtensions.LoadWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{Exception}, Action)"/>
        public static ITikCommand LoadWithCallback<TEntity>(this ITikConnection connection,
            Action<TEntity> onLoadItemCallback, Action<Exception>? onExceptionCallback = null,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");
            Guard.ArgumentNotNull(onLoadItemCallback, "onLoadItemCallback");

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var tracker = TikChangeTracker.For(connection);
            var trackedFields = GetProplistFields(command, metadata);

            command.LoadWithCallback<TEntity>(
                entity => {
                    tracker.TakeSnapshot(entity, metadata, trackedFields);
                    onLoadItemCallback(entity);
                },
                onExceptionCallback);
            return command;
        }

        /// <summary>
        /// Starts asynchronous listening for real-time changes in the entity list.
        /// Builds a <c>/listen</c> command from the entity's API path and starts it via
        /// <see cref="TikCommandExtensions.LoadListenWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{string}, Action{Exception})"/>.
        /// The command streams <c>!re</c> sentences whenever an item is added, changed, or removed.
        /// It never sends <c>!done</c> — stop listening by calling <see cref="ITikCommand.Cancel"/> or <see cref="ITikCommand.CancelAndJoin()"/>
        /// on the returned command.
        /// </summary>
        /// <typeparam name="TEntity">Entity type to listen to (must have a <c>[TikEntity]</c> attribute).</typeparam>
        /// <param name="connection">Active connection.</param>
        /// <param name="onChangeCallback">Called for each changed or added item.</param>
        /// <param name="onDeletedCallback">Called with the <c>.id</c> of a deleted item. Can be <c>null</c>.</param>
        /// <param name="onExceptionCallback">Called when a <c>!trap</c> is received.</param>
        /// <param name="parameters">Optional query filters applied to the listen command.</param>
        /// <returns>The running <see cref="ITikCommand"/>. Cancel it to stop listening.</returns>
        /// <seealso cref="TikCommandExtensions.LoadListenWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{string}, Action{Exception})"/>
        public static ITikCommand LoadListenWithCallback<TEntity>(this ITikConnection connection,
            Action<TEntity> onChangeCallback,
            Action<string>? onDeletedCallback = null,
            Action<Exception>? onExceptionCallback = null,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, "connection");
            Guard.ArgumentNotNull(onChangeCallback, "onChangeCallback");

            var command = CreateListenCommandWithFilter<TEntity>(connection, parameters);
            command.LoadListenWithCallback<TEntity>(onChangeCallback, onDeletedCallback, onExceptionCallback);
            return command;
        }

        private static ITikCommand CreateListenCommandWithFilter<TEntity>(ITikConnection connection, params ITikCommandParameter[] parameters)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            ITikCommand command = connection.CreateCommand(metadata.EntityPath + "/listen", metadata.LoadDefaultParameneterFormat);

            if (parameters != null)
            {
                foreach (ITikCommandParameter param in parameters)
                    command.Parameters.Add(param);
            }

            return command;
        }

        // ── Shared between the synchronous loads above and their Task-based twins ──────────────────
        //
        // internal, not private: TikConnectionAsyncExtensions calls exactly these. What differs between a
        // Load and a LoadAsync is one await — everything around it (how many rows are acceptable, which
        // exception says so, and the change-tracker snapshot that makes a later Save able to send only what
        // changed) has to stay identical, and the only way to be sure of that is for there to be one copy.

        /// <summary>Requires exactly one row, and snapshots it for the change tracker.</summary>
        internal static TEntity ExactlyOne<TEntity>(ITikConnection connection, ITikCommand command,
            IList<TEntity> entities, TikEntityMetadata metadata)
        {
            var cnt = entities.Count;
            if (cnt == 0)
                throw new TikNoSuchItemException(command);
            else if (cnt > 1)
                throw new TikCommandAmbiguousResultException(command, cnt);

            var entity = entities[0];
            TikChangeTracker.For(connection).TakeSnapshot(entity, metadata, GetProplistFields(command, metadata));
            return entity;
        }

        /// <summary>Allows an empty result, and snapshots the row when there is one.</summary>
        internal static TEntity? AtMostOne<TEntity>(ITikConnection connection, ITikCommand command,
            IList<TEntity> entities, TikEntityMetadata metadata)
        {
            var cnt = entities.Count;
            if (cnt == 0)
                return default(TEntity);
            if (cnt > 1)
                throw new TikCommandAmbiguousResultException(command, cnt);

            var entity = entities[0];
            TikChangeTracker.For(connection).TakeSnapshot(entity, metadata, GetProplistFields(command, metadata));
            return entity;
        }

        internal static ITikCommand CreateLoadCommand<TEntity>(ITikConnection connection,
            params ITikCommandParameter[] parameters)
            => CreateLoadCommandWithFilter<TEntity>(connection, parameters);

        internal static void RegisterLoadedSnapshots<TEntity>(ITikConnection connection, IList<TEntity> entities,
            TikEntityMetadata metadata, ITikCommand command)
            => RegisterSnapshots(connection, entities, metadata, command);

        /// <summary>
        /// Registers snapshots for a batch of freshly loaded entities.
        /// </summary>
        private static void RegisterSnapshots<TEntity>(ITikConnection connection, IList<TEntity> entities,
            TikEntityMetadata metadata, ITikCommand command)
        {
            var tracker = TikChangeTracker.For(connection);
            var trackedFields = GetProplistFields(command, metadata);
            foreach (var entity in entities)
                tracker.TakeSnapshot(entity, metadata, trackedFields);
        }

        /// <summary>
        /// Returns the set of field names that were sent as <c>.proplist</c> in the command,
        /// or <c>null</c> when the load was a full load (no proplist, or proplist covers all fields).
        /// </summary>
        private static IEnumerable<string>? GetProplistFields(ITikCommand command, TikEntityMetadata metadata)
        {
            var proplistParam = command.Parameters
                .FirstOrDefault(p => p.Name == TikSpecialProperties.Proplist);
            if (proplistParam == null)
                return null;

            var fields = new HashSet<string>(
                proplistParam.Value.Split(','),
                StringComparer.OrdinalIgnoreCase);

            // IncludeProplist=true sends all entity fields — treat as full load
            bool isFullLoad = metadata.Properties.All(p => fields.Contains(p.FieldName));
            return isFullLoad ? null : (IEnumerable<string>)fields;
        }

        private static ITikCommand CreateLoadCommandWithFilter<TEntity> (ITikConnection connection, params ITikCommandParameter[] parameters)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            ITikCommand command = connection.CreateCommand(metadata.EntityPath + metadata.LoadCommand, metadata.LoadDefaultParameneterFormat);

            // =detail=
            if (metadata.IncludeDetails)
                command.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            // CLI-only marker: two-query (detail + stats) merge for entities with live counters.
            // API/REST transports must silently ignore this parameter (never send it on the wire).
            if (metadata.IncludeCliStats)
                command.AddParameter(TikSpecialProperties.CliStats, "", TikCommandParameterFormat.NameValue);
            // CLI-only marker: read via ':serialize to=json' because at least one field holds free-form
            // text, which the unescaped as-value format cannot represent unambiguously (P2.17).
            if (metadata.HasFreeTextProperties)
                command.AddParameter(TikSpecialProperties.CliJson, "", TikCommandParameterFormat.NameValue);
            //.proplist
            if (metadata.IncludeProplist)
                command.AddParameter(TikSpecialProperties.Proplist, string.Join(",", metadata.Properties.Select(prop => prop.FieldName).ToArray()), TikCommandParameterFormat.NameValue);
            //filter
            //parameters
            if (parameters != null)
            {
                foreach (ITikCommandParameter param in parameters)
                {
                    command.Parameters.Add(param);
                }
            }

            return command;
        }

        #endregion

        #region -- CHANGE TRACKER --
        /// <summary>
        /// Returns the <see cref="TikChangeTracker"/> bound to this connection.
        /// The tracker is created on first access and released when the connection is GC'd.
        /// Use it for advanced diff inspection or to opt entities in/out of tracking.
        /// </summary>
        public static TikChangeTracker ChangeTracker(this ITikConnection connection)
            => TikChangeTracker.For(connection);
        #endregion

        #region -- SAVE --
        private static void EnsureNotReadonlyEntity(TikEntityMetadata entityMetadata)
        {
            if (entityMetadata.IsReadOnly)
                throw new InvalidOperationException("Can not save R/O entity.");
        }

        private static void EnsureSupportsOrdering(TikEntityMetadata entityMetadata)
        {
            if (!entityMetadata.IsOrdered)
                throw new InvalidOperationException("Can not move entity without ordering support.");
        }

        private static void EnsureHasIdProperty(TikEntityMetadata metadata)
        {
            if (!metadata.HasIdProperty)
                throw new InvalidOperationException(string.Format("Can not update/delete non-sigleton entity which doesn't contains property for '{0}' field.", TikSpecialProperties.Id));
        }

        /// <summary>
        /// Saves entity to mikrotik router. Does insert (/add) whan entity has empty id and update(/set + /unset) when id is present).
        /// Behavior of save is modified via <see cref="TikPropertyAttribute"/> on properties.
        /// See <see cref="TikPropertyAttribute.DefaultValue"/>, <see cref="TikPropertyAttribute.UnsetOnDefault"/>.
        /// <para>
        /// <b>Load and save an entity on the SAME connection.</b> The change tracker is scoped to the
        /// <see cref="ITikConnection"/> it was loaded on, so an entity loaded on one connection and saved on
        /// another has no snapshot on the saving connection — and under
        /// <see cref="TikSaveMode.OnlyChanges"/> (the 4.x default) "no snapshot" means <b>every writable
        /// field is sent</b>, not just the ones you changed. Any field the loading transport spells
        /// differently from the saving one is then written back in the loading transport's spelling.
        /// <see cref="TikSaveMode.FullUpdate"/> does not have this problem — it resolves the difference by
        /// re-reading the row over the SAVING connection — but the two-connection pattern is not a supported
        /// way to use the mapper either way.
        /// </para>
        /// </summary>
        /// <typeparam name="TEntity">Saved entitie type.</typeparam>
        /// <param name="connection">Tik connection used to save.</param>
        /// <param name="entity">Saved entity.</param>
        /// <param name="usedFieldsFilter">List of field names (on mikrotik) which should be modified. If is not null, only listed fields will be modified.</param>
        /// <param name="saveMode">Controls which fields are sent — see <see cref="TikSaveMode"/>.</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
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
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            string? id = ResolveSaveId(entity, metadata);

            if (IsCreate(metadata, id))
            {
                var createCmd = BuildCreateCommand(connection, entity, metadata, usedFieldsFilter);
                FinishCreate(connection, entity, metadata, createCmd.ExecuteScalar());
                return;
            }

            if (NeedsFilterResolution(metadata, usedFieldsFilter))
            {
                var resolution = ResolveUpdateFilter(connection, entity, metadata, saveMode);
                if (resolution.Kind == UpdateFilterKind.NothingChanged)
                    return; // nothing changed — skip the API call
                // id: non-null here. This branch is unreachable for a singleton (NeedsFilterResolution is
                // false for those), and IsCreate above already ruled out the non-singleton "empty id" case.
                usedFieldsFilter = resolution.Kind == UpdateFilterKind.NeedsUnmodifiedEntity
                    ? entity.GetDifferentFields(connection.LoadById<TEntity>(id!))
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

        internal static void EnsureNotReadonly(TikEntityMetadata metadata) => EnsureNotReadonlyEntity(metadata);

        /// <summary>
        /// The nullable fields this entity was <b>loaded with a value</b> and now holds <c>null</c> for —
        /// i.e. the caller cleared them, and an update should <c>/unset</c> them on the router.
        /// </summary>
        /// <remarks>
        /// The snapshot is what makes this answerable, and it is the only thing that does. Null on its own is
        /// ambiguous: on an entity that was never loaded it means "nothing was said", and treating that as a
        /// clearing would delete router state on the strength of silence — including every field a partial
        /// <c>.proplist</c> load never populated. Against a snapshot the two separate cleanly, so a clearing
        /// is recognised only where there is evidence of something to clear. An untracked entity yields
        /// nothing here, which leaves those fields skipped as before.
        /// </remarks>
        internal static ISet<string> ResolveClearedFields<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata)
        {
            var cleared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var snapshot = TikChangeTracker.For(connection).GetSnapshot(entity!);
            if (snapshot == null)
                return cleared;

            foreach (var property in metadata.Properties)
            {
                if (property.IsReadOnly || !property.IsNullable)
                    continue;
                if (property.GetEntityValue(entity!) != null)
                    continue;
                if (!snapshot.IsTracked(property.FieldName))
                    continue;   // never loaded, so nothing is known to have been cleared
                if (snapshot.TryGetValue(property.FieldName, out string? previous) && previous != null)
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

        internal static bool NeedsFilterResolution(TikEntityMetadata metadata, IEnumerable<string>? usedFieldsFilter)
            => !metadata.IsSingleton && usedFieldsFilter == null;

        internal static ITikCommand BuildCreateCommand<TEntity>(ITikConnection connection, TEntity entity,
            TikEntityMetadata metadata, IEnumerable<string>? usedFieldsFilter)
        {
            ITikCommand createCmd = connection.CreateCommand(metadata.EntityPath + "/add", TikCommandParameterFormat.NameValue);

            foreach (var property in metadata.Properties
                .Where(pm => !pm.IsReadOnly)
                .Where(pm => usedFieldsFilter == null || usedFieldsFilter.Contains(pm.FieldName, StringComparer.OrdinalIgnoreCase)))
            {
                // Send non-default values; always send mandatory fields, since the router
                // requires them on /add even when their value happens to equal the default
                // (e.g. an enum whose mandatory selection is also its zero/default member).
                if (!property.HasDefaultValue(entity!) || property.IsMandatory)
                {
                    string? value = property.GetEntityValue(entity!);
                    // A mandatory field the caller left null is the one case where the two conditions
                    // disagree: there is nothing to send, and sending the word without a value would be
                    // worse than letting the router say what it requires.
                    if (value != null)
                        createCmd.AddParameter(property.FieldName, value);
                }
            }

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
        /// Builds the update traffic: the <c>/unset</c> commands (which must run first) and the single
        /// <c>/set</c>, or <c>null</c> when the update turned out to carry no fields.
        /// </summary>
        internal static (ITikCommand? SetCommand, IList<ITikCommand> UnsetCommands) BuildUpdateCommands<TEntity>(
            ITikConnection connection, TEntity entity, TikEntityMetadata metadata,
            IEnumerable<string>? usedFieldsFilter, string? id)
        {
            ITikCommand setCmd = connection.CreateCommand(metadata.EntityPath + "/set", TikCommandParameterFormat.NameValue);
            List<string> fieldsToUnset = new List<string>();
            var clearedFields = ResolveClearedFields(connection, entity, metadata);

            foreach (var property in metadata.Properties
                .Where(pm => !pm.IsReadOnly)
                .Where(pm => usedFieldsFilter == null || usedFieldsFilter.Contains(pm.FieldName, StringComparer.OrdinalIgnoreCase)))
            {
                if (property.HasDefaultValue(entity!) && property.UnsetOnDefault)
                    fieldsToUnset.Add(property.FieldName);
                else if (clearedFields.Contains(property.FieldName))
                    fieldsToUnset.Add(property.FieldName);   // loaded with a value, set to null → unset
                else
                {
                    string? value = property.GetEntityValue(entity!);
                    // Null that is NOT a clearing: the caller never said anything about this field (only a
                    // nullable property can say that). Sending it would put the word on the wire with no
                    // value, and unsetting it would destroy what the router holds on the strength of silence.
                    if (value != null)
                        setCmd.AddParameter(property.FieldName, value); //full update (all values)
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
                // id: null only for a singleton, and BuildUpdateCommands is only reached past Save's IsCreate
                // check, which already requires a non-empty id for every non-singleton entity.
                unsetCmd.AddParameter(TikSpecialProperties.Id, id!, TikCommandParameterFormat.NameValue);
                unsetCmd.AddParameter(TikSpecialProperties.UnsetValueName, fld);
                unsetCommands.Add(unsetCmd);
            }

            if (!setCmd.Parameters.Any())
                return (null, unsetCommands);

            if (!metadata.IsSingleton)
                setCmd.AddParameter(TikSpecialProperties.Id, id!, TikCommandParameterFormat.NameValue); // non-null: see the note above
            return (setCmd, unsetCommands);
        }

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
        /// <typeparam name="TEntity">Saved entitie type.</typeparam>
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
        /// field on it reads correct — and because the router <b>appends</b> a created row, so a new entity
        /// placed in the middle of <paramref name="modifiedList"/> owes its position entirely to the follow-up
        /// move.
        /// </para>
        /// <para>
        /// Both lists are enumerated more than once and are materialized internally; the entities in
        /// <paramref name="modifiedList"/> are the same instances throughout, which is what lets a newly
        /// created row be moved by the <c>.id</c> that <see cref="Save"/> just wrote onto it.
        /// </para>
        /// </remarks>
        /// <seealso cref="TikEntityObjectsExtensions.CloneEntity"/>
        /// <seealso cref="TikEntityObjectsExtensions.CloneEntityList"/>
        /// <seealso cref="Save"/>
        /// <seealso cref="Move"/>
        public static void SaveListDifferences<TEntity>(this ITikConnection connection, IEnumerable<TEntity> modifiedList, IEnumerable<TEntity> unmodifiedList)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            EnsureHasIdProperty(metadata);
            var idProperty = metadata.IdProperty!; // non-null: EnsureHasIdProperty just verified it

            // Materialized once: the ordering pass below walks the modified list a second time, and it has to
            // be the same instances in the same order — Save assigns the new .id to the entity it was given,
            // and a re-enumerated lazy sequence would hand back fresh instances without one.
            var modifiedItems = modifiedList.ToList();
            var unmodifiedItems = unmodifiedList.ToList();
            modifiedList = modifiedItems;
            unmodifiedList = unmodifiedItems;

            // entity: TEntity is only constrained to new(), so the compiler treats it as possibly null;
            // every entity here is an actual instance from modifiedList/unmodifiedList. The .id read itself
            // (GetEntityValue's result) is non-null by construction of the Where/dictionary-key filters below.
            var entitiesToCreate = modifiedList.Where(entity => string.IsNullOrEmpty(idProperty.GetEntityValue(entity!))).ToList(); // new items in modifiedList

            Dictionary<string, TEntity> modifiedEntities = modifiedList
                .Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity!)))
                .ToDictionary(entity => idProperty.GetEntityValue(entity!)!); //all entities from modified list with ids
            Dictionary<string, TEntity> unmodifiedEntities = unmodifiedList
                //.Where(entity => !string.IsNullOrEmpty(idProperty.GetEntityValue(entity))) - entity in unmodified list has id (is loaded from miktrotik)
                .ToDictionary(entity => idProperty.GetEntityValue(entity!)!); //all entities from unmodified list with ids

            // The router's order as it stands, kept current through the delete/create below so the ordering
            // pass can tell what still has to move. Only tracked for an ordered menu — on an unordered one
            // there is no order to keep and /move is not a legal command.
            var currentOrder = metadata.IsOrdered
                ? new TikOrderTracker(unmodifiedItems.Select(entity => idProperty.GetEntityValue(entity!)!))
                : null;

            //DELETE
            foreach (string entityId in unmodifiedEntities.Keys.Where(id => !modifiedEntities.ContainsKey(id))) //missing in modified -> deleted
            {
                Delete(connection, unmodifiedEntities[entityId]);
                if (currentOrder != null)
                    currentOrder.Remove(entityId);
            }

            //CREATE
            foreach (TEntity entity in entitiesToCreate)
            {
                Save(connection, entity);
                if (currentOrder != null)
                    currentOrder.Append(idProperty.GetEntityValue(entity!)!); //Save wrote the new .id back onto the entity; the router appended it
            }

            //UPDATE
            foreach (string entityId in unmodifiedEntities.Keys.Where(id => modifiedEntities.ContainsKey(id))) // are in both modified and unmodified -> compare values (update/skip)
            {
                TEntity modifiedEntity = modifiedEntities[entityId];
                TEntity unmodifiedEntity = unmodifiedEntities[entityId];

                if (!modifiedEntity.EntityEquals(unmodifiedEntity))
                {
                    Save(connection, modifiedEntity);
                }
            }

            //ORDER
            if (currentOrder != null)
            {
                // Last to first, each row moved in front of the one already placed — the same walk
                // TikListMerge does, over the same TikOrderTracker. Taken in this direction the anchor is
                // always a row whose final position is settled, so one pass is enough.
                TEntity? anchor = default(TEntity);
                bool hasAnchor = false;   //not "anchor != null": TEntity is only constrained to new(), so a struct would read as non-null
                for (int i = modifiedItems.Count - 1; i >= 0; i--)
                {
                    TEntity entity = modifiedItems[i];
                    string movedKey = idProperty.GetEntityValue(entity!)!;

                    if (hasAnchor)
                    {
                        string anchorKey = idProperty.GetEntityValue(anchor!)!; // hasAnchor guards anchor being a real, loaded entity
                        int movedIdx, anchorIdx;
                        if (currentOrder.NeedsMove(movedKey, anchorKey, out movedIdx, out anchorIdx))
                        {
                            Move(connection, entity, anchor);
                            currentOrder.ApplyMove(movedKey, anchorKey);
                        }
                    }

                    anchor = entity;
                    hasAnchor = true;
                }
            }
        }
        #endregion

        #region -- DELETE --
        /// <summary>
        /// Deletes entity (.id is the key) on mikrotik router.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete entity.</param>
        /// <param name="entity">Entity to be deleted (.id property is the key)</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        public static void Delete<TEntity>(this ITikConnection connection, TEntity entity)
        {
            BuildDeleteCommand(connection, entity).ExecuteNonQuery();
        }

        /// <summary>Shared with <c>DeleteAsync</c> — see the note on <see cref="ResolveSaveId"/>.</summary>
        internal static ITikCommand BuildDeleteCommand<TEntity>(ITikConnection connection, TEntity entity)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureNotReadonlyEntity(metadata);
            EnsureHasIdProperty(metadata);
            // IdProperty is non-null: EnsureHasIdProperty just verified it. The .id value itself can
            // legitimately be null/empty for a never-loaded entity — that is exactly what is checked next.
            string? id = metadata.IdProperty!.GetEntityValue(entity!);
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Entity has no .id (entity is not loaded from mikrotik router)", "entity");

            // `!`: string.IsNullOrEmpty isn't NotNullWhen-annotated on netstandard2.0, so the compiler can't
            // narrow through the throw above even though it guarantees id is non-empty here.
            return connection.CreateCommandAndParameters(metadata.EntityPath + "/remove", TikCommandParameterFormat.NameValue,
                TikSpecialProperties.Id, id!);
        }

        /// <summary>
        /// Deletes all entities of given type on mikrotik router.
        /// </summary>
        /// <typeparam name="TEntity">Deleted entity type.</typeparam>
        /// <param name="connection">Tik connection used to delete entity.</param>
        /// <returns>Number of deleted entities. </returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        public static int DeleteAll<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            var list = connection.LoadAll<TEntity>();
            int result = list.Count();

            connection.SaveListDifferences(new List<TEntity>() /*empty list as expected => delete all*/, list);

            return result;
        }
        #endregion

        #region -- MOVE --
        /// <summary>
        /// Moves given <paramref name="entityToMove"/> before given <paramref name="entityToMoveBefore"/>.
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move entity.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <param name="entityToMoveBefore">Entity before which is given <paramref name="entityToMove"/> moved.</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        public static void Move<TEntity>(this ITikConnection connection, TEntity entityToMove, TEntity entityToMoveBefore)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            EnsureSupportsOrdering(metadata);
            EnsureHasIdProperty(metadata);

            // IdProperty is non-null: EnsureHasIdProperty just verified it. idToMove/idToMoveBefore are not
            // guarded against an unloaded entity here (unlike BuildDeleteCommand) — see the caller contract;
            // `!` types the existing behaviour rather than adding a new check.
            string idToMove = metadata.IdProperty!.GetEntityValue(entityToMove!)!;
            string? idToMoveBefore = entityToMoveBefore != null ? metadata.IdProperty!.GetEntityValue(entityToMoveBefore) : null;

            ITikCommand cmd = connection.CreateCommandAndParameters(metadata.EntityPath + "/move", TikCommandParameterFormat.NameValue,
                "numbers", idToMove);

            if (entityToMoveBefore != null)
                cmd.AddParameter("destination", idToMoveBefore!);

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Moves given <paramref name="entityToMove"/> to the end (make it last entity in the list).
        /// </summary>
        /// <typeparam name="TEntity">Moved entity type.</typeparam>
        /// <param name="connection">Tik connection used to move entity.</param>
        /// <param name="entityToMove">Entity to be moved.</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        public static void MoveToEnd<TEntity>(this ITikConnection connection, TEntity entityToMove)
        {
            Move(connection, entityToMove, default(TEntity));
        }

        #endregion

        #region -- MERGE --
        /// <summary>
        /// Creates merge object. This object should be setuped (via fluent API) and finaly <see cref="TikListMerge{TEntity}.Save"/> must be called.
        /// </summary>
        /// <typeparam name="TEntity">Type of the entity in list to merge.</typeparam>
        /// <param name="connection">Tik connection used to update state of entities.</param>
        /// <param name="expected">Expected state on mikrotik router (Missing items will be added, others will be updated if are different).</param>
        /// <param name="original">Actual state on mikrotik router. (Surplus items will be deleted).</param>
        /// <returns>Merge object, that should be setuped (via fluent API) and finaly <see cref="TikListMerge{TEntity}.Save"/> must be called on this object to perform operations on mikrotik router.</returns>
        /// <example>
        /// var original = connection.LoadAll{QueueTree}().Where(q => q.Name == "Q1" || q.Name == "Q2" || q.Name.StartsWith("Q3")); //just subset of actual QT items
        /// string unique = Guid.NewGuid().ToString();
        /// List{QueueTree} expected = new List{QueueTree}()  //new expected subset of QT items
        ///    {
        ///        new QueueTree() { Name = "Q1", Parent = "global", PacketMark = "PM1" },
        ///        new QueueTree() { Name = "Q2", Parent = "global", PacketMark = "PM2", Comment = unique }, //always update
        ///        new QueueTree() { Name = "Q3 " + unique, Parent = "global", PacketMark = "PM3" }, // always insert + delete from previous run
        ///    };
        /// connection.CreateMerge(expected, original) //access to merge object            
        ///    .WithKey(queue => queue.Name) // items with the same name are the same (name is the key)
        ///    .Field(q => q.Parent)         // we are updating just Parent, PacketMark and Comment fields
        ///    .Field(q => q.PacketMark)
        ///    .Field(q => q.Comment)
        ///    .Save();                      // modify mikrotik router QueueTree 
        /// </example>
        public static TikListMerge<TEntity> CreateMerge<TEntity>(this ITikConnection connection, IEnumerable<TEntity> expected, IEnumerable<TEntity> original)
            where TEntity: new()
        {
            return new TikListMerge<TEntity>(connection, expected, original);
        }

        #endregion

        #region -- EXECUTE --
        /// <summary>
        /// Excecutes given <paramref name="commandText"/> on router and ensures that operation was sucessfull.
        /// </summary>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="commandText">Command text</param>
        /// <param name="parameters">Optional list of parameters</param>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        public static void ExecuteNonQuery(this ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
        {
            var command = connection.CreateCommand(commandText, parameters);
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Executes given <paramref name="commandText"/> on router and ensures that operation returns one value (=ret parameter), which is returned as result.
        /// </summary>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="commandText">Command text</param>
        /// <param name="parameters">Optional list of parameters</param>
        /// <returns>Value returned by router.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        public static string ExecuteScalar(this ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
        {
            var command = connection.CreateCommand(commandText, parameters);
            return command.ExecuteScalar();
        }
        #endregion
    }
}
