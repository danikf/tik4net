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
        /// Command sent to the router, in MikroTik API format — usually just the path and verb
        /// (<c>/ip/address/print</c>), with the words supplied through <see cref="Parameters"/>.
        /// </summary>
        /// <remarks>
        /// It may also carry the <b>whole sentence, one word per line</b>:
        /// <c>"/ip/address/print\n?address=10.0.0.1/24"</c>. Those rows are parsed into
        /// <see cref="Parameters"/> and appended to any added directly, and blank lines are ignored.
        /// <para>
        /// Off the binary API a row that is neither a parameter (<c>=name=value</c>, <c>?name=value</c>,
        /// <c>?name</c>) nor an API sentence marker (<c>.tag</c>, <c>.proplist</c>, which those transports
        /// cannot express and skip) raises an <see cref="System.ArgumentException"/> naming it. The binary
        /// API instead puts the row on the wire and lets the router judge it, which comes back as a trap.
        /// Either way a malformed row is reported rather than dropped — one leading <c>=</c> short used to
        /// mean a parameter silently missing from the command.
        /// </para>
        /// </remarks>
        string CommandText { get; set; }

        /// <summary>
        /// True when command is already running.
        /// </summary>
        /// <seealso cref="ExecuteWithCallback"/>
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
        /// <exception cref="TikCommandEmptyResponseException">Command succeeded but returned no value at all (bare !done without =ret=, or empty CLI output) - use <see cref="ExecuteNonQuery"/> for commands that print nothing.</exception>
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
        /// <exception cref="TikCommandEmptyResponseException">Command succeeded but returned no value at all (bare !done without =ret=, or empty CLI output) - use <see cref="ExecuteNonQuery"/> for commands that print nothing.</exception>
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
        string? ExecuteScalarOrDefault();

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
        string? ExecuteScalarOrDefault(string? defaultValue);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns one value (=ret parameter) or single value in single !re row, which is returned as result. If value is not found, than returns <paramref name="defaultValue"/>.
        /// Usefull to return one value from one selected row (for example .id of searched record).
        /// </summary>
        /// <param name="defaultValue">Value returned when matching record was not found.</param>
        /// <param name="target">Name of returned field.</param>
        /// <returns>Value returned by router or <paramref name="defaultValue"/>.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        /// <exception cref="TikAlreadyHaveSuchItemException">Duplicit item (duplicit id/name etc.). Mikrotik API message: 'already have such item'.</exception>
        string? ExecuteScalarOrDefault(string? defaultValue, string target);

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
        ITikReSentence? ExecuteSingleRowOrDefault();

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
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) which are returned during <paramref name="durationSec"/> wait.
        /// After this period, command is automatically stopped via <see cref="CancelAndJoin()"/>.
        /// Throws <see cref="TikCommandAbortException"/> if command is aborted before <paramref name="durationSec"/>.
        /// Returns data if command ends before <paramref name="durationSec"/> (!done received).
        /// </summary>
        /// <param name="durationSec">How long will method wait for results.</param>
        /// <returns>List of !re sentences read.</returns>
        /// <remarks>If no error occurs, calling this method blocks calling thread for <paramref name="durationSec"/>.
        /// A command that ends earlier (<c>!done</c>, <c>!trap</c> or a lost connection) returns as soon as it ends.</remarks>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Comand is already running. Connection is not opened. Invalid response from API.</exception>
        /// <exception cref="TikCommandTrapException">!trap returned from API call.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        /// <exception cref="TikNoSuchCommandException">Invalid mikrotik command (syntax error). Mikrotik API message: 'no such command'</exception>
        IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences (all !re sentences) which are returned during <paramref name="durationSec"/> wait.
        /// After this period, command is automatically stopped via <see cref="CancelAndJoin()"/>.
        /// Don't throw any exception if command is aborted before <paramref name="durationSec"/>. Returns <paramref name="wasAborted"/>=true instead (usefull if incomplete result is still expected).
        /// Returns data if command ends before <paramref name="durationSec"/> (!done received).
        /// </summary>
        /// <param name="durationSec">How long will method wait for results.</param>
        /// <param name="wasAborted">If command has been terminated before <paramref name="durationSec"/>.</param>
        /// <param name="abortReason">
        /// Detail info if <paramref name="wasAborted"/> is true: the router's own <c>!trap</c> message, or the reason
        /// the connection was lost, whichever ended the command.
        /// </param>
        /// <returns>List of !re sentences read.</returns>
        /// <remarks>If no error occurs, calling this method blocks calling thread for <paramref name="durationSec"/>.
        /// A command that ends earlier (<c>!done</c>, <c>!trap</c> or a lost connection) returns as soon as it ends.</remarks>
        IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason);

        /// <summary>
        /// Executes given <see cref="CommandText"/> on router and returns all result sentences collected until the router sends <c>!done</c>.
        /// Unlike <see cref="ExecuteListWithDuration(int)"/>, this method does NOT send <c>/cancel</c> — it is intended for commands that terminate themselves
        /// (e.g. <c>/tool/traceroute count=N</c>, <c>/tool/ping count=N</c>).
        /// </summary>
        /// <param name="timeoutSec">
        /// Optional safety timeout in seconds. If the router does not send <c>!done</c> within this period the command is cancelled
        /// and <see cref="TikCommandAbortException"/> is thrown. Pass <c>null</c> to wait indefinitely.
        /// </param>
        /// <returns>List of all <c>!re</c> sentences received before <c>!done</c>.</returns>
        /// <exception cref="InvalidOperationException">Connection or command text not set. Command is already running.</exception>
        /// <exception cref="TikCommandTrapException"><c>!trap</c> returned from API call.</exception>
        /// <exception cref="TikCommandAbortException">Command did not finish within <paramref name="timeoutSec"/>.</exception>
        /// <exception cref="System.IO.IOException">Connection was closed before <c>!done</c> was received.</exception>
        IEnumerable<ITikReSentence> ExecuteListUntilDone(int? timeoutSec = null);

        /// <summary>
        /// Calls given <see cref="CommandText"/> to router. Response is returned via <paramref name="oneResponseCallback"/> callback when it is read from mikrotik (for tag, which has been dynamically assigned).
        /// REMARKS: <paramref name="oneResponseCallback"/> is called from another NON-GUI thread. If you want to show response in UI,
        /// you should use some kind of synchronization like BeginInvoke in WinForms or SynchronizationContext. You can not touch UI controls directly without it.
        /// </summary>
        /// <remarks>
        /// <b>This is not the awaitable surface, despite the name.</b> It returns <c>void</c>: it starts the
        /// command on a background thread and calls you back, and you stop it with <see cref="Cancel"/> or
        /// <see cref="CancelAndJoin()"/>. The Task-based commands are the <c>Execute*Async</c> extension
        /// methods (<c>ExecuteListAsync</c>, <c>ExecuteScalarAsync</c>, … — see <c>ITikCommandAsync</c>),
        /// each taking a <see cref="System.Threading.CancellationToken"/> and gated on
        /// <see cref="TikConnectionCapability.AsyncCommands"/>.
        /// <para>
        /// The name is kept because this member is on a public interface that callers implement; the O/R
        /// mapper's equivalents were renamed to <c>LoadWithCallback</c>/<c>LoadListenWithCallback</c>, which
        /// is the vocabulary to prefer when writing new code against either level.
        /// </para>
        /// </remarks>
        /// <param name="oneResponseCallback">Callback called periodically when response sentence is read from mikrotik.</param>
        /// <param name="errorCallback">Callback called when error occurs (command operation is than ended).</param>
        /// <param name="onDoneCallback">Callback called at the end of command run (when command is successfully finished - !done is returned). Usefull for cleanup operations at the end of command lifecycle. You can also use synchronous call <see cref="CancelAndJoin()"/> from calling thread and do cleanup after it.</param>
        /// <seealso cref="Cancel"/>
        /// <seealso cref="ITikReSentence"/>
        void ExecuteWithCallback(Action<ITikReSentence> oneResponseCallback, Action<ITikTrapSentence>? errorCallback = null, Action? onDoneCallback = null);

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
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteWithCallback"/> has been called).
        /// </summary>
        /// <seealso cref="ExecuteWithCallback"/>
        void Cancel();

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteWithCallback"/> has been called).
        /// Blocks the calling thread until a thread terminates or the specified time elapses, while continuing to perform standard COM and SendMessage pumping.
        /// </summary>
        /// <seealso cref="ExecuteWithCallback"/>
        void CancelAndJoin();

        /// <summary>
        /// Cancells already running async command (should be called on the same instance of <see cref="ITikCommand"/> on which <see cref="ExecuteWithCallback"/> has been called).
        /// Blocks the calling thread until a thread terminates or the specified time elapses, while continuing to perform standard COM and SendMessage pumping.
        /// </summary>
        /// <param name="milisecondsTimeout">Wait timeout.</param>
        /// <returns>True if loading thread ends before given timeout.</returns>
        /// <seealso cref="ExecuteWithCallback"/>
        bool CancelAndJoin(int milisecondsTimeout);
    }
}
