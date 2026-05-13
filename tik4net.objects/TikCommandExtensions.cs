using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects
{
    /// <summary>
    /// Main mapper extension - extends <see cref="ITikCommand"/>.
    /// Supports Load metods - maps command results to entities.
    /// </summary>
    public static class TikCommandExtensions
    {
        /// <summary>
        /// Loads entity list from given command.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <returns>List (or empty list) of loaded entities.</returns>
        /// <seealso cref="LoadSingle{TEntity}(ITikCommand)"/>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikCommand command)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");

            var responseSentences = command.ExecuteList();

            return responseSentences.Select(sentence => CreateObject<TEntity>(sentence)).ToList();
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikCommand)"/>, ensures that result contains exactly one row.
        /// </summary>
        /// <param name="command">Command</param>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <returns>Loaded single entity.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity LoadSingle<TEntity>(this ITikCommand command)
            where TEntity : new()
        {
            var candidates = LoadList<TEntity>(command);
            
            var cnt = candidates.Count();
            if (cnt == 0)
                throw new TikNoSuchItemException(command);
            else if (cnt > 1)
                throw new TikCommandAmbiguousResultException(command, cnt);
            else
                return candidates.Single();
        }

        /// <summary>
        /// Alias to <see cref="LoadList{TEntity}(ITikCommand)"/> without filter, ensures that result contains exactly one row.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="command">Command</param>
        /// <returns>Loaded single entity or null.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        public static TEntity LoadSingleOrDefault<TEntity>(this ITikCommand command)
            where TEntity : new()
        {
            var candidates = LoadList<TEntity>(command);

            var cnt = candidates.Count();
            if (cnt == 0)
                return default(TEntity);
            else if (cnt > 1)
                throw new TikCommandAmbiguousResultException(command, cnt);
            else
                return candidates.Single();
        }

        /// <summary>
        /// Calls command and reads all returned rows for given <paramref name="durationSec"/> period.
        /// After this period calls cancell to mikrotik router and returns all loaded rows.
        /// Throws exception if any 'trap' row occurs.
        /// </summary>
        /// <typeparam name="TEntity">Loaded entities type.</typeparam>
        /// <param name="command">Tik command executed to load.</param>
        /// <param name="durationSec">Loading period.</param>
        /// <returns>List (or empty list) of loaded entities.</returns>
        /// <seealso cref="ITikCommand.ExecuteListWithDuration(int)"/>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        public static IEnumerable<TEntity> LoadWithDuration<TEntity>(this ITikCommand command, int durationSec)
                    where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");

            var responseSentences = command.ExecuteListWithDuration(durationSec);

            return responseSentences.Select(sentence => CreateObject<TEntity>(sentence)).ToList();
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
        /// <param name="command">Tik command executed to load.</param>
        /// <param name="onLoadItemCallback">Callback called for each loaded !re row</param>
        /// <param name="onExceptionCallback">Callback called when error occurs (!trap row is returned)</param>
        /// <param name="onDoneCallback">Callback called at the end of command run (!done row is returned). Usefull for cleanup operations at the end of command lifecycle. You can also use synchronous call <see cref="ITikCommand.CancelAndJoin()"/> from calling thread and do cleanup after it.</param>
        public static void LoadAsync<TEntity>(this ITikCommand command,
                    Action<TEntity> onLoadItemCallback, 
                    Action<Exception> onExceptionCallback = null,
                    Action onDoneCallback = null)
                    where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");
            Guard.ArgumentNotNull(onLoadItemCallback, "onLoadItemCallback");

            command.ExecuteAsync(
                reSentence => onLoadItemCallback(CreateObject<TEntity>(reSentence)),
                trapSentence =>
                {
                    if (onExceptionCallback != null)
                        onExceptionCallback(new TikCommandTrapException(command, trapSentence));
                },
                () =>
                {
                    if (onDoneCallback != null)
                        onDoneCallback();
                });
        }

        /// <summary>
        /// Starts asynchronous listening for changes in the entity list via the RouterOS <c>/listen</c> command.
        /// Unlike <see cref="LoadAsync{TEntity}(ITikCommand, Action{TEntity}, Action{Exception}, Action)"/> which uses <c>/print</c>,
        /// this method sends <c>!re</c> sentences only when the list changes — it never sends <c>!done</c>.
        /// Stop by calling <see cref="ITikCommand.Cancel"/> or <see cref="ITikCommand.CancelAndJoin()"/>.
        /// When an item is deleted the router sends <c>=.dead=yes</c>; the optional <paramref name="onDeletedCallback"/>
        /// receives the <c>.id</c> of the deleted item instead of a deserialized entity.
        /// </summary>
        /// <typeparam name="TEntity">Entity type whose path was used to build the <c>/listen</c> command.</typeparam>
        /// <param name="command">Command with path ending in <c>/listen</c>.</param>
        /// <param name="onChangeCallback">Called for each changed item.</param>
        /// <param name="onDeletedCallback">Called with the deleted item's <c>.id</c> when <c>=.dead=yes</c> is received. Can be <c>null</c>.</param>
        /// <param name="onExceptionCallback">Called when a <c>!trap</c> is received.</param>
        public static void LoadListen<TEntity>(this ITikCommand command,
            Action<TEntity> onChangeCallback,
            Action<string> onDeletedCallback = null,
            Action<Exception> onExceptionCallback = null)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");
            Guard.ArgumentNotNull(onChangeCallback, "onChangeCallback");

            command.ExecuteAsync(
                reSentence =>
                {
                    // RouterOS documents =.dead=yes but sends =.dead=true in practice — accept both
                    var deadValue = reSentence.GetResponseFieldOrDefault(".dead", null);
                    if (deadValue == "yes" || deadValue == "true")
                    {
                        if (onDeletedCallback != null)
                            onDeletedCallback(reSentence.GetResponseFieldOrDefault(TikSpecialProperties.Id, null));
                    }
                    else
                    {
                        onChangeCallback(CreateObject<TEntity>(reSentence));
                    }
                },
                trapSentence =>
                {
                    if (onExceptionCallback != null)
                        onExceptionCallback(new TikCommandTrapException(command, trapSentence));
                });
        }

        private static TEntity CreateObject<TEntity>(ITikReSentence sentence)
            where TEntity : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<TEntity>();

            TEntity result = new TEntity();
            foreach (var property in metadata.Properties)
            {
                property.SetEntityValue(result, GetValueFromSentence(sentence, property));
            }

            return result;
        }

        private static string GetValueFromSentence(ITikReSentence sentence, TikEntityPropertyAccessor property)
        {
            //Read field value (or get default value)
            if (property.IsMandatory)
                return sentence.GetResponseField(property.FieldName);
            else
                return sentence.GetResponseFieldOrDefault(property.FieldName, property.DefaultValue);
        }
    }
}