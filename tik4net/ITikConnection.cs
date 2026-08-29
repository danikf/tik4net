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
    /// <code>
    /// var setup = new TikConnectionSetup(TikRouterAddress.FromHost("192.168.1.1"), "user", "pass");
    /// using (ITikConnection connection = setup.Create(TikConnectionType.Api))
    /// {
    ///     // ... do work ...
    ///     // ... do query ...
    /// }   // leaving the using closes the connection — an explicit Close() is not needed
    /// </code>
    /// <para>
    /// <see cref="TikConnectionSetup"/> is the entry point: it carries the options (timeouts, TLS policy,
    /// router MAC, encoding) and opens the transport named. <see cref="ConnectionFactory"/> still works and
    /// is the shorter spelling when no option needs stating, but it has nowhere to put one.
    /// </para>
    /// </example>
    /// </summary>
    /// <remarks>
    /// <b>What a connection is, and what it is not.</b> This interface is the lifecycle (open, close,
    /// timeouts, encoding, diagnostics) and the command factory — what every transport has. The parts a
    /// transport can reasonably lack are separate interfaces it implements when it has them, each paired
    /// with the capability flag that answers the same question:
    /// <list type="bullet">
    /// <item><see cref="ITikRawSentenceConnection"/> — the low-level <c>CallCommandSync</c> dialect.</item>
    /// <item><see cref="ITikSafeModeConnection"/> — Safe Mode bound to the session
    /// (<see cref="TikConnectionCapability.SafeMode"/>); stateless REST has none.</item>
    /// <item><see cref="ITikTaggedConnection"/> — per-command tagging
    /// (<see cref="TikConnectionCapability.Tagging"/>), i.e. the binary API.</item>
    /// <item><see cref="ITikTlsConnection"/>, <see cref="ITikMacLayerConnection"/>,
    /// <see cref="ITikCancellationModeConnection"/> — the options that are not universal.</item>
    /// </list>
    /// <b>There are no compatibility extension methods for these.</b> A facet member is reached by
    /// holding the facet type — <c>((ITikSafeModeConnection)connection).SafeModeTake()</c>, or better,
    /// a variable declared as the facet — so a transport that lacks the feature is a compile error
    /// rather than a runtime one. That is the point: <c>connection.SafeModeTake()</c> on a plain
    /// <see cref="ITikConnection"/> does not build. Ask <see cref="ITikConnectionCapabilities"/>
    /// (<c>connection.Supports(...)</c>) when the transport is only known at run time.
    /// <br/>
    /// <b>What is safe to do concurrently on one connection.</b> The library states this rather than
    /// leaving it to be discovered, because the answer is not the same on every transport and the failure
    /// mode of guessing wrong is a wrong <i>answer</i> rather than an exception.
    /// <list type="bullet">
    /// <item>
    /// <b>Commands may overlap</b> on the binary API, REST and native WinBox. Each of the three correlates
    /// a reply to its caller by its own means — the API by the <c>.tag</c> word, REST because every command
    /// is a separate HTTP request, native WinBox by the M2 request id — so several callers may have a
    /// command in flight at once and each gets its own reply.
    /// <b>On the binary API this needs <see cref="ITikTaggedConnection.SendTagWithSyncCommand"/> set to
    /// <c>true</c> first</b>; see that property.
    /// </item>
    /// <item>
    /// <b>Commands queue</b> on the CLI family (Telnet, SSH, MAC-Telnet, and WinBox CLI over TCP or the MAC
    /// layer). They drive one request/reply terminal, which cannot carry two conversations, so the
    /// connection serializes whole commands internally. Calling from several threads is <i>safe</i> there;
    /// it is simply not faster.
    /// </item>
    /// <item>
    /// <b>An async monitor plus ordinary commands on one connection is safe everywhere, but only
    /// dependable on the transports that multiplex.</b> Nothing corrupts on the CLI family — a polled
    /// monitor takes the same internal turn a command does — but its worker occupies the terminal on its
    /// own cadence, and a change you make over the <i>same</i> connection may then go unreported: measured
    /// on Telnet and WinBox CLI, a listen missed the change 3 times in 4, and triggering harder made
    /// it worse rather than better. <b>Drive the change from a second connection</b> when a monitor has to
    /// observe it, or use the binary API, where the tag makes the two independent.
    /// </item>
    /// <item>
    /// <b>Opening and closing are not concurrent operations.</b> <see cref="Close"/> and
    /// <see cref="IDisposable.Dispose"/> tear the channel down under whatever is using it; a command in
    /// flight when that happens fails, and on the terminal transports it can fail as a truncated read
    /// rather than as an error. Finish or cancel outstanding work first.
    /// </item>
    /// <item>
    /// <b>Safe Mode is connection-wide state, not a per-command option</b> —
    /// <see cref="ITikSafeModeConnection.SafeModeTake"/> and its siblings must not be driven from two
    /// threads, and any command issued while it is held takes part in it.
    /// </item>
    /// <item>
    /// <b>The O/R mapper follows the connection.</b> Its change tracking is per connection and safe for
    /// distinct entities; saving <i>the same</i> entity object from two threads is not, for the ordinary
    /// reason that two threads are then editing one object.
    /// </item>
    /// </list>
    /// <para>
    /// Threads make the client faster, not the router: a sustained burst pushes a round trip from about a
    /// millisecond to twenty and worse, and that ceiling is aggregate — spreading the work over more
    /// connections reaches it sooner. Pace bulk work rather than parallelizing it
    /// (<c>Docs/findings-router-throughput-ceiling.md</c>).
    /// </para>
    /// </remarks>
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
        /// Gets or sets communication encoding (how string values are converted to bytes sent to mikrotik router).
        /// Default is UTF-8, which is what RouterOS 7 speaks.
        /// </summary>
        /// <remarks>
        /// Set it to <see cref="Encoding.ASCII"/> only to talk to a RouterOS 6.x router that predates UTF-8
        /// support. Every transport takes the same default.
        /// </remarks>
        Encoding Encoding { get; set; }

        /// <summary>
        ///     Gets or sets the amount of time a ITikConnection will wait for a send operation to complete successfully. In miliseconds.
        /// </summary>
        /// <remarks>Must be called before <see cref="Open(string, string, string)"/> call.</remarks>
        int SendTimeout { get; set; }

        /// <summary>
        ///     Gets or sets the amount of time a ITikConnection will wait to receive data once a read operation is initiated. In miliseconds.
        /// </summary>
        /// <remarks>
        /// <para>Must be called before <see cref="Open(string, string, string)"/> call.</para>
        /// <para>
        /// This bounds <b>one command waiting for its answer</b>, not the connection's right to exist: an
        /// idle connection with no command in flight is not subject to it and stays open. On the binary API
        /// the budget is per caller and runs from dispatch, so concurrent commands do not consume each
        /// other's.
        /// </para>
        /// </remarks>
        int ReceiveTimeout { get; set; }

        /// <summary>
        /// Gets or sets the amount of time <see cref="Open(string, string, string)"/> may spend reaching the
        /// router before it fails. In milliseconds, default 15 000.
        /// </summary>
        /// <remarks>
        /// <para>Must be set before the <c>Open</c> call; changing it afterwards has no effect.</para>
        /// <para>
        /// It bounds <b>getting connected</b> — the transport's connect handshake and the login exchange
        /// that follows it — and nothing after that, which is what <see cref="ReceiveTimeout"/> and
        /// <see cref="SendTimeout"/> are for. The two are deliberately separate so that an unreachable or
        /// black-holed router fails fast without having to shorten the budget of the commands that a
        /// reachable one answers. What exactly is inside the bound differs a little per transport (the
        /// TCP handshake alone, the handshake plus authentication, or one whole probe request on REST);
        /// each transport's own documentation says which.
        /// </para>
        /// </remarks>
        int ConnectTimeout { get; set; }

        /// <summary>
        /// Event called when row (word) from mikrotik is read by connection.
        /// </summary>
        /// <remarks>Could be used for debug/logging</remarks>
        /// <seealso cref="OnWriteRow"/>
        event EventHandler<TikConnectionCommCallbackEventArgs>? OnReadRow;

        /// <summary>
        /// Event called when row (word) to mikrotik is written  by connection.
        /// </summary>
        /// <remarks>Could be used for debug/logging</remarks>
        /// <seealso cref="OnReadRow"/>
        event EventHandler<TikConnectionCommCallbackEventArgs>? OnWriteRow;

        /// <summary>
        /// Opens connection to the specified mikrotik host on default port (depends on technology) and perform the logon operation.
        /// </summary>
        /// <param name="host">The host. On a MAC-layer connection (<see cref="ITikMacLayerConnection"/>) this may be empty — there the router is identified by its MAC address.</param>
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
        /// <param name="host">The host (name or ip). On a MAC-layer connection (<see cref="ITikMacLayerConnection"/>) this may be empty — there the router is identified by its MAC address.</param>
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
        /// Awaitable version, bounded by <see cref="ConnectTimeout"/> (default 15 000 ms).
        /// </summary>
        /// <param name="host">The host. On a MAC-layer connection (<see cref="ITikMacLayerConnection"/>) this may be empty — there the router is identified by its MAC address.</param>
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
        /// <param name="host">The host (name or ip). On a MAC-layer connection (<see cref="ITikMacLayerConnection"/>) this may be empty — there the router is identified by its MAC address.</param>
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
    }
}
