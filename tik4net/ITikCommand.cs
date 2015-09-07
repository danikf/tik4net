using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    /// <summary>
    /// Provides ADO.NET like api to mikrotik router. Should be used inside of opened <seealso cref="ITikConnection"/>.s
    /// </summary>
    /// <seealso cref="ITikConnection"/>
    /// <seealso cref="TikCommandException"/>
    public interface ITikCommand
    {
        /// <summary>
        /// Connection assigned to command (used to perform operations on router).
        /// </summary>
        ITikConnection Connection { get; set; }

        /// <summary>
        /// Comnmand send to router (in mikrotik API format).
        /// </summary>
        string CommandText { get; set; }

        /// <summary>
        /// True when command is already running.
        /// </summary>
        /// <seealso cref="ExecuteAsync"/>
        /// <seealso cref="Cancel"/>
        bool IsRunning { get; }

        /// <summary>
        /// Parameters of command (without '=').
        /// </summary>
        IList<ITikCommandParameter> Parameters { get; }

        /// <summary>
        /// Filters of command (without '?').
        /// </summary>
        IList<ITikCommandParameter> Filters { get; }

        /// <summary>
        /// Excecutes given <see cref="CommandText"/> on router and ensures that operation was sucessfull.
        /// </summary>
        void ExecuteNonQuery();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns one value (=ret parameter), which is returned as result.
        /// </summary>
        /// <returns>Value returned by router.</returns>
        string ExecuteScalar();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns exactly one row (1x !re and 1x !done) as result.
        /// </summary>
        /// <returns>Content of !re sentence.</returns>
        ITikReSentence ExecuteSingleRow();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) as result.
        /// </summary>
        /// <returns>List of !re sentences</returns>
        IEnumerable<ITikReSentence> ExecuteList();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) which are returned during <paramref name="durationSec"/> wait.
        /// After this period, command is automatically stopped via <see cref="Cancel"/>.
        /// </summary>
        /// <param name="durationSec">How long will method wait for results.</param>
        /// <returns>List of !re sentences read.</returns>
        /// <remarks>If no error occurs, calling this method blocks calling thread for <paramref name="durationSec"/>.</remarks>
        IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec);

        /// <summary>
        /// Calls given <see cref="CommandText"/> to router. Response is returned via <paramref name="oneResponseCallback"/> callback when it is read from mikrotik (for tag, which has been dynamically assigned).
        /// REMARKS: <paramref name="oneResponseCallback"/> is called from another NON-GUI thread. If you want to show response in UI, 
        /// you should use some kind of synchronization like BeginInvoke in WinForms or SynchronizationContext. You can not touch UI controls directly without it.
        /// </summary>
        /// <returns>Wrapper to loading thread.</returns>
        /// <param name="oneResponseCallback">Callback called periodically when response sentence is read from mikrotik.</param>
        /// <param name="errorCallback">Callback called when error occurs (command operation is than ended).</param>
        /// <seealso cref="Cancel"/>
        /// <seealso cref="ITikReSentence"/>
        TikAsyncLoadingThread ExecuteAsync(Action<ITikReSentence> oneResponseCallback, Action<ITikTrapSentence> errorCallback = null);        

        /// <summary>
        /// Adds new instance of parameter to <see cref="Parameters"/> list.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <returns>Instance of added parameter.</returns>
        ITikCommandParameter AddParameter(string name, string value);

        /// <summary>
        /// Adds newly created instances of <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="parameterNamesAndValues">Name and value of parameters for command. (name, value, name2, value2, ..., name9, value9, ...)</param>
        /// <returns>List of created parameters.</returns>
        IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues);

        /// <summary>
        /// Adds new instance of parameter to <see cref="Filters"/> list.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <returns>nstance of added filter parameter.</returns>
        ITikCommandParameter AddFilter(string name, string value);

        /// <summary>
        /// Adds newly created instances of <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="filterNamesAndValues">Name and value of parameters for command. (name, value, name2, value2, ..., name9, value9, ...)</param>
        /// <returns>List of created filter parameters.</returns>
        IEnumerable<ITikCommandParameter> AddFilterAndValues(params string[] filterNamesAndValues);

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteAsync"/> has been called).
        /// </summary>
        /// <seealso cref="ExecuteAsync"/>
        void Cancel();
    }
}
