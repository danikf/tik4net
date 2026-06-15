using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace tik4net
{
    /// <summary>
    /// Mikrotik Connection. Main object to access mikrotik router.
    /// Implementation of interface depends on technology that 
    /// is used to access mikrotik (API, SSH, TELNET, ...).
    /// <example>
    /// using(ITikConnection connection = ConnectionFactory.OpenConnection(TikConnectionType.Api, "192.168.1.1", "user", "pass"))
    /// {
    ///     // ... do work ... 
    ///     // ... do query ...
    ///     Connection.Close();
    /// }
    /// </example>
    /// </summary>
    /// <seealso cref="ITikCommand"/>
    /// <seealso cref="TikConnectionException"/>
    /// <seealso cref="TikConnectionNotOpenException"/>
    public interface ITikConnection: IDisposable
    {
        /// <summary>
        /// If communication should be traced via <see cref="System.Diagnostics.Debug"/>. Default is <c>true</c> when Debugger is attached and <c>false</c> if not.
        /// You can read communication commands in output window (Debug-Windows-Output) when debugging.
        /// </summary>
        bool DebugEnabled { get; set; }

        /// <summary>
        /// Gets a value indicating whether is logged on (<see cref="Open(string, int, string, string)"/>).
        /// </summary>
        /// <value><c>true</c> if is logged on; otherwise, <c>false</c>.</value>
        bool IsOpened { get; }

        /// <summary>
        /// Gets or sets communication encoding (how string values are converted to bytes sent to mikrotik router). Default is ASCII.
        /// </summary>
        Encoding Encoding { get; set; }

        /// <summary>
        /// If set to true, .tag is sent also inside of sync commands (mandatory for multi thread connection usage). Default is false.
        /// </summary>
        bool SendTagWithSyncCommand { get; set; }

        /// <summary>
        ///     Gets or sets the amount of time a ITikConnection will wait for a send operation to complete successfully. In miliseconds.
        /// </summary>
        /// <remarks>Must be called before <see cref="Open(string, string, string)"/> call.</remarks>
        int SendTimeout { get; set; }

        /// <summary>
        ///     Gets or sets the amount of time a ITikConnection will wait to receive data once a read operation is initiated. In miliseconds.
        /// </summary>
        /// <remarks>Must be called before <see cref="Open(string, string, string)"/> call.</remarks>
        int ReceiveTimeout { get; set; }

        /// <summary>
        /// Event called when row (word) from mikrotik is read by connection.
        /// </summary>
        /// <remarks>Could be used for debug/logging</remarks>
        /// <seealso cref="OnWriteRow"/>
        event EventHandler<TikConnectionCommCallbackEventArgs> OnReadRow;

        /// <summary>
        /// Event called when row (word) to mikrotik is written  by connection.
        /// </summary>
        /// <remarks>Could be used for debug/logging</remarks>
        /// <seealso cref="OnReadRow"/>
        event EventHandler<TikConnectionCommCallbackEventArgs> OnWriteRow;

        /// <summary>
        /// Opens connection to the specified mikrotik host on default port (depends on technology) and perform the logon operation.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <seealso cref="Close"/>
        /// <exception cref="System.Net.Sockets.SocketException">Network connection failed.</exception>
        /// <exception cref="TikConnectionLoginException">Invalid credentials.</exception>
        /// <exception cref="TikCommandTrapException">Some other Tik4Net error.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        void Open(string host, string user, string password);

        /// <summary>
        /// Opens connection to the specified mikrotik host on specified port and perform the logon operation.
        /// </summary>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="port">TCPIP port.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <seealso cref="Close"/>
        /// <exception cref="System.Net.Sockets.SocketException">Network connection failed.</exception>
        /// <exception cref="TikConnectionLoginException">Invalid credentials.</exception>
        /// <exception cref="TikCommandTrapException">Some other Tik4Net error.</exception>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        void Open(string host, int port, string user, string password);

        /// <summary>
        /// Opens connection to the specified mikrotik host on default port (depends on technology) and perform the logon operation.
        /// Awaitable version. Default timeout is <see cref="ReceiveTimeout"/>x2 or 5s if not set.
        /// REMARKS: don't forget to use Wait overload with timeout if you use it in OpenAsync(...).Wait(timeout) way.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <seealso cref="Close"/>
        /// <seealso cref="System.Net.Sockets.SocketException">Network connection failed.</seealso>
        /// <seealso cref="TikConnectionLoginException">Invalid credentials.</seealso>
        /// <seealso cref="TikCommandTrapException">Some other Tik4Net error.</seealso>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        System.Threading.Tasks.Task OpenAsync(string host, string user, string password);

        /// <summary>
        /// Opens connection to the specified mikrotik host on specified port and perform the logon operation.
        /// Awaitable version.
        /// </summary>
        /// <param name="host">The host (name or ip).</param>
        /// <param name="port">TCPIP port.</param>
        /// <param name="user">The user.</param>
        /// <param name="password">The password.</param>
        /// <seealso cref="Close"/>
        /// <seealso cref="System.Net.Sockets.SocketException">Network connection failed.</seealso>
        /// <seealso cref="TikConnectionLoginException">Invalid credentials.</seealso>
        /// <seealso cref="TikCommandTrapException">Some other Tik4Net error.</seealso>
        /// <exception cref="TikCommandFatalException">!fatal returned from API call.</exception>
        /// <exception cref="TikCommandUnexpectedResponseException">Unexpected response from mikrotik (multiple returned rows, missing !done row etc.)</exception>
        System.Threading.Tasks.Task OpenAsync(string host, int port, string user, string password);

        /// <summary>
        /// Performs the logoff operation and closes connection. Called also via Dispose of connector.
        /// </summary>
        /// <seealso cref="Open(string, int, string, string)"/>
        void Close();

        /// <summary>
        /// Enters RouterOS <b>Safe Mode</b> on this connection (mirrors <c>/safe-mode/take</c>, the WebFig
        /// "Safe Mode" button, or the terminal <c>Ctrl+X</c>). While safe mode is held, every configuration
        /// change is recorded; if the owning connection drops (network failure, process crash, <see cref="Close"/>
        /// without a preceding <see cref="SafeModeRelease"/>) RouterOS automatically <b>rolls back</b> all those
        /// changes. Call <see cref="SafeModeRelease"/> to keep the changes, or <see cref="SafeModeUnroll"/> to
        /// discard them immediately while staying connected.
        /// <para>
        /// Safe mode is bound to the underlying session, so it is only meaningful on a persistent,
        /// session-oriented transport: binary API / API-SSL, any CLI terminal (Telnet, MAC-Telnet, WinBox CLI)
        /// and native WinBox M2. Stateless REST throws <see cref="NotSupportedException"/>. Check
        /// <see cref="TikConnectionCapability.SafeMode"/> via <c>connection.Supports(...)</c> if unsure.
        /// </para>
        /// <para>The binary-API / native-WinBox / REST paths use the scriptable <c>/safe-mode</c> mechanism
        /// (RouterOS 7.18+); the CLI terminals use the <c>Ctrl+X</c>/<c>Ctrl+D</c> control keys, which also work
        /// on older RouterOS.</para>
        /// </summary>
        /// <exception cref="NotSupportedException">The transport cannot bind safe mode to the connection.</exception>
        /// <exception cref="TikConnectionNotOpenException">Connection is not open.</exception>
        /// <exception cref="TikCommandException">RouterOS refused to take safe mode (e.g. already held by another session).</exception>
        /// <seealso cref="SafeModeRelease"/>
        /// <seealso cref="SafeModeUnroll"/>
        /// <seealso cref="SafeModeGet"/>
        void SafeModeTake();

        /// <summary>
        /// Commits the changes made since <see cref="SafeModeTake"/> and leaves Safe Mode (mirrors
        /// <c>/safe-mode/release</c> or a second terminal <c>Ctrl+X</c>). After this call the changes are
        /// permanent and a later disconnect no longer rolls them back. Closing the connection (or losing it)
        /// <b>before</b> calling this method discards every change made since <see cref="SafeModeTake"/> — that
        /// is the automatic rollback safe mode exists for. No-op when safe mode is not currently held.
        /// </summary>
        /// <exception cref="NotSupportedException">The transport cannot bind safe mode to the connection.</exception>
        /// <exception cref="TikConnectionNotOpenException">Connection is not open.</exception>
        /// <exception cref="TikCommandException">RouterOS reported an error releasing safe mode.</exception>
        /// <seealso cref="SafeModeTake"/>
        void SafeModeRelease();

        /// <summary>
        /// Discards every change made since <see cref="SafeModeTake"/> <b>now</b>, without disconnecting, and
        /// leaves Safe Mode (mirrors <c>/safe-mode/unroll</c> or the terminal <c>Ctrl+D</c>). This is the explicit
        /// rollback — the same revert RouterOS performs automatically on an uncommitted disconnect, but on demand
        /// while the connection stays open and usable. No-op when safe mode is not currently held.
        /// <para>Not available on the native WinBox transport (WebFig exposes only take/release); there, drop the
        /// connection without releasing to roll back.</para>
        /// </summary>
        /// <exception cref="NotSupportedException">The transport cannot roll back safe mode in place.</exception>
        /// <exception cref="TikConnectionNotOpenException">Connection is not open.</exception>
        /// <exception cref="TikCommandException">RouterOS reported an error rolling back safe mode.</exception>
        /// <seealso cref="SafeModeTake"/>
        /// <seealso cref="SafeModeRelease"/>
        void SafeModeUnroll();

        /// <summary>
        /// Returns whether this connection currently holds Safe Mode (mirrors the intent of
        /// <c>/safe-mode/get</c>). The state is tracked client-side per connection: it becomes <c>true</c> after
        /// <see cref="SafeModeTake"/> and <c>false</c> after <see cref="SafeModeRelease"/> /
        /// <see cref="SafeModeUnroll"/>. Useful in a <c>finally</c> block to decide whether to commit or let the
        /// disconnect roll back.
        /// </summary>
        /// <returns><c>true</c> when safe mode is held by this connection; otherwise <c>false</c>.</returns>
        /// <seealso cref="SafeModeTake"/>
        bool SafeModeGet();

        /// <summary>
        /// Factory method - creates empty command specific for connection type with assiged <see cref="ITikCommand.Connection"/>.
        /// </summary>
        /// <returns>Commend with assiged <see cref="ITikCommand.Connection"/>.</returns>
        ITikCommand CreateCommand();

        /// <summary>
        /// Factory method - creates empty command specific for connection type with assiged <see cref="ITikCommand.Connection"/>.
        /// </summary>
        /// <param name="defaultParameterFormat">How will be parameter formated in mikrotik command - default value for command (could be overriden per parameter).</param>
        /// <returns>Commend with assiged <see cref="ITikCommand.Connection"/>.</returns>
        ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat);

        /// <summary>
        /// Factory method - creates command specific for connection type with assiged <see cref="ITikCommand.Connection"/>.
        /// Setups <see cref="ITikCommand.CommandText"/> and <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="commandText">Command text in mikrotik API format</param>
        /// <param name="parameters">Parameters to be added to newly created command.</param>
        /// <returns>Commend with assiged <see cref="ITikCommand.Connection"/>.</returns>
        /// <seealso cref="CreateParameter(string, string)"/>
        ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters);

        /// <summary>
        /// Factory method - creates command specific for connection type with assiged <see cref="ITikCommand.Connection"/>.
        /// Setups <see cref="ITikCommand.CommandText"/> and <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="commandText">Command text in mikrotik API format</param>
        /// <param name="defaultParameterFormat">How will be parameter formated in mikrotik command - default value for command (could be overriden per parameter).</param>
        /// <param name="parameters">Parameters to be added to newly created command.</param>
        /// <returns>Commend with assiged <see cref="ITikCommand.Connection"/>.</returns>
        ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters);        

        /// <summary>
        /// Factory method - creates command specific for connection type with assiged <see cref="ITikCommand.Connection"/>.
        /// Setups <see cref="ITikCommand.CommandText"/> and <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="commandText">Command text in mikrotik API format</param>
        /// <param name="parameterNamesAndValues">Name and value of parameters for command. (name, value, name2, value2, ..., name9, value9, ...)</param>
        /// <returns>Command with assiged <see cref="ITikCommand.Connection"/>.</returns>
        ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues);

        /// <summary>
        /// Factory method - creates command specific for connection type with assiged <see cref="ITikCommand.Connection"/>.
        /// Setups <see cref="ITikCommand.CommandText"/> and <see cref="ITikCommand.Parameters"/>.
        /// </summary>
        /// <param name="commandText">Command text in mikrotik API format</param>
        /// <param name="defaultParameterFormat">How will be parameter formated in mikrotik command - default value for command (could be overriden per parameter).</param>
        /// <param name="parameterNamesAndValues">Name and value of parameters for command. (name, value, name2, value2, ..., name9, value9, ...)</param>
        /// <returns>Command with assiged <see cref="ITikCommand.Connection"/>.</returns>
        ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues);        

        /// <summary>
        /// Factory method - creates parameters instance specific for connection and command type.
        /// </summary>
        /// <param name="name">Name of the parameter (without '=')</param>
        /// <param name="value">Value of the parameter</param>
        /// <returns>Created parameter with name and value.</returns>
        /// <seealso cref="ITikCommand.Parameters"/>
        ITikCommandParameter CreateParameter(string name, string value);

        /// <summary>
        /// Factory method - creates parameters instance specific for connection and command type.
        /// </summary>
        /// <param name="name">Name of the parameter (without '=')</param>
        /// <param name="value">Value of the parameter</param>
        /// <param name="parameterFormat">How will be parameter formated in mikrotik command.</param>
        /// <returns>Created parameter with name and value.</returns>
        /// <seealso cref="ITikCommand.Parameters"/>
        ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat parameterFormat);

        /// <summary>
        /// Calls command to mikrotik (in connection specific format) and waits for response. Command is called without .tag. If you want to use it, just add it as usual parameter (.tag=1234) as last row.
        /// </summary>
        /// <param name="commandRows">Rows of one command to be send to mikrotik router (in conection specific format).</param>
        /// <returns>List of returned sentences.</returns>
        /// <remarks>This is extremly low-level API and should be used only if there is no other way (for example <seealso cref="ITikCommand"/>).</remarks>
        /// <exception cref="TikConnectionNotOpenException" />
        /// <seealso cref="ITikReSentence"/>
        /// <seealso cref="ITikDoneSentence"/>
        /// <seealso cref="ITikTrapSentence"/>
        /// <seealso cref="ITikCommand.ExecuteNonQuery"/>
        /// <seealso cref="ITikCommand.ExecuteScalar()"/>
        /// <seealso cref="ITikCommand.ExecuteSingleRow()"/>
        /// <seealso cref="ITikCommand.ExecuteList()"/>
        IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows);

        /// <summary>
        /// Calls command to mikrotik (in connection specific format) and waits for response. Command is called without .tag. If you want to use it, just add it as usual parameter (.tag=1234) as last row.
        /// </summary>
        /// <param name="commandRows">Rows of one command to be send to mikrotik router (in conection specific format).</param>
        /// <returns>List of returned sentences.</returns>
        /// <remarks>This is extremly low-level API and should be used only if there is no other way (for example <seealso cref="ITikCommand"/>).</remarks>
        /// <exception cref="TikConnectionNotOpenException" />
        /// <seealso cref="ITikReSentence"/>
        /// <seealso cref="ITikDoneSentence"/>
        /// <seealso cref="ITikTrapSentence"/>
        /// <seealso cref="ITikCommand.ExecuteNonQuery"/>
        /// <seealso cref="ITikCommand.ExecuteScalar()"/>
        /// <seealso cref="ITikCommand.ExecuteSingleRow"/>
        /// <seealso cref="ITikCommand.ExecuteList()"/>
        IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows);

        /// <summary>
        /// Calls command to mikrotik (in connection specific format). Response is returned via <paramref name="oneResponseCallback"/> callback when it is read from mikrotik (for given <paramref name="tag"/>).
        /// REMARKS: <paramref name="oneResponseCallback"/> is called from another NON-GUI thread. If you want to show response in UI, 
        /// you should use some kind of synchronization like BeginInvoke in WinForms. You can not touch UI controls directly without it.
        /// </summary>
        /// <exception cref="TikConnectionNotOpenException" />
        /// <param name="commandRows">Rows of one command to be send to mikrotik router (in conection specific format).</param>
        /// <param name="tag">Tag that allows to perform cancel operation. Should be unique!</param>
        /// <param name="oneResponseCallback">Callback called periodically when response sentence is read from mikrotik.</param>
        /// <returns>Loading thread.</returns>
        /// <remarks>This is extremly low-level API and should be used only if there is no other way (for example <see cref="ITikCommand.ExecuteAsync"/>).</remarks>
        /// <seealso cref="ITikReSentence"/>
        /// <seealso cref="ITikDoneSentence"/>
        /// <seealso cref="ITikTrapSentence"/>
        /// <seealso cref="ITikCommand.ExecuteAsync"/>
        Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, Action<ITikSentence> oneResponseCallback);
    }
}
