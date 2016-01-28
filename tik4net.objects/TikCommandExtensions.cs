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
        /// <seealso cref="TikConnectionExtensions.LoadList{TEntity}(ITikConnection, ITikCommandParameter[])"/>
        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikCommand command)
            where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");

            var responseSentences = command.ExecuteList();

            return responseSentences.Select(sentence => CreateObject<TEntity>(sentence)).ToList();
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
        /// <seealso cref="TikConnectionExtensions.LoadWithDuration{TEntity}(ITikConnection, int, ITikCommandParameter[])"/>
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
        /// <seealso cref="TikConnectionExtensions.LoadAsync{TEntity}(ITikConnection, Action{TEntity}, Action{Exception}, ITikCommandParameter[])"/>
        public static void LoadAsync<TEntity>(this ITikCommand command,
                    Action<TEntity> onLoadItemCallback, Action<Exception> onExceptionCallback = null)
                    where TEntity : new()
        {
            Guard.ArgumentNotNull(command, "command");
            Guard.ArgumentNotNull(onLoadItemCallback, "onLoadItemCallback");

            command.ExecuteAsync(
                reSentence => onLoadItemCallback(CreateObject<TEntity>(reSentence)),
                trapSentence =>
                {
                    if (onExceptionCallback != null)
                        onExceptionCallback(new TikCommandException(command, trapSentence));
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