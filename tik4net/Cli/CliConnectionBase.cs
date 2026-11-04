using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// Transport-agnostic base class for RouterOS CLI-based connections (Telnet, SSH, MACTelnet, WinBox CLI).
    /// Builds the CLI command text, sends it through the transport, and parses the textual response back
    /// into the shared command/sentence model. All the transport-neutral plumbing
    /// (<see cref="ITikConnection"/> surface, command factory, low-level dispatch, diagnostics) lives in
    /// <see cref="TikCommandConnectionBase"/>.
    ///
    /// The class is derived only inside tik4net and its friend assemblies (<c>tik4net.ssh</c>, the tests): its
    /// constructor and its subclass members are <c>private protected</c>. A transport written elsewhere derives
    /// from <see cref="TikCommandConnectionBase"/>. Each CLI transport implements the <c>Open</c> overloads by
    /// building, per open attempt, a terminal (<c>ICliTerminal</c>: one of the four terminal clients, or a
    /// <c>ReconnectingCliTerminal</c> around one) with its login, and calling <c>OpenTerminal</c> /
    /// <c>OpenTerminalAsync</c>; it names itself through <c>TransportName</c> (shown in the "not open" diagnostic).
    /// <see cref="TikConnectionBase.Close"/> and the not-open guards are provided by this base.
    ///
    /// The text a terminal returns for a command must already have ANSI escape sequences stripped
    /// (<see cref="VtStripper.StripAnsi"/>) and any terminal echo / prompt trimmed by the transport — the
    /// core layer only sees data lines.
    /// </summary>
    /// <remarks>
    /// <b>Thread safety.</b> A CLI connection is safe to use from several threads, but commands do not
    /// overlap: a terminal carries one conversation, so every command is taken through
    /// <see cref="TikCommandConnectionBase"/>'s command semaphore and extra callers queue behind the one
    /// holding it. That is by design and not a limitation waiting to be lifted — a gate that failed to
    /// hold here would not raise an error, it would interleave two commands' bytes and hand each caller a
    /// plausible answer built from the other's. Open a second connection, or use a transport that
    /// multiplexes (the binary API, REST, native WinBox), when commands have to run at the same time.
    /// <para>
    /// <b>A monitor is the one case worth planning around.</b> Its poll worker takes the same turn a
    /// command does, so nothing corrupts and CRUD from another thread still runs — it queues. What is not
    /// dependable is the other direction: the worker holds the terminal on its own cadence, so a change
    /// made over the <i>same</i> connection can fall between two polls and never be reported. Measured on
    /// Telnet and WinBox CLI, a listen missed such a change 3 times in 4, and polling harder made it worse
    /// rather than better. Drive the change from a second connection when a monitor has to observe it, or
    /// use the binary API, where the tag makes the two independent.
    /// </para>
    /// <para>The rest of the contract — <c>Close</c> not waiting for a running command, Safe Mode as
    /// connection-wide state, and the router's shared throughput ceiling — is on
    /// <see cref="ITikConnection"/>.</para>
    /// </remarks>
    public abstract class CliConnectionBase : TikCommandConnectionBase, ITikCliConnection,
        ITikMonitorTransport, IPollingMonitorHost, ITikCliPagedReadConnection,
        ITikCliFieldSeparatorConnection, ITikMenuSchemaConnection, ICliCompletionReaction, ICliHelpKey, ITikCatalogCacheConnection,
        ITikRomonConnection, ICliCommandHost
    {
        /// <inheritdoc/>
        public string? CliFieldSeparator
        {
            get => _cliFieldSeparator;
            set
            {
                CliCommandBuilder.ValidateFieldSeparator(value);
                _cliFieldSeparator = value;
            }
        }

        private string? _cliFieldSeparator = TikConnectionSetup.DefaultCliFieldSeparator;

        // What this connection has learnt about its router, and the build's store (CliRouterKnowledge). Replaced on
        // every open: an object reopened against another router would otherwise send it what only the first one took.
        private CliRouterKnowledge _knowledge;

        // Tab completion and the help key (CliCompletion), replaced on every open with the settle window it learns.
        private CliCompletion _completion;

        // The flag fields on RouterOS before 7.20 (CliFlagSupplier), for the connection's lifetime.
        private readonly CliFlagSupplier _flags;

        // The counted and windowed print reads (CliPagedReader).
        private readonly CliPagedReader _pagedReader;

        // What a read method runs (CliPrintDriver).
        private readonly CliPrintDriver _printDriver;

        // The monitor loops (CliMonitorDriver); the connection stays the polling engine's host.
        private readonly CliMonitorDriver _monitorDriver;

        // ── What the collaborators reach the connection through (ICliCommandHost) ──

        string? ICliCommandHost.CatalogCachePath => CatalogCachePath;
        string? ICliCommandHost.CliFieldSeparator => CliFieldSeparator;
        int ICliCommandHost.CountedReadDepth { get => _countedReadDepth; set => _countedReadDepth = value; }
        string ICliCommandHost.Execute(string cliText) => ExecuteCliCommand(cliText);
        Task<string> ICliCommandHost.ExecuteAsync(string cliText, CancellationToken ct) => ExecuteCliCommandAsync(cliText, ct);
        Task<string> ICliCommandHost.ExecuteReadAsync(string cliText, CancellationToken ct)
            => ExecuteCliCommandCoreAsync(cliText, ct, mayMarkEnd: true, interruptible: true);
        TikMenuSchema ICliCommandHost.DescribeMenuLive(string path) => DescribeMenuLive(path);
        TikMenuSchemaCache ICliCommandHost.MenuSchemas => MenuSchemas;
        ICliTerminal? ICliCommandHost.Terminal => _terminal;
        void ICliCommandHost.EnsureOpened() => EnsureOpened();
        string ICliCommandHost.TransportName => TransportName;
        Encoding ICliCommandHost.Encoding => Encoding;
        SemaphoreSlim ICliCommandHost.CommandLock => CommandLock;
        void ICliCommandHost.FireWriteRow(string row) => FireWriteRow(row);
        void ICliCommandHost.FireReadRow(string row) => FireReadRow(row);
        Task<string> ICliCommandHost.SendRawAndReadAsync(byte[] raw, CancellationToken ct) => SendRawAndReadAsync(raw, ct);
        CliRouterKnowledge ICliCommandHost.Knowledge => _knowledge;
        ITikCommand ICliCommandHost.CommandFor(TikCommandDescriptor descriptor) => CommandFor(descriptor);
        Task<IList<TikRecordSentence>> ICliCommandHost.RunPrintQueryAsync(TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression, CancellationToken cancellationToken)
            => _printDriver.RunPrintQueryAsync(descriptor, wantJson, buildExpression, cancellationToken);
        int ICliCommandHost.CliReadPageSize => CliReadPageSize;
        int ICliCommandHost.WindowedReadDepth { get => _windowedReadDepth; set => _windowedReadDepth = value; }
        IList<TikRecordSentence> ICliCommandHost.ParseRecords(string output, TikCommandDescriptor descriptor)
            => _printDriver.ParseRecords(output, descriptor);
        void ICliCommandHost.ThrowIfSyntaxError(string? output, TikCommandDescriptor descriptor)
            => CliErrorParser.ThrowIfSyntaxError(output, CommandFor(descriptor));
        Task<IList<TikRecordSentence>> ICliCommandHost.RunMonitorSnapshotAsync(TikCommandDescriptor descriptor,
            string modifier, bool includeFilters, CancellationToken cancellationToken)
            => _printDriver.RunMonitorSnapshotAsync(descriptor, modifier, includeFilters, cancellationToken);
        bool ICliCommandHost.SupportsStreamingRead => SupportsStreamingRead;
        string ICliCommandHost.ExecuteStreaming(string cliText, Action<string> onLine) => ExecuteCliCommandStreaming(cliText, onLine);

        /// <inheritdoc/>
        public int CliReadPageSize
        {
            get => _cliReadPageSize;
            set
            {
                if (value < 0)
                    throw new System.ArgumentOutOfRangeException(nameof(value),
                        "CliReadPageSize is a row count; use 0 to read each table in a single command.");
                _cliReadPageSize = value;
            }
        }

        // Set by the MAC-layer terminals in their constructors (see ITikCliPagedReadConnection), and by
        // TikConnectionSetup only when the caller asked for a value.
        private int _cliReadPageSize;

        // ── Raw sentences — native CLI text (ITikRawSentenceConnection) ───────

        /// <inheritdoc/>
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
            => CallCommandSync((IEnumerable<string>)commandRows);

        /// <summary>
        /// Runs a command written in this transport's own language — <b>RouterOS CLI text</b> — and returns
        /// the response as sentences. Nothing is translated on the way out.
        /// </summary>
        /// <param name="commandRows">
        /// The command, in CLI syntax. Several rows are joined with a single space, so the command may be
        /// written in one piece or split the way the API's sentence rows are:
        /// <c>CallCommandSync("/interface print as-value")</c> and
        /// <c>CallCommandSync("/interface print", "as-value", "where name=ether1")</c> send the same line.
        /// </param>
        /// <returns>
        /// The parsed response. A command whose output is in <c>as-value</c> form yields one
        /// <see cref="ITikReSentence"/> per record followed by an <see cref="ITikDoneSentence"/>; any other
        /// output is returned whole as the <c>ret</c> word of a single <see cref="ITikDoneSentence"/>, because
        /// arbitrary terminal text has no record structure to parse.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This is the low-level counterpart of <c>CreateRawCommand</c> and behaves the same way: the line is
        /// sent as typed, with no path rewriting, no <c>where</c> building and no <c>proplist</c>. That is the
        /// point of it — everything the O/R mapper cannot express is reachable here, including
        /// <c>:put</c>/<c>:foreach</c> scripting, <c>/export</c>, and menus tik4net has no entity for.
        /// </para>
        /// <para>
        /// <b>To get records back you must ask the router for them.</b> RouterOS prints nothing for a bare
        /// <c>print as-value</c> typed at a terminal — as-value output is materialised only in script context
        /// — so wrap a read yourself: <c>:put [/interface print as-value]</c>. Unwrapped output is still
        /// returned, just as text rather than as rows.
        /// </para>
        /// <para>
        /// Errors the router reports in its output are raised as
        /// <see cref="TikCommandTrapException"/>, the same as on any other CLI command.
        /// </para>
        /// </remarks>
        /// <exception cref="TikConnectionNotOpenException">The connection is not open.</exception>
        /// <exception cref="TikCommandTrapException">The router reported an error.</exception>
        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
            => CallCommandAsync(commandRows, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        public Task<IList<ITikSentence>> CallCommandAsync(string[] commandRows,
            CancellationToken cancellationToken = default(CancellationToken))
            => CallCommandAsync((IEnumerable<string>)commandRows, cancellationToken);

        /// <inheritdoc/>
        /// <remarks>
        /// This is the implementation and <see cref="CallCommandSync(IEnumerable{string})"/> blocks on it,
        /// the same way round as the rest of this class: one code path, so the two cannot drift apart in
        /// what they send or how they read the answer.
        /// </remarks>
        public async Task<IList<ITikSentence>> CallCommandAsync(IEnumerable<string> commandRows,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.ArgumentNotNull(commandRows, nameof(commandRows));
            EnsureOpened();

            string cliText = string.Join(" ",
                commandRows.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray());
            if (cliText.Length == 0)
                throw new ArgumentException("commandRows must contain a CLI command.", nameof(commandRows));

            // Raw means native syntax, sent as given: no end marker is appended to it.
            string output = await ExecuteCliCommandCoreAsync(cliText, cancellationToken, mayMarkEnd: false).ConfigureAwait(false);

            // Raw mode is handed a finished line rather than building one, but the verb is IN that line —
            // so a plain '/path set …' still gets the positional check, and only a line the reader cannot
            // vouch for (scripting, ':put [ … ]', chaining) falls back to phrase matching alone. "No
            // output" remains a perfectly good answer either way and must not be read as failure.
            CliErrorParser.ThrowIfError(output,
                CommandFor(cliText),
                silentOnSuccess: CliErrorParser.TryGetRawSilentVerb(cliText, out _));

            var result = new List<ITikSentence>();
            var records = CliOutputParser.ParseAsValue(output);
            if (records.Count > 0)
            {
                foreach (var record in records)
                    result.Add(record);
                result.Add(new TikDoneSentenceResult());
            }
            else
            {
                // Not as-value output — hand back the text rather than inventing rows from it.
                string text = output.Trim();
                result.Add(new TikDoneSentenceResult(text.Length > 0 ? text : null));
            }
            return result;
        }

        // ── Capabilities ──────────────────────────────────────────────────────

        /// <summary>
        /// CLI transports support CRUD and (via polling) Listen/async: <c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>/
        /// <c>LoadListenWithCallback</c> are emulated by re-issuing a one-shot snapshot/print on a background timer
        /// (see <see cref="ITikMonitorTransport"/> below). Streaming (<c>ExecuteListWithDuration</c>) is NOT
        /// reported — use the binary API for that.
        /// <para>
        /// <see cref="TikConnectionCapability.AsyncCommands"/> is reported because the terminal clients are
        /// async to the socket and the <c>Execute*Async</c> surface awaits them — nothing is pushed onto a
        /// thread-pool thread to look asynchronous. <see cref="TikConnectionCapability.CancelInFlight"/> is
        /// <b>not</b>, and never will be: a terminal answers with an unframed byte stream, so a read that is
        /// abandoned mid-command leaves output for the next command to misread. See
        /// <see cref="TikCancellationMode"/> for what a token does here instead.
        /// </para>
        /// <para>
        /// <see cref="TikConnectionCapability.RawCommand"/> is reported because a terminal <i>has</i> a
        /// native dialect to be raw in: <see cref="CallCommandSync(string[])"/> sends RouterOS CLI text
        /// verbatim. It is not reported by REST or native WinBox, which have no command language of their own
        /// for a caller to write.
        /// </para>
        /// </summary>
        public override TikConnectionCapability Capabilities
            => TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode
             | TikConnectionCapability.RawCommand | TikConnectionCapability.AsyncCommands
             | TikConnectionCapability.MenuSchema;

        bool ITikMenuSchemaConnection.ValidateWrites { get => ValidateWrites; set => ValidateWrites = value; }

        /// <summary>
        /// <c>/console/inspect</c> where the router has it (RouterOS 7), Tab completion where it does not (RouterOS 6):
        /// whether it does is asked by the first menu described and remembered for the open.
        /// </summary>
        TikMenuSchema ITikMenuSchemaConnection.DescribeMenu(string path, string? winboxLabels)
            => MenuSchemas.GetOrAdd(path, p =>
            {
                var store = _knowledge.SchemaStore();
                if (store == null)
                    return DescribeMenuLive(p);
                var source = store.SourceOf(p);
                return source != null ? _knowledge.CachedSchema(store, p, source.Value) : CliRouterKnowledge.Recording(store, p, DescribeMenuLive(p));
            });

        /// <inheritdoc/>
        /// <remarks>On a terminal transport it holds the menu grammar <c>DescribeMenu</c> answers, per RouterOS build.
        /// The build is asked the first time a menu is described (one command); it is read when that happens, so a
        /// change before then takes effect.</remarks>
        public string? CatalogCachePath { get; set; } = DefaultCatalogCachePath;

        /// <summary>
        /// What a new CLI connection starts with in <see cref="CatalogCachePath"/>: <c>%TEMP%/tik4net</c>, the WinBox
        /// <c>.jg</c> cache's directory. The router-free unit tests clear it once for the whole assembly, so their fake
        /// routers neither answer the build question nor touch the machine's cache.
        /// </summary>
        internal static string? DefaultCatalogCachePath = TikConnectionSetup.DefaultCatalogCachePath;

        /// <summary>
        /// <c>/console/inspect</c> where the router has it (RouterOS 7), Tab completion where it does not (RouterOS 6):
        /// whether it does is asked by the first menu described and remembered for the open.
        /// </summary>
        private TikMenuSchema DescribeMenuLive(string p)
        {
            if (_knowledge.Features.ConsoleInspect != false)
            {
                try
                {
                    var schema = ConsoleInspectSchemaReader.Read(this, p);
                    _knowledge.Features.ConsoleInspect = true;
                    _knowledge.StoreFeature(CliRouterKnowledge.FeatureConsoleInspect, true);
                    return schema;
                }
                catch (TikNoSuchCommandException ex) when (_knowledge.Features.ConsoleInspect == null
                    && ex.Message.IndexOf("bad command name inspect", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _knowledge.Features.ConsoleInspect = false;
                    _knowledge.StoreFeature(CliRouterKnowledge.FeatureConsoleInspect, false);
                }
            }
            // No inspect, and no Tab to ask with either: the router cannot be asked, which a caller treats as a
            // refusal (the filter check then sends the read as before). A closed connection says that instead, as the
            // Tab read would.
            EnsureOpened();
            if (_terminal?.SupportsQuietRead != true)
                throw new TikNoSuchCommandException(CommandFor(p),
                    new TikTrapSentenceResult("no /console/inspect and no Tab completion on this connection"));
            return CliCompletionSchemaReader.Read(this, p);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Set it to <see cref="TikCancellationMode.AbandonAndClose"/> (usually via
        /// <see cref="TikConnectionSetup.CancellationMode"/>) to trade the connection for a prompt return.
        /// A token cancelled <i>before</i> dispatch always throws without writing anything, in either mode.
        /// </remarks>
        public TikCancellationMode CancellationMode { get; set; } = TikCancellationMode.Cooperative;

        // ── Transport driver — subclass contract ──────────────────────────────

        // The open console (ICliTerminal), set by a successful open and taken away by Close; every reader below goes
        // through it. A leaf builds a new terminal and its login per open attempt (CliTerminalOpen) and opens through
        // OpenTerminal.

        /// <summary>Derived inside tik4net and its friend assemblies only; see the class remarks.</summary>
        /// <param name="connectionType">The transport this is; <c>null</c> only for a test double.</param>
        private protected CliConnectionBase(TikConnectionType? connectionType = null) : base(connectionType)
        {
            _knowledge = new CliRouterKnowledge(this);
            _completion = new CliCompletion(this);
            _flags = new CliFlagSupplier(this);
            _pagedReader = new CliPagedReader(this);
            _monitorDriver = new CliMonitorDriver(this);
            _printDriver = new CliPrintDriver(this, _flags, _pagedReader, _monitorDriver);
        }

        private ICliTerminal? _terminal;

        /// <summary>The timeouts a leaf hands its terminal client; the receive one is read per use.</summary>
        private protected CliClientTimeouts ClientTimeouts => new CliClientTimeouts(() => ReceiveTimeoutMs, ConnectTimeoutMs, SendTimeoutMs);

        /// <summary>
        /// When set before <c>Open</c>, the host/user/password given to <c>Open</c> are the AGENT's, and the
        /// transport continues from the agent into this target over RoMON before the connection counts as open.
        /// Set by <see cref="TikConnectionSetup.ApplyTo"/> through <see cref="ITikRomonConnection"/>, which this class
        /// implements for all five CLI transports: Telnet, SSH and MAC-Telnet relay through <c>/tool romon ssh</c>, the
        /// two WinBox CLI transports through the WinBox relay.
        /// </summary>
        internal RomonRelayTarget? RomonTarget { get; set; }

        /// <summary>Set once the relay has reached the target — see <see cref="ITikRomonConnection"/>.</summary>
        internal TikRomonConnectionInfo? RomonConnectionInfo { get; private set; }

        /// <summary>
        /// Records that the relay reached <see cref="RomonTarget"/>. Called by a relaying transport's login, after
        /// <see cref="RouterOsCliLogin.RomonSshLoginAsync"/> returned the agent's own RoMON id, or by a WinBox CLI
        /// transport once the WinBox relay (<see cref="Winbox.WinboxRomonChannel"/>) has opened its link.
        /// </summary>
        internal void RomonEntered(string host, string user, string agentRomonId, TikRomonRelay relay = TikRomonRelay.Ssh)
            => RomonConnectionInfo = TikRomonConnectionInfo.Relayed(RomonTarget!, host, ConnectionType!.Value, user,
                                                                    agentRomonId, relay);

        RomonRelayTarget? ITikRomonConnection.RomonTarget { get => RomonTarget; set => RomonTarget = value; }

        TikConnectionType ITikRomonConnection.RomonAgentConnectionType => ConnectionType!.Value;

        TikRomonConnectionInfo? ITikRomonConnection.RomonConnectionInfo => RomonConnectionInfo;

        /// <summary>
        /// The RoMON agent as a message names it: its host, or — a MAC-layer agent given by MAC alone has none — its
        /// <paramref name="routerMac"/>. Shared by MAC-Telnet and the WinBox relay (<see cref="Winbox.WinboxRomonChannel"/>).
        /// </summary>
        internal static string AgentName(string? host, string? routerMac)
            => !string.IsNullOrEmpty(host) ? host! : !string.IsNullOrEmpty(routerMac) ? routerMac! : "(unnamed)";

        /// <summary>
        /// A failed login to the RoMON agent, said to be the agent's: otherwise the message reads as if the
        /// target had refused, and the target's credentials are the first thing a reader would doubt. Only for
        /// an agent that answered and refused — an agent port that refuses the connection is a
        /// <see cref="SocketException"/>, as on a direct connection.
        /// </summary>
        internal static TikConnectionLoginException AgentLoginFailed(string host, Exception ex)
            => new TikConnectionLoginException(new Exception(
                "RoMON agent " + host + " refused the login — " +
                (ex is TikConnectionLoginException ? ex.InnerException?.Message ?? ex.Message : ex.Message), ex));

        // Direct: just log in. Through a RoMON agent the login is the agent's — a refusal is reported as the agent's —
        // and the terminal is then continued into the target from the agent's shell.
        private protected async Task LoginOrRelayAsync(string host, string agentName, string user,
            Func<CancellationToken, Task> login, Func<RomonRelayTarget, CancellationToken, Task<string>> enterRomon,
            CancellationToken ct)
        {
            var target = RomonTarget;
            if (target == null)
            {
                await login(ct).ConfigureAwait(false);
                return;
            }
            try { await login(ct).ConfigureAwait(false); }
            catch (TikConnectionLoginException ex) { throw AgentLoginFailed(agentName, ex); }
            string agentRomonId = await enterRomon(target, ct).ConfigureAwait(false);
            RomonEntered(host, user, agentRomonId);
        }

        /// <summary>What a leaf builds per open attempt: a fresh terminal and the login that makes it usable.</summary>
        internal readonly struct CliTerminalOpen
        {
            internal CliTerminalOpen(ICliTerminal terminal, Func<CancellationToken, Task> login)
            {
                Terminal = terminal;
                Login = login;
            }

            internal ICliTerminal Terminal { get; }
            internal Func<CancellationToken, Task> Login { get; }
        }

        /// <summary>
        /// Shared open: builds a terminal through <paramref name="attempt"/>, runs its login under the standard guard
        /// (what <see cref="TikOpenGuard.PassesThrough"/> names is rethrown as-is; any other exception is wrapped in a
        /// login exception; the half-open terminal is ended either way), then keeps the terminal and marks the
        /// connection opened.
        /// </summary>
        /// <param name="attempt">Builds a new terminal and its login; called once per attempt.</param>
        /// <param name="retryRefusedLogin">Retry a transient refusal (<see cref="TikOpenRetry"/>): the
        /// EC-SRP5 logins of MAC-Telnet and WinBox.</param>
        private protected void OpenTerminal(Func<CliTerminalOpen> attempt, bool retryRefusedLogin)
            => OpenTerminalAsync(attempt, retryRefusedLogin, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>Async counterpart of <see cref="OpenTerminal"/>.</summary>
        private protected Task OpenTerminalAsync(Func<CliTerminalOpen> attempt, bool retryRefusedLogin, CancellationToken ct)
            // A refused handshake leaves the carrier unusable, so every attempt builds a new terminal (TikOpenRetry).
            // The cancel check in OpenOnceAsync is also what ends the retry loop on a cancel: TikOpenRetry retries
            // only a refusal.
            => retryRefusedLogin
                ? TikOpenRetry.RunAsync(() => OpenOnceAsync(attempt, ct), TikOpenRetry.IsTransientRefusal)
                : OpenOnceAsync(attempt, ct);

        private async Task OpenOnceAsync(Func<CliTerminalOpen> attempt, CancellationToken cancellationToken)
        {
            // Before the login, not inside it: the login opens the socket synchronously and only then awaits, so an
            // already-cancelled token would otherwise still cost a TCP connect.
            cancellationToken.ThrowIfCancellationRequested();
            RomonConnectionInfo = null;
            _knowledge = new CliRouterKnowledge(this);
            _completion = new CliCompletion(this);
            var open = attempt();
            try
            {
                // The login reaches the router-facing waits with the caller's token — the prompt reads of
                // RouterOsCliLogin — which is the part of an open that actually blocks. The socket connect inside it
                // is synchronous and stays bounded by ConnectTimeout.
                await open.Login(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (TikOpenGuard.PassesThrough(ex))
            {
                open.Terminal.End(SafeModeHeld);
                throw;
            }
            catch (Exception ex)
            {
                open.Terminal.End(SafeModeHeld);
                throw new TikConnectionLoginException(ex);
            }
            // A login that never entered the target leaves the terminal on the agent, where every command would run.
            if (RomonTarget != null && RomonConnectionInfo == null)
            {
                open.Terminal.End(SafeModeHeld);
                throw new NotSupportedException(TransportName + " logged in to the RoMON agent without entering the target ("
                    + RomonTarget + "): every command would have run on the agent.");
            }
            _terminal = open.Terminal;
            SetOpened();
        }

        /// <inheritdoc/>
        public override void Close()
        {
            // SetClosed FIRST, as WinboxNativeConnection.Close does and for the same reason: a command
            // running on another thread is about to lose its socket, and IsOpened is how it tells "the user
            // closed this" from "the transport broke", so the failure must not arrive while the connection still looks open.
            SetClosed();
            _knowledge.Store?.Flush();   // never throws, bounded by the store's lock timeout
            // A session holding Safe Mode answers the /quit question (see the clients).
            Interlocked.Exchange(ref _terminal, null)?.End(SafeModeHeld);
            SafeModeHeld = false;
        }

        // ── Semaphore-serialised execution ────────────────────────────────────

        /// <summary>
        /// Serialises access, fires diagnostics events, then sends the command through the transport.
        /// </summary>
        /// <remarks>
        /// <paramref name="ct"/> is observed at the points where observing it is safe, which on a terminal is
        /// not the same as "wherever it fires" — see <see cref="TransportToken"/> and
        /// <see cref="TikCancellationMode"/>. Cancelling while queued for <c>CommandLock</c> is free: the command
        /// has not been written, so nothing needs resynchronizing.
        /// </remarks>
        private protected Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
            => ExecuteCliCommandCoreAsync(cliText, ct, mayMarkEnd: true);

        private async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct, bool mayMarkEnd,
            bool interruptible = false)
        {
            var terminal = _terminal ?? throw NotOpen();
            string? endLine = null;
            if (mayMarkEnd && CliFieldSeparator == null && _knowledge.Features.Dsv == null && IsMarkableWrite(cliText))
                await _printDriver.EnsureEndMarkerKnownAsync(ct).ConfigureAwait(false);
            if (mayMarkEnd && TakesEndMarker(cliText))
            {
                cliText = CliOutputHelper.WithEndMarker(cliText, NewEndNonce());
                endLine = CliOutputHelper.EndMarkerLineOf(cliText);
            }
            await CommandLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();   // level 0 — nothing written yet
                FireWriteRow(cliText);
                string result;
                // Under Cooperative a read still ends by its prompt, so the terminal stays in step: the key only makes the
                // prompt come sooner (measured on 7.21.5 and 6.49.21 over Telnet: ~50 ms, and the next command read its own answer).
                using (interruptible && CancellationMode == TikCancellationMode.Cooperative && ct.CanBeCanceled
                    ? ct.Register(terminal.RequestInterrupt)
                    : default(CancellationTokenRegistration))
                {
                    result = await SendGuardedAsync(terminal, cliText, null, TransportToken(ct), ct).ConfigureAwait(false);
                }
                if (endLine != null)
                    result = CliOutputHelper.WithoutEndMarker(result, endLine);
                FireReadRow(result);
                // The safe point. The response has been drained, so the channel is consistent and the caller's
                // cancel can finally be honoured without costing the next command its answer.
                ct.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                CommandLock.Release();
            }
        }

        // Sends one command through the terminal under the caller's command lock: the receive-timeout resync, the end of a
        // RoMON relay, and a Close() on another thread are handled here once, for the plain and the streaming read alike.
        private async Task<string> SendGuardedAsync(ICliTerminal terminal, string cliText, Action<string>? onLine,
            CancellationToken transportToken, CancellationToken callerToken)
        {
            try
            {
                return await terminal.SendCommandAndReadAsync(cliText, onLine, transportToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                // Only reachable in AbandonAndClose: the read was cut mid-response, so whatever the router
                // is still writing would be read by the next command. Close instead of pretending we are
                // in step again.
                CloseAfterAbandonedRead();
                throw;
            }
            catch (TikConnectionResponseIncompleteException) when (callerToken.IsCancellationRequested
                                                                   && CancellationMode == TikCancellationMode.Cooperative)
            {
                // An interrupted read is short by design, and a transport that vets its answers says so. The caller asked.
                throw new OperationCanceledException(callerToken);
            }
            catch (TikConnectionReceiveTimeoutException)
            {
                // The response never reached a prompt, so its tail is still on its way and the NEXT command
                // would read it as its own answer. Bring the session back in step (TryResynchronizeAsync),
                // and only when that fails stop pretending it is usable.
                if (!await TryResynchronizeAsync().ConfigureAwait(false))
                    CloseAfterAbandonedRead();
                throw;
            }
            catch (TikRomonRelayEndedException)
            {
                // The agent's session ended with the relay; there is nothing left to send commands into.
                CloseAfterAbandonedRead();
                throw;
            }
            catch (Exception ex) when (!IsOpened && !(ex is TikConnectionException))
            {
                // Close() ran on another thread while this command held the socket. IsOpened is already
                // false because Close sets it first, which is what makes this distinguishable from a
                // transport that simply broke.
                throw ClosedWhileRunning(ex);
            }
        }

        /// <summary>
        /// Whether a command the library built gets the end marker (<see cref="CliOutputHelper.WithEndMarker"/>): a
        /// one-line write — <c>add</c>, <c>set</c>, <c>remove</c>, <c>enable</c>, <c>disable</c>, <c>move</c>,
        /// <c>unset</c>, <c>comment</c>, bare or as <c>:put [… add …]</c> — on a router known to be RouterOS 7.
        /// </summary>
        /// <remarks>
        /// Its read then ends on the marker line instead of on a prompt that stayed silent for the settle window, which
        /// was about half of what such a command cost (SSH, 800 writes of a full suite leg, 2026-10-01). Not on
        /// RouterOS 6, which repaints the whole line after every typed character: 30 characters more echo cost more
        /// than the settle they save. Not on a command that may ask a question (<c>reboot</c>) — only the verbs
        /// above — and not on a raw command, which is sent as written. A connection with a separator knows from its first
        /// read, and one that only writes stays unmarked rather than pay the probe; a connection with no separator asks at
        /// its first write (<c>CliPrintDriver.EnsureEndMarkerKnownAsync</c>).
        /// </remarks>
        private bool TakesEndMarker(string cliText)
            => _knowledge.Features.Dsv == true && IsMarkableWrite(cliText);

        internal static bool IsMarkableWrite(string cliText)
            => cliText.IndexOf('\n') < 0 && cliText.IndexOf('\r') < 0 && WriteVerbLine.IsMatch(cliText);

        private static readonly System.Text.RegularExpressions.Regex WriteVerbLine = new System.Text.RegularExpressions.Regex(
            @"^(:put \[)?/(?:[\w.-]+ )*(add|set|remove|enable|disable|move|unset|comment)(?=[ \]]|$)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static string NewEndNonce() => Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>
        /// The token the <b>transport read</b> is given, which is deliberately not always the caller's.
        /// In <see cref="TikCancellationMode.Cooperative"/> it is <see cref="CancellationToken.None"/>: the
        /// response must be read to its end before the cancel is reported, because a terminal has no framing
        /// to resynchronize on. In <see cref="TikCancellationMode.AbandonAndClose"/> the caller's token is
        /// passed down, and the connection is closed if it fires.
        /// </summary>
        private CancellationToken TransportToken(CancellationToken ct)
            => CancellationMode == TikCancellationMode.AbandonAndClose ? ct : CancellationToken.None;

        /// <summary>How long the terminal must stay silent after the Ctrl-C before the fence is sent.</summary>
        private const int ResyncQuietMs = 1000;

        /// <summary>How many fences are sent before the session is given up as out of step.</summary>
        private const int ResyncFenceAttempts = 3;

        /// <summary>
        /// Brings a terminal whose read timed out back in step, rather than closing it: Ctrl-C aborts whatever still
        /// runs (and dismisses a pager or a prompt), a settle read drains what the router writes until it goes quiet,
        /// then <c>:put ("t4n-sync-" . "&lt;guid&gt;")</c> is sent and read to its prompt. The router prints the JOINED
        /// string; the echo of the typed line never contains it, so finding it in the answer proves the channel is
        /// at the fence's own prompt. True when it is; false (and the caller closes) otherwise.
        /// </summary>
        /// <remarks>
        /// Only on the unhappy path — a fence before every command would cost a round trip each. Never Enter: it runs
        /// whatever sits on the input line, and at <c>new password&gt;</c> it would submit one. The timed-out command
        /// itself may have been cut part-way by the Ctrl-C; its outcome is as unknown as it was before, which the
        /// timeout exception the caller receives already says.
        /// </remarks>
        private async Task<bool> TryResynchronizeAsync()
        {
            if (!(_terminal is { SupportsQuietRead: true } terminal))
                return false;
            try
            {
                await terminal.SendRawAndReadUntilQuietAsync(new[] { CliTerminalText.CtrlC }, ResyncQuietMs, CancellationToken.None)
                    .ConfigureAwait(false);
                for (int attempt = 1; attempt <= ResyncFenceAttempts; attempt++)
                {
                    string guid = Guid.NewGuid().ToString("N");
                    string answer = await terminal.SendCommandAndReadAsync(":put (\"t4n-sync-\" . \"" + guid + "\")", null,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (answer.IndexOf("t4n-sync-" + guid, StringComparison.Ordinal) >= 0)
                    {
                        TikWireTrace.Emit("cli.resync", TikWireDir.Note,
                            "back in step after a receive timeout (fence " + attempt + ")");
                        return true;
                    }
                }
                TikWireTrace.Emit("cli.resync", TikWireDir.Note,
                    "no fence came back after " + ResyncFenceAttempts + " tries — closing");
            }
            catch (Exception ex)
            {
                TikWireTrace.Emit("cli.resync", TikWireDir.Note, "resync failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return false;
        }

        // Closes the session after a read was abandoned. Best-effort: the point is that the connection is
        // marked unusable, and a transport that throws while closing an already-broken channel must not
        // replace the OperationCanceledException the caller is waiting for.
        private void CloseAfterAbandonedRead()
        {
            TikWireTrace.Emit("cli.cancel", TikWireDir.Note,
                "read abandoned mid-response (in-flight cancel with TikCancellationMode.AbandonAndClose, "
                    + "a receive timeout, or the end of a RoMON relay) — closing the connection, the unread "
                    + "response cannot be resynchronized");
            MarkLost();
            try { Close(); }
            catch (Exception ex)
            {
                TikWireTrace.Emit("cli.cancel", TikWireDir.Note,
                    "close after abandoned read failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Synchronous wrapper around <see cref="ExecuteCliCommandAsync"/>.</summary>
        private protected string ExecuteCliCommand(string cliText)
            => ExecuteCliCommandAsync(cliText, CancellationToken.None).GetAwaiter().GetResult();

        // ── Streaming read (incremental line delivery) ─────────────────────────

        // The optional streaming read (ICliTerminal.SupportsLineStreaming): "send a command and report each COMPLETED
        // output line while the command is still running", returning the same cleaned output the plain read returns
        // when it finally ends. All four terminal clients have it; a terminal without it is not broken, it just falls
        // back to the one-shot as-value monitor (see SnapshotOnce).

        /// <summary>True when this transport can deliver a command's output line by line as it arrives.</summary>
        private protected bool SupportsStreamingRead => _terminal?.SupportsLineStreaming == true;

        /// <summary>
        /// Runs a command, reporting each completed output line to <paramref name="onLine"/> as the router
        /// produces it, and returns the full cleaned output once the command ends. The lines are raw — echo,
        /// header and prompt included — because the caller parses them positionally.
        /// </summary>
        private protected string ExecuteCliCommandStreaming(string cliText, Action<string> onLine)
        {
            var terminal = _terminal is { SupportsLineStreaming: true } t ? t : throw new NotSupportedException(
                $"{TransportName} does not support incremental reads.");
            CommandLock.Wait();
            try
            {
                FireWriteRow(cliText);
                string result = SendGuardedAsync(terminal, cliText, onLine, CancellationToken.None, CancellationToken.None)
                    .GetAwaiter().GetResult();
                FireReadRow(result);
                return result;
            }
            finally
            {
                CommandLock.Release();
            }
        }

        // ── Safe Mode (Ctrl+X / Ctrl+D control keys) ───────────────────────────

        /// <summary>Ctrl+X — toggles Safe Mode in the RouterOS terminal (take, then commit). Byte 0x18.</summary>
        private const byte CtrlX = 0x18;

        /// <summary>Enter — declines RouterOS's question about another session's Safe Mode. Byte 0x0D.</summary>
        private const byte Enter = 0x0D;

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X, with no line terminator) to the terminal and returns
        /// the ANSI-stripped response read up to the next stable shell prompt. The bytes are sent verbatim and
        /// the output is not echo/prompt-stripped — the caller inspects the raw terminal reaction
        /// (e.g. <c>[Safe Mode taken]</c>). Drives the terminal's <see cref="ICliTerminal.SendRawAndReadAsync"/>.
        /// </summary>
        private protected Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
            => (_terminal ?? throw NotOpen()).SendRawAndReadAsync(raw, ct);

        private string SendControlKey(byte key)
        {
            CommandLock.Wait();
            try
            {
                FireWriteRow($"<ctrl-0x{key:X2}>");
                string result = SendRawAndReadAsync(new[] { key }, CancellationToken.None).GetAwaiter().GetResult();
                FireReadRow(result);
                return result;
            }
            finally { CommandLock.Release(); }
        }

        /// <summary>
        /// Enters Safe Mode by sending <c>Ctrl+X</c> in the live terminal. RouterOS prints
        /// <c>[Safe Mode taken]</c> and shows the <c>&lt;SAFE&gt;</c> token in the prompt (handled transparently
        /// by <see cref="RouterOsCliLogin.IsShellPrompt"/>); the rollback is tied to THIS terminal session, so
        /// dropping the connection without a <see cref="SafeModeRelease"/> reverts every change made since. Works
        /// on any RouterOS version (no scriptable <c>/safe-mode</c> needed). No-op when already held.
        /// </summary>
        /// <remarks>
        /// When another session holds Safe Mode, RouterOS asks what to do with it and waits. The question is answered
        /// with Enter, which changes nothing on either version measured (6.49.13 "Safe mode not taken", 7.24.4
        /// "Action aborted."), before the refusal is thrown: a session left inside the question takes the next command
        /// as its answer, and one that ends there wedges RouterOS 6.49.13's console until a reboot — every later
        /// Telnet, SSH and API login is accepted and never served.
        /// </remarks>
        public void SafeModeTake()
            => SafeModeTransition(true, () =>
            {
                string output = SendControlKey(CtrlX);
                if (CliSafeModeParser.IsTakeConflict(output))
                    SendControlKey(Enter);
                CliSafeModeParser.ThrowIfTakeFailed(output, CommandFor("/safe-mode/take"));
            });

        /// <summary>
        /// Commits the safe-mode changes and leaves Safe Mode by sending a second <c>Ctrl+X</c>; the prompt
        /// reverts to its normal form afterwards. No-op when safe mode is not held.
        /// </summary>
        public void SafeModeRelease()
            => SafeModeTransition(false, () => SendControlKey(CtrlX));

        /// <summary>
        /// Discards the safe-mode changes immediately and leaves Safe Mode. On RouterOS 7.18+ this uses the
        /// scriptable <c>/safe-mode/unroll</c> and the connection stays open. On an older router (RouterOS 6, and
        /// 7.x before 7.18) there is no way to unroll in place: the connection is closed, which unrolls the
        /// changes, and <see cref="ITikConnection.IsOpened"/> is <c>false</c> afterwards. No-op when safe mode is
        /// not held.
        /// </summary>
        /// <remarks>
        /// The terminal's own discard key does not keep the session either: <c>Ctrl+D</c> in Safe Mode unrolls and
        /// ends the terminal session, with no reply to read — measured on 6.49.13 and 7.24.4 over Telnet (the session
        /// leaves <c>/user active</c>, and the next write is refused by the host). Sent anyway, it left a connection that
        /// looked open and swallowed every later command for its full receive timeout. Closing instead answers
        /// <c>/quit</c>'s "Quitting will unroll changes. Quit? [y/N]" with <c>y</c>: the same unroll, done at once.
        /// </remarks>
        public void SafeModeUnroll()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;

            try
            {
                string output = ExecuteCliCommand("/safe-mode/unroll");
                CliErrorParser.ThrowIfError(output, CommandFor("/safe-mode/unroll"),
                    silentOnSuccess: false); // prints "Unrolling Safe Mode... Success!"
                SafeModeHeld = false;
                return;
            }
            catch (TikNoSuchCommandException)
            {
                // RouterOS predates scriptable /safe-mode.
            }

            // Before 7.18 no way to unroll in place exists: Close answers /quit's question with 'y', which unrolls.
            Close();
        }

        /// <inheritdoc/>
        public bool SafeModeGet() => SafeModeHeld;

        // ── Tab completion and the help key (CliCompletion) ────────────────────

        /// <inheritdoc/>
        public IReadOnlyList<string> CompleteCli(string partialInput) => _completion.CompleteCli(partialInput);

        /// <inheritdoc/>
        public string CompleteCliRaw(string partialInput) => _completion.CompleteCliRaw(partialInput);

        string ICliHelpKey.HelpCli(string partialInput) => _completion.HelpCli(partialInput);

        (IReadOnlyList<CliCompletionItem> Items, string Raw) ICliCompletionReaction.CompleteCliBoth(string partialInput)
            => _completion.CompleteCliBoth(partialInput);

        // ── CRUD hooks — CLI text build + parse ────────────────────────────────
        //
        // The terminal clients are async to the socket, so the Task-based hooks are the real implementation
        // here and the synchronous ones block on them (async is the primitive; nothing is pushed onto a
        // thread-pool thread to look asynchronous). Every await below carries ConfigureAwait(false), which is
        // also what keeps the blocking wrappers safe to call from a UI / ASP.NET-classic SynchronizationContext.

        /// <inheritdoc/>
        protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => RunPrintAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override string RunAdd(TikCommandDescriptor descriptor)
            => RunAddAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override void RunNonQuery(TikCommandDescriptor descriptor)
            => RunNonQueryAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override string RunRawText(TikCommandDescriptor descriptor)
            => RunRawTextAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override Task<IList<TikRecordSentence>> RunPrintAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => _printDriver.RunPrintAsync(descriptor, cancellationToken);

        // ExecuteListUntilDone(timeout): torch never ends, so one frame would answer where the binary API reads on until the
        // timeout ends the read. Every other read is the plain one, under the deadline.
        internal override Task<IList<TikRecordSentence>> RunPrintUntilDoneAsync(TikCommandDescriptor descriptor,
            CancellationToken cancellationToken)
            => !descriptor.IsRaw && CliVerbs.ReadShapeOf(TikPath.Verb(descriptor.CommandText)) == CliReadShape.Torch
                ? _monitorDriver.ReadTorchUntilCancelledAsync(descriptor, cancellationToken)
                : RunPrintAsync(descriptor, cancellationToken);

        /// <summary>
        /// True while the command on the wire is a read whose answer this class will count against the
        /// router's own statement of its size — a window, a whole-table print, a counted argument read
        /// (<c>/console inspect</c>), or the grammar cache's build key. The MAC-layer datagram-loss
        /// heuristic stands down for these: the count is exact and the heuristic is not, and it condemns
        /// complete answers now and then. It still covers every other command.
        /// </summary>
        private protected bool IsCountedReadInFlight => _windowedReadDepth > 0 || _countedReadDepth > 0;

        private int _windowedReadDepth;
        private int _countedReadDepth;

        /// <summary>
        /// Executes an <c>add</c> command and returns the new record's .id.
        /// </summary>
        protected override async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string cliText = CliCommandBuilder.BuildAdd(descriptor.CommandText, descriptor.Parameters);
            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            CliErrorParser.ThrowIfError(output, CommandFor(descriptor));

            string? id = ExtractAddId(output);
            if (id != null)
                return id;

            // No id in the answer, and the router did not complain: the row is very probably on the router with
            // nothing referring to it (TikAddIdNotRead says why that is thrown, not returned). On a terminal the
            // cause is a read that settled too early: the answer is an unframed byte stream, so a reply arriving
            // after the prompt has gone quiet is lost rather than late.
            throw TikAddIdNotRead.Create(CommandFor(descriptor), output);
        }

        /// <summary>
        /// Extracts the new record's <c>.id</c> from an <c>add</c> response. Normally the cleaned output is a
        /// single <c>*N</c> token. But when a parameter VALUE contains newlines (e.g. a script <c>source</c>
        /// with embedded line breaks), RouterOS's line editor enters bracket-continuation mode and echoes the
        /// continuation lines (<c>["... …</c>) BEFORE printing the result, so the cleaned output is multi-line
        /// with the real id on the LAST non-empty line. So the id is looked for on every line, from the last
        /// backwards.
        /// <para>
        /// <c>null</c> means <b>no line was an id</b>, and the caller reports that rather than substituting
        /// something for it. A fallback to the last non-empty line would turn a read that had gone wrong into
        /// an id-shaped string, stored on the entity and sent in the <c>[find where .id=…]</c> of the next set
        /// or remove, where it matches nothing.
        /// </para>
        /// </summary>
        private static string? ExtractAddId(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (CliValueNormalizer.IsRecordId(t))
                    return CliValueNormalizer.NormalizeId(t);   // pre-7.20 answers in lowercase hex
            }
            return null;
        }

        /// <summary>
        /// Executes a non-query command (set, remove, enable, disable, move, reboot, …).
        /// </summary>
        protected override async Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string verb = TikPath.Verb(descriptor.CommandText);

            string cliText;
            switch (verb)
            {
                case "run":
                    // An action verb: fire-and-forget over the terminal, no result set. ExecuteNonQuery is the supported
                    // entry point (ExecuteList throws). It addresses a row, by [find].
                    cliText = CliCommandBuilder.BuildRowVerb(descriptor.CommandText, verb, descriptor.Parameters);
                    break;
                default:
                    // 'comment' is a real RouterOS menu command (it appears in Tab-completion next to set/remove/move),
                    // not a synonym for 'set comment=', and takes the same selector.
                    cliText = CliVerbs.IsRowWrite(verb)
                        ? CliCommandBuilder.BuildRowVerb(descriptor.CommandText, verb, descriptor.Parameters)
                        : CliCommandBuilder.BuildNonQuery(descriptor.CommandText, descriptor.Parameters);
                    break;
            }

            string output = await ExecuteCliCommandAsync(cliText, cancellationToken).ConfigureAwait(false);
            // set/remove/enable/disable/move/unset print nothing when they succeed, so anything left after
            // echo/prompt trimming is the router rejecting the command — catch it by position rather than
            // by phrase, or it is reported to the caller as success.
            CliErrorParser.ThrowIfError(output, CommandFor(descriptor),
                silentOnSuccess: CliErrorParser.IsSilentOnSuccessVerb(verb));
        }

        /// <summary>
        /// Raw pass-through scalar/non-query (CreateRawCommand): sends the command line verbatim and returns the
        /// cleaned terminal output text (ANSI-stripped, echo/prompt-trimmed by the transport).
        /// Used by <c>ExecuteScalar</c> (e.g. <c>/export</c>) and <c>ExecuteNonQuery</c>.
        /// </summary>
        /// <remarks>
        /// The output IS checked for a router error, although raw mode cannot know what counts as one for an
        /// arbitrary command: unchecked, <c>bad command name prnt (line 1 column 12)</c> would be returned as a
        /// successful value, to be assigned and used, while the command never ran. The
        /// check is <see cref="CliErrorParser"/>'s phrase list, over the whole answer: a raw answer has no shape to
        /// tell a value from a refusal, so a value reading <c>no such item</c> is reported as one (findings-cli §10).
        /// <para>
        /// On top of the phrases, an answer that is one parse error and nothing else — a single line ending in
        /// <c>(line N column M)</c>, such as <c>expected yes or no (line 1 column 44)</c> — is the router refusing
        /// the line (findings-cli §10), and is raised as <see cref="TikCommandTrapException"/>. Output of several
        /// lines is never read that way.
        /// </para>
        /// </remarks>
        protected override async Task<string> RunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureOpened();
            string rawCli = descriptor.WrapAsValue
                ? WrapRawAsValue(descriptor.CommandText)
                : descriptor.CommandText;
            string output = (await ExecuteCliCommandAsync(rawCli, cancellationToken).ConfigureAwait(false)).Trim();
            CliErrorParser.ThrowIfError(output, CommandFor(descriptor));
            CliErrorParser.ThrowIfSyntaxError(output, CommandFor(descriptor));
            return output;
        }

        // Wraps a verbatim CLI line so RouterOS materialises its as-value output (bare 'print as-value' prints
        // nothing to a terminal — only script context, i.e. inside ':put [ … ]', emits the as-value line).
        internal static string WrapRawAsValue(string rawCli)
            => ":put [" + (rawCli ?? string.Empty).Trim() + " as-value]";

        // ── Streaming monitor / async / listen (ITikMonitorTransport) ──────────

        /// <summary>
        /// Dispatches a callback-based async command (<c>ExecuteWithCallback</c>/<c>LoadWithCallback</c>/<c>LoadListenWithCallback</c>)
        /// onto a background worker. A terminal has no server push, so all three shapes are emulated by polling:
        /// <list type="bullet">
        ///   <item><c>/path/print</c> (LoadWithCallback) — run the read once off-thread, emit rows, complete.</item>
        ///   <item><c>/path/listen</c> — poll the table and diff snapshots by <c>.id</c>; an added/changed row
        ///         fires <paramref name="onRow"/>, a vanished <c>.id</c> fires a synthetic <c>.dead=true</c>
        ///         record (routed to <c>onDeleted</c> by the O/R layer).</item>
        ///   <item>a monitor verb (<c>monitor-traffic</c>, <c>profile</c>, <c>ping</c>, …) — re-issue a one-shot
        ///         <c>:put [… &lt;snapshot-modifier&gt; as-value]</c> every <c>CliMonitorDriver.MonitorPollIntervalMs</c> ms and
        ///         emit each polled record. <c>torch</c> is driven differently — see <c>TorchFreezeFrameLoop</c>.</item>
        /// </list>
        /// The worker takes the same command turn everything else does, so CRUD from another thread while a
        /// monitor runs is safe — it queues behind the poll rather than interleaving with it. What it is not
        /// is dependable in the other direction: the worker holds the terminal on its own cadence, so a
        /// change made over this <i>same</i> connection can fall between two polls and never be reported.
        /// See the thread-safety remarks on <see cref="CliConnectionBase"/>.
        /// </summary>
        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            EnsureOpened();
            string verb = TikPath.Verb(descriptor.CommandText);

            var table = PollingMonitorEngine.TryStartTableShape("cli", this, descriptor, verb, null, onRow, onError, onDone);
            if (table != null) return table;

            return _monitorDriver.Start(descriptor, verb, onRow, onError, onDone);
        }

        // ── IPollingMonitorHost (shared listen/async-list scaffolding lives in PollingMonitorEngine) ──

        /// <inheritdoc/>
        bool IPollingMonitorHost.ClosedByCaller => ClosedByCaller;

        /// <inheritdoc/>
        IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
            // RunPrint serialises via ExecuteCliCommand's CommandLock. Not through InvokeRunPrint, so it trims itself.
            => TikProplist.Apply(printDescriptor, RunPrint(printDescriptor));

        /// <inheritdoc/>
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);
    }
}
