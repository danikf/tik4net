using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using tik4net.Connection;
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
    /// <item><see cref="LoadWithCallback{TEntity}(ITikConnection, Action{TEntity}, Action{Exception}, ITikCommandParameter[])"/></item>
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
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static partial class TikConnectionExtensions
    {
        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> without filter.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <returns>The loaded entities, as a list (empty when nothing matched). The read is over when this returns:
        /// enumerating the list again, or indexing it, sends nothing to the router.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Command is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        public static IList<TEntity> LoadAll<TEntity>(this ITikConnection connection)
            where TEntity : new()
        {
            return LoadList<TEntity>(connection);
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/> optionally with filter, ensures that result contains exactly one row.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>Loaded single entity.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Command is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity LoadSingle<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = command.LoadList<TEntity>();

            return ExactlyOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/>, optionally filtered; returns the one row,
        /// or the type default when nothing matched.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>The loaded entity, or the type default when nothing matched.</returns>
        /// <inheritdoc cref="LoadList{TEntity}(ITikConnection, ITikCommandParameter[])" path="/exception"/>
        public static TEntity? LoadSingleOrDefault<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = command.LoadList<TEntity>();

            return AtMostOne(connection, command, entities, metadata);
        }

        /// <summary>
        /// Loads entity with specified id. Throws <see cref="TikNoSuchItemException"/> when no row matches.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="id">Entity id.</param>
        /// <returns>The loaded entity.</returns>
        /// <inheritdoc cref="LoadSingle{TEntity}(ITikConnection, ITikCommandParameter[])" path="/exception"/>
        public static TEntity LoadById<TEntity>(this ITikConnection connection, string id)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, connection.CreateParameter(TikSpecialProperties.Id, id));
            var candidates = command.LoadList<TEntity>();

            return ExactlyOne(connection, command, candidates, metadata);
        }

        /// <summary>
        /// Loads entity with specified name. Throws <see cref="TikNoSuchItemException"/> when no row matches.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="name">Entity name.</param>
        /// <returns>The loaded entity.</returns>
        /// <inheritdoc cref="LoadSingle{TEntity}(ITikConnection, ITikCommandParameter[])" path="/exception"/>
        public static TEntity LoadByName<TEntity>(this ITikConnection connection, string name)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, connection.CreateParameter("name", name));
            var candidates = command.LoadList<TEntity>();

            return ExactlyOne(connection, command, candidates, metadata);
        }

        /// <summary>
        /// Loads entity list. Could be filtered with <paramref name="filterParameters"/>.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="filterParameters">Optional list of filter parameters (interpreted as connected with AND)</param>
        /// <returns>The loaded entities, as a list (empty when nothing matched). The read is over when this returns:
        /// enumerating the list again, or indexing it, sends nothing to the router.</returns>
        /// <seealso cref="TikCommandExtensions.LoadList{TEntity}(ITikCommand)"/>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Command is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static IList<TEntity> LoadList<TEntity>(this ITikConnection connection, params ITikCommandParameter[] filterParameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, filterParameters);
            var entities = command.LoadList<TEntity>();
            RegisterSnapshots(connection, entities, metadata, command);
            return entities;
        }

        /// <summary>
        /// Calls command and reads all returned rows for given <paramref name="duration"/>.
        /// After this period calls cancel to mikrotik router and returns all loaded rows.
        /// Throws exception if any 'trap' row occurs.
        /// Rows are tracked like any loaded entity, so <c>Save</c> sends only what changed.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="duration">Loading period. A negative value is zero.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns>The loaded entities, as a list (empty when nothing matched). The read is over when this returns:
        /// enumerating the list again, or indexing it, sends nothing to the router.</returns>
        /// <seealso cref="TikCommandExtensions.LoadWithDuration{TEntity}(ITikCommand, TimeSpan)"/>
        /// <inheritdoc cref="LoadAll{TEntity}(ITikConnection)" path="/exception"/>
        public static IList<TEntity> LoadWithDuration<TEntity>(this ITikConnection connection, TimeSpan duration, params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var entities = command.LoadWithDuration<TEntity>(duration);
            RegisterSnapshots(connection, entities, metadata, command);
            return entities;
        }

        /// <summary>
        /// Calls command and starts background reading thread. After that returns control to calling thread.
        /// All read rows are returned as callbacks (<paramref name="onLoadItemCallback"/>, <paramref name="onExceptionCallback"/>) from loading thread.
        /// REMARKS: if you want to propagate loaded values to GUI, you should use some kind of synchronization or Invoke, because 
        /// callbacks are called from non-ui thread.
        /// The running load can be terminated by <see cref="ITikCommand.Cancel"/> or <see cref="ITikCommand.CancelAndJoin()"/> call. 
        /// Command is returned as result of the method.
        /// </summary>
        /// <remarks>
        /// To be told when the load has ended, use the overload with <c>onDoneCallback</c>
        /// (<see cref="LoadWithCallback{TEntity}(ITikConnection, Action{TEntity}, Action{Exception}, Action, ITikCommandParameter[])"/>).
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="onLoadItemCallback">Callback called for each loaded !re row</param>
        /// <param name="onExceptionCallback">Callback called when the router answers with an error (<c>!trap</c>): it receives the router's trap, classified as every command's trap is: a <see cref="TikNoSuchItemException"/>, <see cref="TikNoSuchCommandException"/> or <see cref="TikAlreadyHaveSuchItemException"/> where the message says so, else a <see cref="TikCommandTrapException"/>.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns><see cref="ITikCommand"/> which is already running the async load operation. You can cancel the running operation by <see cref="ITikCommand.Cancel"/> method call.</returns>
        /// <seealso cref="TikCommandExtensions.LoadWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{Exception}, Action)"/>
        public static ITikCommand LoadWithCallback<TEntity>(this ITikConnection connection,
            Action<TEntity> onLoadItemCallback, Action<Exception>? onExceptionCallback = null,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
            => LoadWithCallback(connection, onLoadItemCallback, onExceptionCallback, onDoneCallback: null, parameters);

        /// <summary>
        /// As <see cref="LoadWithCallback{TEntity}(ITikConnection, Action{TEntity}, Action{Exception}, ITikCommandParameter[])"/>,
        /// and calls <paramref name="onDoneCallback"/> once the load has ended without an error: after the last row, or when
        /// <see cref="ITikCommand.Cancel"/> or the caller's own <see cref="ITikConnection.Close"/> stopped it. After an error only <paramref name="onExceptionCallback"/> is called.
        /// Callbacks are called from a background thread.
        /// </summary>
        /// <remarks>
        /// <paramref name="onExceptionCallback"/> has no default here, so the shorter call shapes
        /// (<c>LoadWithCallback&lt;T&gt;(onItem)</c>, <c>LoadWithCallback&lt;T&gt;(onItem, onError, filter)</c>) keep binding
        /// to the overload without <paramref name="onDoneCallback"/>. Pass <c>null</c> for no error callback.
        /// </remarks>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="connection">Tik connection used to load.</param>
        /// <param name="onLoadItemCallback">Callback called for each loaded row.</param>
        /// <param name="onExceptionCallback">Callback called when the router answers with an error (<c>!trap</c>): it receives the router's trap, classified as every command's trap is: a <see cref="TikNoSuchItemException"/>, <see cref="TikNoSuchCommandException"/> or <see cref="TikAlreadyHaveSuchItemException"/> where the message says so, else a <see cref="TikCommandTrapException"/>. May be <c>null</c>.</param>
        /// <param name="onDoneCallback">Callback called once the load has ended without an error; not after an error. May be <c>null</c>.</param>
        /// <param name="parameters">Optional list of filters/parameters (interpreted as connected with AND)</param>
        /// <returns><see cref="ITikCommand"/> which is already running the load. You can cancel it by <see cref="ITikCommand.Cancel"/>.</returns>
        /// <seealso cref="TikCommandExtensions.LoadWithCallback{TEntity}(ITikCommand, Action{TEntity}, Action{Exception}, Action)"/>
        public static ITikCommand LoadWithCallback<TEntity>(this ITikConnection connection,
            Action<TEntity> onLoadItemCallback, Action<Exception>? onExceptionCallback, Action? onDoneCallback,
            params ITikCommandParameter[] parameters)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(connection, nameof(connection));
            Guard.ArgumentNotNull(onLoadItemCallback, nameof(onLoadItemCallback));

            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            var command = CreateLoadCommandWithFilter<TEntity>(connection, parameters);
            var tracker = TikChangeTracker.For(connection);
            var trackedFields = GetProplistFields(command, metadata);

            command.LoadWithCallback<TEntity>(
                entity => {
                    tracker.TakeSnapshot(entity, metadata, trackedFields);
                    onLoadItemCallback(entity);
                },
                onExceptionCallback,
                onDoneCallback);
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
        /// <remarks>
        /// Rows that exist when the listen starts are not reported. The transports without server push (REST, the
        /// CLI family, WinBox native) emulate <c>/listen</c> by re-reading the table; they read it once before this
        /// call returns, so a change made after it returns is reported there too, and the call takes as long as
        /// one read of the table. A failure of that read arrives through <paramref name="onExceptionCallback"/>;
        /// the call does not throw it.
        /// </remarks>
        /// <typeparam name="TEntity">Entity type to listen to (must have a <c>[TikEntity]</c> attribute).</typeparam>
        /// <param name="connection">Active connection.</param>
        /// <param name="onChangeCallback">Called for each changed or added item.</param>
        /// <param name="onDeletedCallback">Called with the <c>.id</c> of a deleted item. Can be <c>null</c>.</param>
        /// <param name="onExceptionCallback">Called when a <c>!trap</c> is received, with the router's trap, classified as every command's trap is: a <see cref="TikNoSuchItemException"/>, <see cref="TikNoSuchCommandException"/> or <see cref="TikAlreadyHaveSuchItemException"/> where the message says so, else a <see cref="TikCommandTrapException"/>.</param>
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
            Guard.ArgumentNotNull(connection, nameof(connection));
            Guard.ArgumentNotNull(onChangeCallback, nameof(onChangeCallback));

            var command = CreateListenCommandWithFilter<TEntity>(connection, parameters);
            command.LoadListenWithCallback<TEntity>(onChangeCallback, onDeletedCallback, onExceptionCallback);
            return command;
        }

        internal static ITikCommand CreateListenCommandWithFilter<TEntity>(ITikConnection connection, params ITikCommandParameter[] parameters)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
            ITikCommand command = connection.CreateCommand(metadata.EntityPath + "/listen", metadata.LoadDefaultParameterFormat);
            SetHints(command, metadata.LabelHints);

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
            var entity = TikCommandExtensions.ExactlyOneRow(command, entities);
            TikChangeTracker.For(connection).TakeSnapshot(entity, metadata, GetProplistFields(command, metadata));
            return entity;
        }

        /// <summary>Allows an empty result, and snapshots the row when there is one.</summary>
        internal static TEntity? AtMostOne<TEntity>(ITikConnection connection, ITikCommand command,
            IList<TEntity> entities, TikEntityMetadata metadata)
        {
            var entity = TikCommandExtensions.AtMostOneRow(command, entities);
            if (entities.Count == 1)
                TikChangeTracker.For(connection).TakeSnapshot(entity, metadata, GetProplistFields(command, metadata));
            return entity;
        }

        /// <summary>
        /// Registers snapshots for a batch of freshly loaded entities.
        /// </summary>
        internal static void RegisterSnapshots<TEntity>(ITikConnection connection, IList<TEntity> entities,
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
                proplistParam.Value!.Split(','),   // a .proplist parameter always carries its field list; only a bare '?name' filter has a null Value
                StringComparer.OrdinalIgnoreCase);

            // IncludeProplist=true sends all entity fields — treat as full load
            bool isFullLoad = metadata.Properties.All(p => fields.Contains(p.FieldName));
            return isFullLoad ? null : (IEnumerable<string>)fields;
        }

        // The entity's transport hints (TikEntityMetadata.ReadHints / LabelHints) on a command of tik4net's own: what the
        // CLI transports and WinBox native need to read or write the entity, beside the command's words. A command that
        // carries none — the binary API's, which needs none, or a caller's own — is left as it is.
        internal static void SetHints(ITikCommand command, TikTransportHints hints)
        {
            if (command is ITikTransportHintsCommand carrier)
                carrier.Hints = hints;
        }

        internal static ITikCommand CreateLoadCommandWithFilter<TEntity> (ITikConnection connection, params ITikCommandParameter[] parameters)
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            ITikCommand command = connection.CreateCommand(metadata.EntityPath + metadata.LoadCommand, metadata.LoadDefaultParameterFormat);

            // =detail=
            if (metadata.IncludeDetails)
                command.AddParameter("detail", "", TikCommandParameterFormat.NameValue);
            // How tik4net's own transports read the entity (the CLI's stats merge, :serialize, show-sensitive and flag
            // fields; WinBox native's labels): beside the words, never one of them.
            SetHints(command, metadata.ReadHints);
            //.proplist
            if (metadata.IncludeProplist)
                command.AddParameter(TikSpecialProperties.Proplist, string.Join(",", metadata.Properties
                    .SelectMany(prop => new[] { prop.FieldName }.Concat(prop.AlternateNames)).ToArray()), TikCommandParameterFormat.NameValue);
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

        /// <summary>
        /// Returns the <see cref="TikChangeTracker"/> bound to this connection.
        /// The tracker is created on first access and released when the connection is GC'd.
        /// Use it for advanced diff inspection or to opt entities in/out of tracking.
        /// </summary>
        public static TikChangeTracker ChangeTracker(this ITikConnection connection)
            => TikChangeTracker.For(connection);
    }
}
