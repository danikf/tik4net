using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Provides ADO.NET like api to mikrotik router. Should be used inside of opened <seealso cref="ITikConnection"/>.
    /// </summary>
    /// <seealso cref="ITikConnection"/>
    /// <seealso cref="ITikConnection.CreateCommand()"/>
    /// <seealso cref="TikCommandTrapException"/>
    /// <seealso cref="TikCommandFatalException"/>
    /// <seealso cref="TikCommandAbortException"/>
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
        /// Parameters of command (without '=') or filter of query (without '?').
        /// </summary>
        IList<ITikCommandParameter> Parameters { get; }

        /// <summary>
        /// Default value, how will be command parameters formated in mikrotik request. Could be overriden per parameter.
        /// </summary>
        TikCommandParameterFormat DefaultParameterFormat { get; set; }

        /// <summary>
        /// Excecutes given <see cref="CommandText"/> on router and ensures that operation was sucessfull.
        /// </summary>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        void ExecuteNonQuery();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns one value (=ret parameter) or single value in single !re row, which is returned as result.
        /// </summary>
        /// <returns>Value returned by router.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        string ExecuteScalar();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns single value (<paramref name="target"/> field) in single !re row, which is returned as result.
        /// Usefull to return one value from one selected row (for example .id of searched record).
        /// </summary>
        /// <param name="target">Name of returned field.</param>
        /// <returns>Value returned by router.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        string ExecuteScalar(string target);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns one value (=ret parameter) or single value in single !re row, which is returned as result. If value is not found, than returns <c>null</c>.
        /// </summary>
        /// <returns>Value returned by router or <c>null</c>.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        string ExecuteScalarOrDefault();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns one value (=ret parameter) or single value in single !re row, which is returned as result. If value is not found, than returns <paramref name="defaultValue"/>.
        /// </summary>
        /// <returns>Value returned by router or <paramref name="defaultValue"/>.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        string ExecuteScalarOrDefault(string defaultValue);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns one value (=ret parameter) or single value in single !re row, which is returned as result. If value is not found, than returns <paramref name="defaultValue"/>.
        /// Usefull to return one value from one selected row (for example .id of searched record).
        /// </summary>
        /// <param name="target">Name of returned field.</param>
        /// <returns>Value returned by router or <paramref name="defaultValue"/>.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        string ExecuteScalarOrDefault(string defaultValue, string target);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns exactly one row (1x !re and 1x !done) as result.        
        /// </summary>
        /// <returns>Content of !re sentence.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikNoSuchItemException">Invalid item (bad id/name etc.). Mikrotik API message: 'no such item'.</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        ITikReSentence ExecuteSingleRow();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and ensures that operation returns exactly one row (1x !re and 1x !done) as result. If not, <c>null</c> is returned.
        /// NOTE: !fail exceptions are handled as usual (throws error).
        /// </summary>
        /// <returns>Content of !re sentence or null.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikCommandAmbiguousResultException">More than one row returned.</exception>
        ITikReSentence ExecuteSingleRowOrDefault();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) as result.
        /// </summary>
        /// <returns>List of !re sentences</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        IEnumerable<ITikReSentence> ExecuteList();

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) as result.
        /// </summary>
        /// <param name="proplistFields">List of fields to be returned (only subset of fields will be returned).</param>
        /// <returns>List of !re sentences</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        IEnumerable<ITikReSentence> ExecuteList(params string[] proplistFields);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) which are returned during <paramref name="durationSec"/> wait.<br>
        /// After this period, command is automatically stopped via <see cref="CancelAndJoin()"/>.<br>
        /// Throws <see cref="TikCommandAbortException"/> if command is aborted before <paramref name="durationSec"/>.<br>
        /// Returns data if command ends before <paramref name="durationSec"/> (!done received).
        /// </summary>
        /// <param name="durationSec">How long will method wait for results.</param>
        /// <returns>List of !re sentences read.</returns>
        /// <remarks>If no error occurs, calling this method blocks calling thread for <paramref name="durationSec"/>.</remarks>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) which are returned during <paramref name="durationSec"/> wait.<br>
        /// After this period, command is automatically stopped via <see cref="CancelAndJoin()"/>.<br>
        /// Don't throw any exception if command is aborted before <paramref name="durationSec"/>. Returns <paramref name="wasAborted"/>=true instead (usefull if incomplete result is still expected).<br>
        /// Returns data if command ends before <paramref name="durationSec"/> (!done received).
        /// </summary>
        /// <param name="durationSec">How long will method wait for results.</param>
        /// <param name="wasAborted">If command has been terminated before <paramref name="durationSec"/>.</param>
        /// <param name="abortReason">Detail info if <paramref name="wasAborted"/> is true.</param>
        /// <returns>List of !re sentences read.</returns>
        /// <remarks>If no error occurs, calling this method blocks calling thread for <paramref name="durationSec"/>.</remarks>
        IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason);

        /// <summary>
        /// Calls given <see cref="CommandText"/> to router. Response is returned via <paramref name="oneResponseCallback"/> callback when it is read from mikrotik (for tag, which has been dynamically assigned).<br>
        /// REMARKS: <paramref name="oneResponseCallback"/> is called from another NON-GUI thread. If you want to show response in UI, 
        /// you should use some kind of synchronization like BeginInvoke in WinForms or SynchronizationContext. You can not touch UI controls directly without it.
        /// </summary>
        /// <param name="oneResponseCallback">Callback called periodically when response sentence is read from mikrotik.</param>
        /// <param name="errorCallback">Callback called when error occurs (command operation is than ended).</param>
        /// <param name="onDoneCallback">Callback called at the end of command run (when command is successfully finished - !done is returned). Usefull for cleanup operations at the end of command lifecycle. You can also use synchronous call <see cref="CancelAndJoin()"/> from calling thread and do cleanup after it.</param>
        /// <seealso cref="Cancel"/>
        /// <seealso cref="ITikReSentence"/>
        void ExecuteAsync(Action<ITikReSentence> oneResponseCallback, Action<ITikTrapSentence> errorCallback=null, Action onDoneCallback = null);        

        /// <summary>
        /// Adds new instance of parameter to <see cref="Parameters"/> list. Type of parameter is resolved from parameter name or from command type.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <returns>Instance of added parameter.</returns>
        ITikCommandParameter AddParameter(string name, string value);

        /// <summary>
        /// Adds new instance of parameter to <see cref="Parameters"/> list with specified <paramref name="parameterFormat"/>.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <param name="parameterFormat">How will be parameter formated in mikrotik command.</param>
        /// <returns>Instance of added parameter.</returns>
        ITikCommandParameter AddParameter(string name, string value, TikCommandParameterFormat parameterFormat);

        /// <summary>
        /// Adds new instance of parameter to <see cref="Parameters"/> list. Type of parameter is resolved from parameter name or from command type.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <returns>Command - builder pattern.</returns>
        ITikCommand WithParameter(string name, string value);

        /// <summary>
        /// Adds new instance of parameter to <see cref="Parameters"/> list with specified <paramref name="parameterFormat"/>.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value</param>
        /// <param name="parameterFormat">How will be parameter formated in mikrotik command.</param>
        /// <returns>Command - builder pattern.</returns>
        ITikCommand WithParameter(string name, string value, TikCommandParameterFormat parameterFormat);

        /// <summary>
        /// Adds newly created instances of <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="parameterNamesAndValues">Name and value of parameters for command. (name, value, name2, value2, ..., name9, value9, ...). Type of parameter is resolved from parameter name or from command type.</param>
        /// <returns>List of created parameters.</returns>
        IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues);

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteAsync"/> has been called).
        /// </summary>
        /// <seealso cref="ExecuteAsync"/>
        void Cancel();

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteAsync"/> has been called).<br>
        /// Blocks the calling thread until a thread terminates or the specified time elapses, while continuing to perform standard COM and SendMessage pumping.
        /// </summary>
        /// <seealso cref="ExecuteAsync"/>
        void CancelAndJoin();

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteAsync"/> has been called).<br>
        /// Blocks the calling thread until a thread terminates or the specified time elapses, while continuing to perform standard COM and SendMessage pumping.
        /// </summary>
        /// <param name="milisecondsTimeout">Wait timeout.</param>
        /// <returns>True if loading thread ends before given timeout.</returns>
        /// <seealso cref="ExecuteAsync"/>
        bool CancelAndJoin(int milisecondsTimeout);
    }
}
