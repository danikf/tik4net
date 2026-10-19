using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.Connection;
using tik4net.Diagnostics;
using tik4net.Winbox;

namespace tik4net.WinboxNative
{
    /// <summary>
    /// MikroTik RouterOS WinBox <b>native-M2</b> connection (TCP port 8291).
    /// Performs reads as structured M2 <c>getall</c>/<c>get-one</c> calls (no terminal), translating
    /// numeric WinBox field keys back to RouterOS API field names so the existing O/R mapper works
    /// unchanged.
    /// <para><b>Experimental.</b> See the remarks for what that means and what to use instead in
    /// production.</para>
    /// </summary>
    /// <remarks>
    /// <para><b>Experimental — and for one specific reason.</b> M2 addresses everything by NUMBER, and the
    /// API name ↔ WinBox key mapping is reconstructed from the router's own <c>.jg</c> catalog rather than
    /// being a published contract, so translating RouterOS API syntax into it is not a straightforward one.
    /// It covers common tables, but an exotic table or verb may need a session mapping
    /// (<c>PathAlias</c>/<c>PathOverride</c>/<c>FieldOverride</c>), and mapping details may still change
    /// between releases.
    /// <b>For production work prefer <see cref="WinboxCli.WinboxCliConnection"/></b> — the stable, proven
    /// transport on the same encrypted WinBox channel (same port, same crypto), which drives the router's
    /// own CLI and so needs no name mapping at all. The two are interchangeable at the
    /// <see cref="ITikConnection"/> level, so moving between them is a one-line change to the
    /// <see cref="TikConnectionType"/>. The wiki page <i>WinBox-Native-connection</i> explains the mapping
    /// problem in full ("The mapping problem: API names ↔ WinBox keys").</para>
    /// <para>Full CRUD: <see cref="ITikConnection.CreateCommand()"/> + <c>ExecuteList</c>/<c>LoadAll</c>
    /// route reads through native M2 <c>getall</c>/<c>get-one</c>; <c>Save</c>/<c>Add</c>/<c>Delete</c>/<c>Move</c>
    /// route writes through native <c>set</c>/<c>add</c>/<c>remove</c>/<c>move</c>.</para>
    /// <para>Authentication and the encrypted channel are reused from the shared
    /// <see cref="WinboxM2Session"/> (EC-SRP5 / legacy-MD5, AES-128-CBC). Field keys/types are loaded at
    /// connect time from the <c>.jg</c> catalog the router itself advertises (cached under
    /// <see cref="CatalogCachePath"/>); the apiName↔label mapping is a stable normalizer plus
    /// session overrides.</para>
    /// <para>Streaming monitors are supported via <c>ExecuteWithCallback</c>/<c>LoadWithCallback</c> (capability
    /// <see cref="TikConnectionCapability.Listen"/>): <c>.jg</c> <c>type:'query'</c> windows such as
    /// <c>/tool/torch</c>/<c>/tool/profile</c> are polled start→poll→cancel on a background worker.</para>
    /// <para><see cref="ITikConnection.ConnectTimeout"/> bounds the connect handshake and then the
    /// authentication exchange, but not the <c>.jg</c> catalog load that follows them.</para>
    /// <para><b>Thread safety.</b> One open connection may be used from several threads, and commands
    /// genuinely overlap: the M2 channel is multiplexed — every request carries an id the router echoes
    /// back, and a single reader loop hands each reply to the caller waiting for it, so a running monitor
    /// does not block CRUD on another thread. The concurrency is <i>between</i> independent operations,
    /// not within one: a read that needs reference resolution still issues its follow-up <c>getall</c>
    /// calls in sequence, because each depends on the previous reply. <c>Open</c> is the exception —
    /// authentication, the version probe and the <c>.jg</c> catalog load run lockstep on the raw channel
    /// before the reader loop starts, so open the connection from one thread and share it afterwards. The
    /// rest of the contract — <c>Close</c> not waiting for a running command, Safe Mode as connection-wide
    /// state, and the router's shared throughput ceiling — is on <see cref="ITikConnection"/>.</para>
    /// </remarks>
    public class WinboxNativeConnection : TikCommandConnectionBase, ITikWinboxNativeConnection,
        ITikMonitorTransport, IPollingMonitorHost, ITikMenuSchemaConnection, ITikCatalogCacheConnection,
        ITikRomonConnection, IWinboxNativeHost
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly); the MAC-layer
        // subclass constructor is internal too and calls this one.
        internal WinboxNativeConnection() : this(TikConnectionType.WinboxNative) { }

        /// <param name="connectionType">WinBox native, or its MAC-layer sibling.</param>
        private protected WinboxNativeConnection(TikConnectionType connectionType) : base(connectionType)
        {
            _monitor = new WinboxNativeMonitor(this);
            _writer = new WinboxNativeWriter(this);
        }

        // The monitors (WinboxNativeMonitor); the connection stays the polling engine's host.
        private readonly WinboxNativeMonitor _monitor;

        // The writes and the field encoding (WinboxNativeWriter).
        private readonly WinboxNativeWriter _writer;

        // ── What the collaborators reach the connection through (IWinboxNativeHost) ──

        WinboxNativeM2Operations IWinboxNativeHost.Ops => _ops;
        WinboxRecordCodec IWinboxNativeHost.Codec => _codec;
        WinboxIdResolver IWinboxNativeHost.IdResolver => _idResolver;
        WinboxJgCatalog IWinboxNativeHost.Catalog => _catalog;
        WinboxHandlerMap IWinboxNativeHost.HandlerMap => _handlerMap;
        IReadOnlyDictionary<string, string>? IWinboxNativeHost.LabelHints => _labelHints.Value;
        bool IWinboxNativeHost.UseGuiNames => _useGuiNames;
        bool IWinboxNativeHost.IsOpened => IsOpened;
        int IWinboxNativeHost.ReceiveTimeoutMs => ReceiveTimeoutMs;
        void IWinboxNativeHost.EnsureNativeOpen() => EnsureNativeOpen();
        TikCommandDescriptor IWinboxNativeHost.Canonical(TikCommandDescriptor descriptor) => Canonical(descriptor);
        ITikCommand IWinboxNativeHost.CommandFor(string commandText) => CommandFor(commandText);
        ITikCommand IWinboxNativeHost.CommandFor(TikCommandDescriptor descriptor) => CommandFor(descriptor);
        TikCommandException IWinboxNativeHost.TranslateM2Error(WinboxM2OperationException ex, string commandText)
            => TranslateM2Error(ex, commandText);
        IReadOnlyDictionary<string, int> IWinboxNativeHost.OverridesFor(string apiPath) => OverridesFor(apiPath);
        WinboxFieldResolver IWinboxNativeHost.MakeResolver(string apiPath, int[] handler) => MakeResolver(apiPath, handler);
        CommandGate IWinboxNativeHost.EnterCommand() => EnterCommand();
        Task<List<byte[]>> IWinboxNativeHost.EncodeNameValueFieldsAsync(int[] handler, TikCommandDescriptor descriptor,
            WinboxFieldResolver resolver, bool skipId, CancellationToken cancellationToken, bool allowReadOnly,
            bool includeFilters, bool skipSnapshotModifier)
            => _writer.EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId, cancellationToken, allowReadOnly,
                includeFilters, skipSnapshotModifier);
        List<byte[]> IWinboxNativeHost.EncodeNameValueFields(int[] handler, TikCommandDescriptor descriptor,
            WinboxFieldResolver resolver, bool skipId, Func<int[], string, int?> resolveRef, bool allowReadOnly,
            bool includeFilters, bool skipSnapshotModifier)
            => _writer.EncodeNameValueFields(handler, descriptor, resolver, skipId, resolveRef, allowReadOnly, includeFilters,
                skipSnapshotModifier);
        void IWinboxNativeHost.TakeLabelHints(TikCommandDescriptor descriptor) => TakeLabelHints(descriptor);
        Task<CommandGate> IWinboxNativeHost.EnterCommandAsync(CancellationToken cancellationToken) => EnterCommandAsync(cancellationToken);
        (int[] handler, WinboxFieldResolver resolver) IWinboxNativeHost.ResolveHandlerAndFields(string apiPath)
            => ResolveHandlerAndFields(apiPath);
        bool IWinboxNativeHost.IsSingletonWindow(string apiPath, int[] handler) => IsSingletonWindow(apiPath, handler);
        Task<IList<TikRecordSentence>> IWinboxNativeHost.RunActionWindowAsync(string apiPath, int[] handler,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => RunActionWindowAsync(apiPath, handler, descriptor, cancellationToken);
        Task IWinboxNativeHost.DispatchActionVerbAsync(string verb, string apiPath, int[] handler,
            WinboxFieldResolver resolver, TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => DispatchActionVerbAsync(verb, apiPath, handler, resolver, descriptor, cancellationToken);

        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

        /// <summary>
        /// Directory under which the router's <c>.jg</c> menu catalogs are cached, as
        /// <c>&lt;CatalogCachePath&gt;/plugins/&lt;unique&gt;.jg</c>.
        /// Defaults to <see cref="TikConnectionSetup.DefaultCatalogCachePath"/>; <c>null</c> or empty keeps nothing on
        /// disk. Set before opening to change (<see cref="TikConnectionSetup.CatalogCachePath"/> sets it).
        /// Supports environment variables (<c>%APPDATA%</c>, <c>$HOME</c>, …) and relative paths
        /// (resolved against <see cref="Environment.CurrentDirectory"/> at open time).
        /// </summary>
        /// <remarks>
        /// Which plugins a router serves is resolved from the router itself on every open (a ~2 KB list), so
        /// routers on the same RouterOS version but with different packages installed are handled correctly.
        /// Only the plugin bodies are cached, under the version-stamped name the router reports for each —
        /// so the cache is shared across routers that serve the same file, and a RouterOS upgrade simply
        /// resolves new names rather than needing the cache invalidated. Deleting the directory is safe.
        /// </remarks>
        public string? CatalogCachePath { get; set; } = TikConnectionSetup.DefaultCatalogCachePath;

        /// <summary>
        /// Number of M2 handlers the <c>.jg</c> catalog supplied for this connection; <c>0</c> until
        /// <see cref="Connection.TikConnectionBase.Open(string, int, string, string)"/> completes.
        /// </summary>
        /// <remarks>
        /// A catalog load is best-effort, and a connection whose catalog did not load still opens and still
        /// answers most commands — but it runs on the built-in seed table, where singleton windows, dynamic
        /// (counter) fields and streaming monitors are all unknown. That returns <i>wrong values</i> rather
        /// than errors: firewall <c>bytes</c>/<c>packets</c> come back as 0 because the getall stats bit was
        /// never set. A healthy 7.23.2 CHR reports well over a thousand, so treat <c>0</c> — or a count
        /// far below a previous connection's — as a degraded connection worth reopening.
        /// <para>The figure counts every field map the catalog holds, windows and actions included, not one
        /// per M2 handler. Use it to tell a loaded catalog from an empty one and to compare two connections
        /// of the SAME build; a catalog change can move the absolute number without anything degrading.</para>
        /// </remarks>
        public int CatalogHandlerCount => _catalog.HandlerCount;

        private readonly WinboxHandlerMap _handlerMap = new WinboxHandlerMap();
        // apiPath → (apiName → key) session field overrides
        private readonly Dictionary<string, Dictionary<string, int>> _fieldOverrides =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        // _session, _ops, _codec, _idResolver are assigned in Open (not the constructor).
        private IWinboxM2Channel _session = null!;
        private WinboxM2Multiplexer? _mux;   // null on transports that cannot run a reader loop (MAC)
        private WinboxNativeM2Operations _ops = null!;
        private WinboxRecordCodec _codec = null!;   // M2 record → API field decoder (see WinboxRecordCodec)
        // The WinBox labels the O/R mapper sent with the command being run (TikSpecialProperties.WinboxLabels).
        // Set at each command entry — an async method, so the value never outlives the command — and handed to
        // every resolver the command builds.
        private readonly AsyncLocal<IReadOnlyDictionary<string, string>?> _labelHints = new AsyncLocal<IReadOnlyDictionary<string, string>?>();

        private void TakeLabelHints(TikCommandDescriptor descriptor)
            => _labelHints.Value = WinboxFieldResolver.ParseLabelHints(FindParam(descriptor, TikSpecialProperties.WinboxLabels));

        /// <summary>
        /// A menu-label path becomes the API path it stands for (<c>/Interfaces/Interface/print</c> →
        /// <c>/interface/print</c>), and with <see cref="UseGuiNames"/> a filter on a WinBox caption becomes a
        /// filter on the API field it labels (<c>Chain</c> → <c>chain</c>).
        /// </summary>
        /// <remarks>
        /// Both happen once, before the filter check and the transport see the command. The handler resolves either
        /// spelling of the path, but everything keyed by API path does not: read under the menu-label path,
        /// <c>/interface</c>'s field aliases were skipped and <c>type</c> came back as the numeric type id. And the
        /// filter check compares a filter with the field names the router reports, which a caption is not.
        /// </remarks>
        internal override TikCommandDescriptor Canonical(TikCommandDescriptor descriptor)
        {
            if (descriptor.IsRaw)
                return descriptor;

            // An action window is addressed by the whole path (/tool/wol); everything else by the parent of its verb.
            string text = descriptor.CommandText;
            bool whole = _handlerMap.Resolve(text) != null;
            string path = whole ? text : TikPath.Parent(text);
            if (!whole && string.Equals(path, TikPath.Normalize(text), StringComparison.Ordinal))
                return descriptor;   // a single segment: no verb to split off
            string? apiPath = _handlerMap.CanonicalApiPath(path);
            string commandText = apiPath == null ? text : whole ? apiPath : apiPath + "/" + TikPath.Verb(text);

            IList<ITikCommandParameter> parameters = descriptor.Parameters;
            if (_useGuiNames && parameters.Any(p => p.ParameterFormat == TikCommandParameterFormat.Filter))
            {
                string resolvePath = apiPath ?? path;
                int[]? handler = _handlerMap.Resolve(resolvePath);
                if (handler != null)
                {
                    var resolver = MakeResolver(resolvePath, handler);
                    parameters = parameters.Select(p => p.ParameterFormat == TikCommandParameterFormat.Filter
                        ? FilterOnApiName(p, resolver) : p).ToList();
                }
            }

            return ReferenceEquals(commandText, text) && ReferenceEquals(parameters, descriptor.Parameters)
                ? descriptor
                : new TikCommandDescriptor(commandText, parameters, descriptor.IsRaw, descriptor.WrapAsValue);
        }

        // The filter with its caption replaced by the API name; an operator prefix ('?', '-', '<', '>', '=') stays.
        private static ITikCommandParameter FilterOnApiName(ITikCommandParameter filter, WinboxFieldResolver resolver)
        {
            string name = filter.Name;
            int start = 0;
            while (start < name.Length && "?-<>=".IndexOf(name[start]) >= 0)
                start++;
            string? apiName = resolver.GuiCaptionToApiName(name.Substring(start));
            return apiName == null
                ? filter
                : new TikCommandParameter(name.Substring(0, start) + apiName, filter.Value, filter.ParameterFormat);
        }
        private WinboxIdResolver _idResolver = null!;   // friendly-name → M2 id lookup (see WinboxIdResolver)
        // Replaced at open time by the process-shared catalog for this router's plugin set
        // (see WinboxJgCatalog.Load); the empty default keeps pre-open access harmless.
        private WinboxJgCatalog _catalog = new WinboxJgCatalog();

        // What Open was given, kept so a session the router dropped can be rebuilt without the caller
        // (see ReopenAsync). The password lives no longer than the connection does and no more exposed
        // than the MAC-Telnet transport's, which captures the same four values in its reopen closure.
        private string _host = null!, _user = null!, _password = null!;
        private int _port;

        // Guards the rebuild, and tells a caller that arrives with a stale session from one that has to do
        // the rebuilding: _sessionGeneration advances once per successful reopen, so two commands failing on
        // the same dead session produce one reopen and not two.
        private readonly SemaphoreSlim _reopenLock = new SemaphoreSlim(1, 1);
        private int _sessionGeneration;

        // ── Session configuration (set before/after open) ──────────────────────

        /// <inheritdoc/>
        public void FieldOverride(string apiPath, string apiName, int key)
        {
            string norm = WinboxHandlerMap.Normalize(apiPath);
            if (!_fieldOverrides.TryGetValue(norm, out var map))
            {
                map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _fieldOverrides[norm] = map;
            }
            map[apiName] = key;
        }

        /// <inheritdoc/>
        public void PathAlias(string apiPath, string winboxMenuPath)
        {
            _handlerMap.AddAlias(apiPath, winboxMenuPath);
        }

        /// <inheritdoc/>
        public void PathOverride(string apiPath, int[] handler)
        {
            _handlerMap.AddOverride(apiPath, handler);
        }

        private bool _useGuiNames;

        /// <summary>
        /// When <c>true</c>, paths and field names may be addressed by the label seen in the <b>WinBox GUI</b>
        /// (spaces or underscores, any case, abbreviation dots) in addition to the exact RouterOS API name —
        /// e.g. <c>"MAC_Address"</c> or <c>"MAC Address"</c> resolve to <c>"mac-address"</c>. A name that resolves
        /// verbatim is never re-normalized, and <see cref="FieldOverride"/>/<see cref="PathOverride"/> still win,
        /// so this is a best-effort convenience layered under strict API-name resolution. Default <c>false</c>
        /// (strict, predictable). Decoded output always uses canonical API names regardless of this flag.
        /// <para>
        /// It covers a read's filters as well as writes: <c>CreateCommandAndParameters("/IP/Firewall/Filter/print",
        /// "Chain", "forward")</c> filters on <c>chain</c>. A path given as WinBox menu labels is read exactly as its
        /// API path — <c>/Interfaces/Interface/print</c> answers like <c>/interface/print</c> — with or without the flag
        /// for a path already in menu-label form, and with it for one with spaces or underscores.
        /// </para>
        /// <para>
        /// <b>Switchable at any time</b>, including after <c>Open</c> and between commands — the path/field
        /// resolvers are built per operation and read this flag then, so it can be scoped to a single call
        /// rather than the whole session:
        /// <code>
        /// conn.UseGuiNames = true;
        /// conn.CreateCommandAndParameters("/IP/Firewall/Filter/set", ".id", id, "Src. Address", "10.0.0.0/24")
        ///     .ExecuteNonQuery();
        /// conn.UseGuiNames = false;   // back to strict API-name resolution
        /// </code>
        /// What counts is the value at <b>execute</b> time, not when the command was created. Because this is
        /// mutable connection state, toggling it is not safe while the same connection is used from another
        /// thread — there, set it once before first use.
        /// </para>
        /// </summary>
        public bool UseGuiNames
        {
            get => _useGuiNames;
            set { _useGuiNames = value; _handlerMap.UseGuiNames = value; }
        }

        // ── Open / Close ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the (not-yet-opened) carrier this connection rides on. The base uses the TCP
        /// WinBox session (port 8291); <see cref="WinboxNativeMac.WinboxNativeMacConnection"/> overrides it
        /// to ride the MAC-layer channel (UDP 20561).
        /// </summary>
        private protected virtual IWinboxM2Channel CreateCarrier() => new WinboxM2Session();

        // Through a RoMON agent the carrier goes to the agent, and the relay channel routes every message on to the
        // target (Docs/findings-romon.md §5).
        private IWinboxM2Channel CreateChannel()
            => RomonTarget == null ? CreateCarrier() : new WinboxRomonChannel(CreateCarrier(), RomonTarget);

        /// <summary>
        /// When set before <c>Open</c>, the host, user and password given to <c>Open</c> are the AGENT's, and the
        /// connection continues to this target over the WinBox RoMON relay. Set by
        /// <see cref="TikConnectionSetup.ApplyTo"/> through <see cref="ITikRomonConnection"/>.
        /// </summary>
        internal RomonRelayTarget? RomonTarget { get; set; }

        /// <summary>Set once the relay has reached the target; <c>null</c> on a direct connection.</summary>
        internal TikRomonConnectionInfo? RomonConnectionInfo { get; private set; }

        RomonRelayTarget? ITikRomonConnection.RomonTarget { get => RomonTarget; set => RomonTarget = value; }

        TikConnectionType ITikRomonConnection.RomonAgentConnectionType => ConnectionType!.Value;

        TikRomonConnectionInfo? ITikRomonConnection.RomonConnectionInfo => RomonConnectionInfo;

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            _host = host;
            _port = port;
            _user = user;
            _password = password;
            OpenChannel();
        }

        // The open proper, split out so the reopen path (see ReopenAsync) can repeat it verbatim rather
        // than keeping a second, drifting copy of the login sequence.
        private void OpenChannel()
        {
            string host = _host, user = _user, password = _password;
            int port = _port;
            IWinboxM2Channel? session = null;
            RomonConnectionInfo = null;
            // A refused handshake leaves the channel unusable, so the retry builds a fresh one rather
            // than reopening this one — see TikOpenRetry for why a WinBox login is retried at all.
            TikOpenRetry.Run(() =>
            {
                session = CreateChannel();
                try
                {
                    session.Open(host, port, user, password, ConnectTimeoutMs, ReceiveTimeoutMs, SendTimeoutMs);
                }
                catch (Exception ex) when (TikOpenGuard.PassesThrough(ex))
                {
                    session.Dispose();
                    throw;
                }
                catch (Exception ex)
                {
                    session.Dispose();
                    throw new TikConnectionLoginException(ex);
                }
            }, TikOpenRetry.IsTransientRefusal);
            // Run() either assigns session above or throws, so it is always set here.
            string routerKey = host;
            if (session is WinboxRomonChannel relay)
            {
                RomonConnectionInfo = TikRomonConnectionInfo.Relayed(RomonTarget!, host, ConnectionType!.Value, user,
                    relay.AgentRomonId!, TikRomonRelay.Winbox);
                // The remembered plugin list is the TARGET's: keyed by the agent's host it would answer for every
                // router reached through that agent.
                routerKey = "romon-" + RomonTarget!.RomonId + "-via-" + host;
            }
            InitAfterAuth(session!, routerKey);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password,
            CancellationToken cancellationToken = default)
        {
            // Checked here and nowhere after, and that is the whole truth of it: everything below is
            // synchronous (see the note under this line), so there is no await for a token to interrupt.
            // Claiming otherwise by sprinkling checks between blocking calls would be theatre — the
            // handshake would still have to finish first. ConnectTimeout is the real bound.
            cancellationToken.ThrowIfCancellationRequested();
            // A Task.Run façade, and knowingly so — the one place on this transport that still is, now that
            // the command surface awaits for real. Opening means a blocking connect followed by the
            // EC-SRP5 handshake and the .jg catalog fetch, none of which has an awaitable form:
            // IWinboxM2Channel.Open is synchronous down through WinboxTcpTransport and the crypto, and that
            // is reverse-engineered code with no deterministic coverage. So this holds a thread-pool thread
            // for the duration of the open, which the async design says not to do; it is called out here rather
            // than left to be discovered. It does at least keep its promise to the CALLER — control returns
            // immediately — unlike a hook that would claim async and then block the caller.
            //
            // It stays. AsyncCommands is a claim about COMMANDS, and an open is once per
            // connection; buying it back means an awaitable form of the EC-SRP5 handshake, i.e. rewriting
            // the one part of this transport that has no deterministic coverage, for a thread held once.
            // MAC-Telnet and WinBox CLI open the same way and for the same reason; the capability's own
            // documentation says opening is excluded rather than leaving each site to explain itself.
            return Task.Run(() => Open(host, port, user, password), cancellationToken);
        }

        private void InitAfterAuth(IWinboxM2Channel session, string routerKey)
        {
            _session = session;
            // ReceiveTimeout, not ConnectTimeout: this bounds each M2 operation, not the connect phase.
            // (It was ConnectTimeout while per-read socket deadlines made the distinction
            // moot; with per-request deadlines in the multiplexer it is now the value that actually fires.)
            _ops = new WinboxNativeM2Operations(session, () => ReceiveTimeoutMs);
            // Participate in the shared row-level diagnostics: render each raw M2 request/reply to the
            // OnWriteRow/OnReadRow events (gated so the describe is only built when something listens).
            _ops.OnRequest = msg => { if (RowTracingEnabled) FireWriteRow(M2Message.Describe(msg)); };
            _ops.OnResponse = msg => { if (RowTracingEnabled) FireReadRow(M2Message.Describe(msg)); };
            // A RoMON relay whose link ended takes nothing more, so the connection closes as the CLI transports do.
            _ops.OnRelayEnded = Close;
            // Through _ops, not the raw channel: the catalog's mproxy transfer must share the same
            // request-id correlation as every other operation, or a stray frame during it desyncs the
            // channel for the rest of the connection (worst on MAC, which has no stale drain).
            // A fresh, empty catalog first: on a reopen the previous router's would otherwise survive a failed load
            // and answer for this one.
            _catalog = new WinboxJgCatalog();
            try { _catalog = WinboxJgCatalog.Load(_ops, TikCacheFileSystem.ResolveDirectory(CatalogCachePath), routerKey); }
            catch { /* catalog is best-effort; seeds + normalizer still work */ }
            // Never silently: a seeds-only catalog answers "no" to singleton/dynamic-field/monitor lookups,
            // which returns wrong values rather than errors. CatalogHandlerCount makes it visible.
            if (_catalog.HandlerCount == 0)
                TikWireTrace.Emit("wbx.catalog", TikWireDir.Note,
                    "no .jg handlers loaded — running on seed table only, dynamic counters and singleton "
                    + "reads will be wrong (see WinboxNativeConnection.CatalogHandlerCount)");
            // Feed the .jg-derived apiPath→handler map into the handler resolver (after session overrides,
            // before the shipped override tail).
            _handlerMap.SetDerivedPaths(_catalog.GetDerivedPaths());
            _handlerMap.SetSubtypeFilters(_catalog.GetSubtypeFilters());
            _codec = new WinboxRecordCodec(_ops, _catalog);
            _idResolver = new WinboxIdResolver(_ops, _catalog);
            StartMultiplexer(session);
            SetOpened();
        }

        /// <summary>
        /// Hands the channel's read side to a <see cref="WinboxM2Multiplexer"/> so M2 operations dispatch by
        /// request id instead of serializing on <c>CommandLock</c>.
        /// </summary>
        /// <remarks>
        /// Deliberately the <b>last</b> step of <see cref="InitAfterAuth"/>: authentication, the router
        /// version probe and the <c>.jg</c> catalog fetch all read the channel directly and would race the
        /// reader loop. Those run once, so leaving them lockstep costs nothing (design §4.2).
        /// <para>Both native transports multiplex. A channel that cannot yield its
        /// read side (<see cref="IWinboxM2Channel.SupportsReaderLoop"/>) would keep the lockstep path and its
        /// stale-frame drain instead.</para>
        /// </remarks>
        private void StartMultiplexer(IWinboxM2Channel session)
        {
            if (!session.SupportsReaderLoop) return;

            // Discard anything the lockstep init phase left buffered BEFORE the reader loop starts.
            // A leftover frame is not merely noise here: the multiplexer restarts request ids from 1, so a
            // stale frame carrying, say, id 3 could be delivered to a *new* request that later gets id 3 —
            // turning the old "reply shifted by one" desync into a silent wrong-reply. Draining is safe at
            // this exact point because every init exchange has already completed.
            _ops.DrainBufferedFrames();

            _mux = new WinboxM2Multiplexer(session);
            _ops.UseMultiplexer(_mux);
        }

        /// <summary>
        /// Acquires the command gate for one M2 operation. On a multiplexed connection this is a no-op:
        /// the reader loop correlates replies by request id, so concurrent operations are safe and
        /// serializing them would give back exactly the throughput multiplexing was added to gain. It stays
        /// the real semaphore on any channel left on the lockstep path, where a reader that takes "the next
        /// frame" would otherwise pick up someone else's reply.
        /// </summary>
        private CommandGate EnterCommand() => new CommandGate(_mux == null ? CommandLock : null);

        /// <summary>
        /// <see cref="EnterCommand"/> for an awaiting caller: on the multiplexed path there is nothing to
        /// acquire, so it completes synchronously; on the lockstep path it waits for the semaphore without
        /// blocking a thread.
        /// </summary>
        private async Task<CommandGate> EnterCommandAsync(CancellationToken cancellationToken)
        {
            if (_mux != null) return default;
            await CommandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new CommandGate(CommandLock, alreadyHeld: true);
        }

        /// <summary>Scope object for <see cref="EnterCommand"/>; releases the semaphore if one was taken.</summary>
        internal readonly struct CommandGate : IDisposable
        {
            private readonly SemaphoreSlim? _held;

            internal CommandGate(SemaphoreSlim? toAcquire)
            {
                _held = toAcquire;
                _held?.Wait();
            }

            /// <summary>Wraps a semaphore the caller has already acquired (see <c>EnterCommandAsync</c>).</summary>
            internal CommandGate(SemaphoreSlim held, bool alreadyHeld)
            {
                _held = alreadyHeld ? held : throw new ArgumentException(
                    "Use the single-argument constructor to acquire the semaphore here.", nameof(alreadyHeld));
            }

            public void Dispose() => _held?.Release();
        }

        /// <inheritdoc/>
        public override void Close()
        {
            // Mark closed BEFORE tearing anything down. A running monitor decides whether an exception is a
            // real error or a clean shutdown by asking PollingMonitorEngine.Stopping → !IsOpen, and the
            // multiplexer fails pending requests the instant it is disposed. Faulting first left a window
            // where the monitor thread saw the failure while the connection still looked open and reported it
            // as an error (intermittent PingLocalhostAsyncWithCloseWillNotFail).
            SetClosed();
            // A session that ends in Safe Mode is rolled back by the router: nothing is held any more.
            SafeModeHeld = false;
            _safeModeId = 0;

            // Then the multiplexer: it fails every outstanding request with a clear reason instead of letting
            // callers block until their deadline on a socket that is about to vanish. Disposing the channel
            // is what unblocks the reader thread out of its indefinite read.
            _mux?.Dispose();
            _mux = null;
            _session?.Dispose();
            // Released for GC rather than left dangling; a closed connection must be reopened before any of
            // these are read again (every read path runs only while IsOpen), so the null! here does not
            // claim they stay valid — it just keeps the field's declared type from forcing '?' onto every
            // one of their many use sites elsewhere in this class.
            _session = null!;
            _ops = null!;
            _codec = null!;
            _idResolver = null!;
        }

        // ── Native read overrides ───────────────────────────────────────────────

        // The M2 round trips below are awaited, so the Task-based hooks are the real implementation and the
        // synchronous ones block on them (async is the primitive; nothing is pushed onto a thread-pool
        // thread to look asynchronous). Every await carries ConfigureAwait(false), which is also what keeps
        // the blocking wrappers safe under a UI / ASP.NET-classic SynchronizationContext.
        //
        // That includes the name→id round trips field encoding and record decoding need, which used to be the
        // one blocking hole left in an awaited command. They are still issued by SYNCHRONOUS code —
        // WinboxFieldResolver.EncodeField takes a plain Func resolveRef, and DecodeRecord returns a dictionary
        // — but no longer from this thread: the round trips are hoisted out and awaited first, and the
        // synchronous pass then reads answers that are already in memory.
        //   • encode: EncodeNameValueFieldsAsync runs the encoder twice, the first time with a delegate that
        //     collects the lookups instead of performing them (so the ENCODER decides what needs resolving, not
        //     a second copy of that rule), then resolves them in one batch and encodes for real.
        //   • decode: WinboxRecordCodec.PrimeReferencesAsync fills the per-handler reference-name cache for the
        //     tables the fetched rows actually reference, before the decode loop runs.
        // The blocking lookups remain as the fallback and still serve the synchronous monitor round, which
        // drives its own loop. Keeping EncodeField synchronous is deliberate: making it async would have
        // rippled through ~1000 lines of reverse-engineered encoder that no deterministic test covers.

        /// <inheritdoc/>
        protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => RunPrintAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override async Task<IList<TikRecordSentence>> RunPrintAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            int generation = _sessionGeneration;
            try
            {
                // Gate the M2 channel (see EnterCommand): a no-op when multiplexed, a real lock on the lockstep
                // MAC path, where a concurrent CRUD call or monitor poll would otherwise interleave with ours.
                // Background workers enter the gate themselves and call RunPrintCore directly (not reentrant).
                using (await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
                {
                    // M2 has no field projection: a getall returns every key, and the base trims the rows to
                    // .proplist (TikProplist).
                    return await RunPrintCoreAsync(descriptor, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (TikConnectionSessionClosedException) when (SessionRebuildAllowed)
            {
                // The router dropped an idle session and said nothing; the carrier established that it
                // never took our bytes, so nothing ran and re-running is not a second execution. Only reads
                // come through here — RunAdd/RunNonQuery deliberately do not retry, because "the bytes were
                // never acknowledged" is a statement about the CARRIER, and re-adding a row on the strength
                // of it is a guess this transport must not make.
                await ReopenAsync(generation, cancellationToken).ConfigureAwait(false);
                using (await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
                    return await RunPrintCoreAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rebuilds the M2 channel after the router dropped the session, unless another caller has
        /// already done it — <paramref name="observedGeneration"/> is what the caller saw before its command
        /// failed, so a session that has moved on since means the work is done and this returns.
        /// </summary>
        /// <remarks>
        /// A running streaming monitor does not survive this, and does not have to: the monitor's own poll
        /// traffic is what keeps the session from ever going idle, so a monitor and a dropped-idle session are
        /// not a combination the router produces.
        /// </remarks>
        private async Task ReopenAsync(int observedGeneration, CancellationToken cancellationToken)
        {
            await _reopenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_sessionGeneration != observedGeneration) return;   // someone else rebuilt it already

                TikWireTrace.Emit("wbx.session", TikWireDir.Note,
                    "the router dropped this M2 session while it was idle — reopening and reissuing the read");

                _mux?.Dispose();
                _mux = null;
                try { _session?.Dispose(); } catch { /* the old session is gone anyway */ }
                SetClosed();

                // The same login sequence Open runs, credentials included — a reopen that diverged from the
                // open would be a second way of establishing a session, with its own bugs. It blocks this
                // thread for the handshake, for the reason OpenAsync states: the EC-SRP5 exchange has no
                // awaitable form, and wrapping it in a Task.Run here would only move the block elsewhere.
                OpenChannel();
                _sessionGeneration = observedGeneration + 1;
            }
            finally
            {
                _reopenLock.Release();
            }
        }

        private IList<TikRecordSentence> RunPrintCore(TikCommandDescriptor descriptor)
            => RunPrintCoreAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        private async Task<IList<TikRecordSentence>> RunPrintCoreAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            TakeLabelHints(descriptor);   // here, not in RunPrintAsync: background workers enter the core directly
            EnsureNativeOpen();

            string apiPath = ApiPathOf(descriptor.CommandText);
            int[]? handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
                // A "monitor once" snapshot (e.g. /interface/ethernet/monitor numbers=ether1): the live
                // values are read-only fields on the parent interface record, so a getall + name filter gives
                // the snapshot. Tried before the action-verb path (monitor is not a doit/action cmd).
                var monitorRows = await _monitor.TryRunMonitorAsync(descriptor, cancellationToken).ConfigureAwait(false);
                if (monitorRows != null) return monitorRows;

                // NOT tried here: the parent-handler fallback that RunMonitorAsync uses (ResolveMonitorWindow).
                // For /interface/monitor-traffic it lands on the generic [20,0] interface window, whose field
                // map decodes the monitor's rows under the wrong names (the rows come back without so much as
                // a 'name'). A read that answers with mislabelled data is worse than one that says it cannot
                // do this at all, so this path keeps the actionable "no M2 handler mapping … add a PathAlias"
                // below. Measured while fixing the sync monitors; the async path has the same mis-decode and its test
                // only counts rows, which is why nobody had noticed.

                // The path may be an action verb (e.g. /system/script/run) rather than a table — a .jg
                // doit/action SYS_CMD on the parent handler. Actions perform work and yield no rows, so they
                // belong on the non-query path: reject the read misuse explicitly and guide to
                // ExecuteNonQuery (RunVerb dispatches the SYS_CMD) before reporting "no such command".
                if (IsActionVerbPath(descriptor))
                    throw ActionVerbOnReadPath(descriptor.CommandText);

                // No native handler mapping for this read path. This is OUR gap, not the router's answer —
                // the request was never sent — so it is raised as TikPathNotMappedException (a
                // TikNoSuchCommandException, so existing handling still works, but distinguishable from a
                // router that really refused the command).
                throw PathNotMapped(descriptor.CommandText, apiPath);
            }
            handler = PreferSingletonHealthHandler(apiPath, handler);

            // A standalone action window (.jg doit/action with no record window behind the same handler,
            // e.g. /tool/wol) is an operation, not a table — a getall on it is meaningless. Invoke it and
            // return what the binary API returns for the same command: exactly one empty row. Verified live
            // on RouterOS 7.21.4 — "/tool/wol =mac=…" answers "!re" (no words) then "!done", which is why the
            // shipped ToolWol entity reads it with ExecuteSingleRow rather than ExecuteNonQuery.
            if (_catalog.IsActionOnlyHandler(handler))
                return await RunActionWindowAsync(apiPath, handler, descriptor, cancellationToken).ConfigureAwait(false);

            // A monitor command (/ping, /tool/traceroute) reached through a read method. Checked before the
            // getall below because that is what the router answers with nothing: a monitor window holds no
            // records outside a monitor cycle, so the getall came back empty and ToolPing.Execute reported
            // success with zero rows.
            //
            // Gated on the COMMAND's verb, not on "the resolved window happens to be a query window". The
            // latter reads like the more precise test and is in fact far too wide: /interface resolves to an
            // autorefresh window too, so keying on the window sent every LoadAll of the interface table down
            // the monitor path — which then tried to encode the mapper's 'detail' flag as an M2 request field
            // and threw. Caught by the full native suite, not by any monitor test.
            if (TikMonitorVerbs.Contains(TikPath.Verb(descriptor.CommandText)))
            {
                var monitorSpec = _catalog.GetMonitorByHandler(handler);
                if (monitorSpec != null)
                    return await _monitor.RunMonitorWindowAsync(monitorSpec, apiPath, handler, descriptor, cancellationToken)
                        .ConfigureAwait(false);
            }

            var resolver = MakeResolver(apiPath, handler);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            var numFlags = resolver.BuildNumFlags();

            // Singleton tables (type:'item' window, e.g. /system/resource, /ip/dns) expose a single record
            // read via get-singleton; everything else lists via getall.
            List<Dictionary<int, Tuple<string, object>>> records;
            try
            {
                if (IsSingletonWindow(apiPath, handler))
                {
                    _catalog.GetSingletonCommands(_handlerMap.ResolveDerivedKey(apiPath), out int? getCommand, out _);
                    var one = await _ops.GetSingletonAsync(handler, cancellationToken,
                            WinboxM2Protocol.GetAllFlags | _catalog.GetWindowReadFlags(handler), getCommand)
                        .ConfigureAwait(false);
                    records = (one != null && one.Count > 0)
                        ? new List<Dictionary<int, Tuple<string, object>>> { one }
                        : new List<Dictionary<int, Tuple<string, object>>>();
                }
                else
                {
                    // The window's own refreshfilter/refetchonopen bits (6.x routes send scope and routing-mark
                    // only with them), plus the stats bit on autorefresh windows for the runtime counters.
                    int flags = _catalog.GetReadFlags(handler);
                    records = await _ops.GetAllAsync(handler, cancellationToken, flags).ConfigureAwait(false);
                }
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }

            // Interface subtype paths (e.g. /interface/bridge) share the generic interface handler; keep only the
            // rows whose numeric type field matches the subtype's discriminator (derived from the .jg typevalue).
            if (_handlerMap.TryResolveSubtypeFilter(apiPath, out int typeKey, out int typeValue))
                records = records.Where(r =>
                {
                    // A row with no discriminator genuinely is not of this subtype. A row whose discriminator
                    // is not a number is a different thing: our model of the field is wrong, and dropping the
                    // row silently shortens the result set — a missing row reads exactly like "the router has
                    // none". It is still dropped (including it would be a guess), but it says so.
                    if (!r.TryGetValue(typeKey, out var t) || t.Item2 == null) return false;
                    if (WinboxFieldResolver.TryToInt64(t.Item2, out long tv)) return tv == typeValue;
                    if (TikWireTrace.Enabled)
                        TikWireTrace.Emit("wbx.codec", TikWireDir.Note,
                            $"subtype key {typeKey} of {apiPath} is '{t.Item2}' ({t.Item2.GetType().Name}), " +
                            "not a number — row dropped from the result");
                    return false;
                }).ToList();

            // Decoding a reference field renders the referenced record's NAME, which needs that table read
            // once. Priming it here keeps the decode below synchronous without it blocking this thread on a
            // cache miss (see WinboxRecordCodec.PrimeReferencesAsync).
            await _codec.PrimeReferencesAsync(records, keyToName, keyToField, cancellationToken).ConfigureAwait(false);

            _codec.LearnKindWords(records, keyToField);
            var rows = new List<TikRecordSentence>(records.Count);
            RowsTheApiHides.TryGetValue(apiPath, out var hidden);
            foreach (var rec in records)
            {
                var row = new TikRecordSentence(_codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields, numFlags,
                            resolver.ExtraSpellings));
                if (hidden != null && row.Words.TryGetValue(hidden.Item1, out var hiddenValue)
                    && string.Equals(hiddenValue, hidden.Item2, StringComparison.Ordinal))
                    continue;
                rows.Add(row);
            }

            // Apply Filter parameters (?name=value) in-memory — RouterOS-side filtering is not used here.
            // The filters form a postfix query stack (?#| OR, ?#& AND, ?#! NOT), so they are evaluated as such
            // rather than a naive AND-of-equalities; leftover predicates are implicitly ANDed.
            var filters = descriptor.Parameters
                .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter)
                .ToList();
            if (filters.Count > 0)
                rows = rows.Where(r => TikQueryStack.Matches(r, filters)).ToList();

            // `get` is a narrowing of this same read: native has no get of its own, so the window is read
            // and the row and field are picked here. Without it value-name was ignored entirely and the
            // scalar layer returned whichever field happened to come first — /system/identity/get
            // value-name=name answered "7.24", the RouterOS version. A wrong answer, with no error.
            return TikGetResult.Shape(descriptor, rows);
        }

        /// <summary>
        /// Rows a WinBox window lists that the API's <c>print</c> of the same path leaves out, as (decoded field, value).
        /// </summary>
        /// <remarks>
        /// <c>/ip/route</c>: the routes window lists a route a routing filter rejected (<c>contribution=filtered</c>,
        /// inactive); the API's <c>/ip/route print</c> does not, and shows it only under <c>/routing/route</c>
        /// (<c>filtered=true</c>). Measured on 7.24.4 with two BGP routes rejected by an input filter: the API printed 2
        /// routes, native read 4. <c>/ipv6/route</c> is the same window family but unmeasured, so it is not listed.
        /// </remarks>
        private static readonly Dictionary<string, Tuple<string, string>> RowsTheApiHides =
            new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/ip/route"] = Tuple.Create("contribution", "filtered"),
            };

        // True when the command's last path segment matches a .jg doit/action on the parent handler
        // (e.g. /system/script/run). Detection only — does NOT invoke. Used by the read path to reject the
        // misuse; the non-query path dispatches the SYS_CMD via DispatchActionVerb.
        private bool IsActionVerbPath(TikCommandDescriptor descriptor)
        {
            int[]? handler = _handlerMap.Resolve(TikPath.Parent(descriptor.CommandText));
            if (handler == null) return false;
            var actions = _catalog.GetHandlerActions(handler);
            if (actions == null) return false;
            string verb = TikPath.Verb(descriptor.CommandText);
            foreach (var kv in actions)
                if (ActionMatchesVerb(kv.Key, verb)) return true;
            return false;
        }

        /// <summary>
        /// Invokes the action verb's <c>.jg</c> doit/SYS_CMD on its (already-resolved) parent handler, with the
        /// caller's parameters as the action's arguments and the optional target <c>.id</c>. Throws
        /// <see cref="NotSupportedException"/> when the verb is not a known action on the handler.
        /// </summary>
        /// <remarks>
        /// The arguments are encoded against the ACTION's own field map, not the handler's — see
        /// <see cref="MakeActionResolver"/>. Until then this sent no arguments at all, so
        /// <c>/ip/ipsec/key/rsa/generate-key name=x key-size=2048</c> reached the router as a bare "generate a
        /// key" and produced an unnamed 1024-bit one, reported back as success.
        /// </remarks>
        private async Task DispatchActionVerbAsync(string verb, string apiPath, int[] handler,
            WinboxFieldResolver resolver, TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            int cmd = -1;
            string? actionLabel = null;
            var actions = _catalog.GetHandlerActions(handler);
            if (actions != null)
                foreach (var kv in actions)
                    if (ActionMatchesVerb(kv.Key, verb)) { cmd = kv.Value; actionLabel = kv.Key; break; }
            if (cmd < 0)
            {
                // Say WHAT was looked for and WHAT the handler actually offers. The old message named only
                // the verb, so every reader had to re-derive whether the verb was missing from the catalog
                // or merely spelled differently by its GUI label — the two need opposite fixes (a genuine
                // protocol gap vs. an ActionMatchesVerb/override problem), and only the catalog can tell
                // them apart. '/log/error' turned out to be the first kind: the router's
                // own .jg declares no log-writing action on ANY handler, so WinBox itself cannot write a
                // log line.
                string offered = (actions == null || actions.Count == 0)
                    ? "it declares no actions at all"
                    : "it declares: " + string.Join(", ", actions.Keys.OrderBy(k => k, StringComparer.Ordinal));
                throw new NotSupportedException(
                    $"WinBox native: '{apiPath}' has no action matching the command verb '{verb}' — {offered}. " +
                    "The WinBox protocol invokes actions declared by the router's own .jg catalog, so a verb " +
                    "absent from it cannot be sent over this transport at all. Use a WinboxCli, Api, Rest or " +
                    "other CLI connection for this command.");
            }

            // The record the action targets is named by '.id' or (as RouterOS spells it for menu commands)
            // 'numbers'; both are resolved against the RECORD window, so this keeps the path's own resolver.
            int id = await _writer.ResolveRecordIdAsync(handler, resolver, descriptor, required: false,
                cancellationToken: cancellationToken, alternateIdParam: "numbers").ConfigureAwait(false);

            // Everything else the caller passed is an argument of the action. allowReadOnly/includeFilters
            // mirror RunActionWindow: an action's inputs are often .jg-marked read-only (they are display
            // widgets in the GUI) yet are exactly the values to send, and a caller reaching an action through
            // a read method has had its parameters rewritten to Filter format.
            // cmd and actionLabel are always assigned together in the loop above, and cmd < 0 already
            // returned/threw, so actionLabel is guaranteed non-null here.
            var argResolver = MakeActionResolver(apiPath, handler, actionLabel!);
            var fields = await _writer.EncodeNameValueFieldsAsync(handler, WithoutRecordSelector(descriptor), argResolver,
                skipId: true, cancellationToken, allowReadOnly: true, includeFilters: true).ConfigureAwait(false);

            try { await _ops.InvokeActionAsync(handler, cmd, id, fields, cancellationToken: cancellationToken).ConfigureAwait(false); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
        }

        // The same descriptor without the parameters that NAME the target record rather than carry a value —
        // they were already spent on the .id and are not fields of the action ('numbers' is not a router field
        // at all, so encoding it would fail resolution on something the caller never meant as data).
        private static TikCommandDescriptor WithoutRecordSelector(TikCommandDescriptor descriptor)
        {
            var kept = descriptor.Parameters
                .Where(p => !string.Equals(p.Name, "numbers", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return kept.Count == descriptor.Parameters.Count
                ? descriptor
                : new TikCommandDescriptor(descriptor.CommandText, kept);
        }

        /// <summary>
        /// Invokes a standalone action window (<c>.jg</c> <c>doit</c>/<c>action</c> whose handler backs no
        /// record window) with the caller's parameters as its input fields, and returns no rows — matching
        /// what the API transport returns for the same command.
        /// </summary>
        /// <remarks>
        /// <c>allowReadOnly</c> mirrors the monitor path: an action window's inputs are often <c>.jg</c>-marked
        /// read-only (they are display widgets in the GUI) yet are exactly the values that must be sent —
        /// Wake on LAN's MAC address is one.
        /// </remarks>
        private async Task<IList<TikRecordSentence>> RunActionWindowAsync(
            string apiPath, int[] handler, TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            int cmd = _catalog.GetActionCmd(handler, TikPath.Verb(apiPath), out string? actionLabel);
            if (cmd < 0)
                throw new NotSupportedException(
                    $"WinBox native: '{apiPath}' maps to an action window with no single action to invoke. " +
                    "Use a WinboxCli or Api connection.");

            // cmd and actionLabel are always assigned together; cmd >= 0 guarantees actionLabel non-null here
            // (same contract as the RunActionAsync call site above).
            var resolver = MakeActionResolver(apiPath, handler, actionLabel!);
            var fields = await _writer.EncodeNameValueFieldsAsync(handler, descriptor, resolver,
                skipId: true, cancellationToken: cancellationToken, allowReadOnly: true, includeFilters: true)
                .ConfigureAwait(false);

            try { await _ops.InvokeActionAsync(handler, cmd, id: -1, fields: fields, cancellationToken: cancellationToken).ConfigureAwait(false); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }

            // One empty row — the API's answer shape for these commands (see the call site).
            return new List<TikRecordSentence> { new TikRecordSentence(new Dictionary<string, string>()) };
        }

        // Misuse of a read method (ExecuteList/ExecuteScalar/…) on an action command — guide to ExecuteNonQuery.
        private static NotSupportedException ActionVerbOnReadPath(string commandText)
            => new NotSupportedException(
                $"'{commandText}' is an action command and returns no result set over the WinBox native " +
                "transport. Invoke it with ExecuteNonQuery() instead of ExecuteList()/ExecuteScalar().");

        // True when a .jg action label maps to the RouterOS API verb: exact match, or the label's first
        // hyphen-token equals the verb ("run" ↔ "run-script").
        private static bool ActionMatchesVerb(string normalizedLabel, string verb)
        {
            if (string.Equals(normalizedLabel, verb, StringComparison.OrdinalIgnoreCase)) return true;
            int dash = normalizedLabel.IndexOf('-');
            string first = dash > 0 ? normalizedLabel.Substring(0, dash) : normalizedLabel;
            return string.Equals(first, verb, StringComparison.OrdinalIgnoreCase);
        }

        // ── Writes (set / add / remove / move): WinboxNativeWriter ─────────────

        /// <inheritdoc/>
        protected override string RunAdd(TikCommandDescriptor descriptor)
            => RunAddAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => _writer.RunAddAsync(descriptor, cancellationToken);

        /// <inheritdoc/>
        protected override void RunNonQuery(TikCommandDescriptor descriptor)
            => RunNonQueryAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            => _writer.RunNonQueryAsync(descriptor, cancellationToken);

        // ── Capabilities and menu schema ─────────────────────────────────────────

        /// <summary>
        /// Native WinBox M2 supports streaming monitors (<c>.jg</c> <c>type:'query'</c> / poll-action windows),
        /// so it reports <see cref="TikConnectionCapability.Listen"/> on top of <see cref="TikConnectionCapability.Crud"/>.
        /// </summary>
        /// <remarks>
        /// <para><see cref="TikConnectionCapability.AsyncCommands"/>: the M2 reader loop dispatches replies by
        /// request id, so an awaiting command holds a registration rather than a thread.</para>
        /// <para><see cref="TikConnectionCapability.CancelInFlight"/> means two different things here, and the
        /// stronger one is why it is declared. A <b>streaming window</b> — torch, ping, scan, traceroute,
        /// bandwidth-test — is closed with the window's own <c>cancelcmd</c>, which is what WinBox sends when
        /// you close its window; the router stops. That is a genuine router-side cancel, the same guarantee the
        /// binary API's <c>/cancel tag=N</c> gives, and every streaming window in the router's <c>.jg</c>
        /// catalog declares one (68 of them on 7.23.2, exactly one per <c>startcmd</c>). An <b>ordinary round
        /// trip</b> (getall/set/add) has no cancel verb, so cancelling it frees the caller and drops the
        /// registration while the router finishes the work — which is safe precisely because dispatch is by
        /// request id, so the late reply is identified and discarded rather than handed to the next command.
        /// That weaker half is the same shape REST declares the flag with.</para>
        /// <para><see cref="TikConnectionCapability.StructuredWrites"/>: a write carries each part of a field as its own M2
        /// key — <c>tcp-flags</c>' plain members, negated members and whole-list <c>not</c> flag in one message — so a
        /// value naming only one kind of member leaves nothing of the other behind (verified on 7.24.5: <c>!,syn,!ack</c>
        /// written <c>rst</c> reads back <c>rst</c>, and <c>!fin</c> back <c>!fin</c>; a text <c>set</c> keeps the half it
        /// does not name).</para>
        /// </remarks>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.CancelInFlight
            | TikConnectionCapability.FieldLabels | TikConnectionCapability.MenuSchema
            | TikConnectionCapability.StructuredWrites;

        bool ITikMenuSchemaConnection.ValidateWrites { get => ValidateWrites; set => ValidateWrites = value; }

        /// <summary>
        /// A menu from the <c>.jg</c> catalog: the fields of its window the resolver maps to API names, writable ones as
        /// the arguments, and each field's enum words. No round trip. The names are tik4net's reconstruction, so a missing
        /// one can be a gap in the mapping (<see cref="TikMenuSchemaSource.WinboxCatalog"/>). A path with no WinBox window
        /// throws <see cref="TikPathNotMappedException"/>.
        /// </summary>
        TikMenuSchema ITikMenuSchemaConnection.DescribeMenu(string path, string? winboxLabels)
            => MenuSchemas.GetOrAdd(path, p =>
            {
                EnsureNativeOpen();
                // The hints name the fields: set them for this call alone. Left as the previous command's, a path
                // described after an entity read got that entity's names, and one described first did not.
                var previous = _labelHints.Value;
                _labelHints.Value = WinboxFieldResolver.ParseLabelHints(winboxLabels);
                int[] handler;
                WinboxFieldResolver resolver;
                try { (handler, resolver) = ResolveHandlerAndFields(p); }
                finally { _labelHints.Value = previous; }
                // The names from BuildKeyToApiName, which applies the hints and the overrides as a read does; the
                // field itself (read-only, enum words) by key from BuildKeyToField, whose names are the catalog's.
                var byKey = resolver.BuildKeyToField();
                var named = resolver.BuildKeyToApiName()
                    .Where(kv => kv.Value.Length > 0 && kv.Value != TikSpecialProperties.Id)
                    .Select(kv => new { Name = kv.Value, Field = byKey.TryGetValue(kv.Key, out var f) ? f : null })
                    .ToList();
                var readable = named.Select(n => n.Name).Distinct(StringComparer.Ordinal).ToList();
                var writable = named.Where(n => n.Field == null || !n.Field.ReadOnly)
                    .Select(n => n.Name).Distinct(StringComparer.Ordinal).ToList();
                var words = named
                    .GroupBy(n => n.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key,
                        g => (IReadOnlyList<string>)g.SelectMany(n => n.Field?.EnumMap?.Values ?? Enumerable.Empty<string>())
                            .Distinct(StringComparer.Ordinal).ToList(),
                        StringComparer.Ordinal);
                bool singleton = IsSingletonWindow(p, handler);
                return new TikMenuSchema(p, TikMenuSchemaSource.WinboxCatalog, Array.Empty<string>(),
                    singleton ? null : writable, writable, readable,
                    (verb, argument) => words.TryGetValue(argument, out var w) ? w : Array.Empty<string>());
            }, winboxLabels);

        // ── Safe Mode (system handler [17]) ──────────────────────────────────────
        // Take/release map to the webfig toggleSafeMode() M2 commands. WebFig exposes no in-place
        // unroll/get, so SafeModeUnroll closes the connection, which rolls back (as a terminal does before
        // 7.18), and SafeModeGet reports the client-side held flag.

        private uint _safeModeId;

        /// <inheritdoc/>
        public void SafeModeTake()
            => SafeModeTransition(true, () => _safeModeId = _ops.SafeModeTake());

        /// <inheritdoc/>
        public void SafeModeRelease()
            => SafeModeTransition(false, () =>
            {
                _ops.SafeModeRelease(_safeModeId);
                _safeModeId = 0;
            });

        /// <inheritdoc/>
        /// <remarks>
        /// WinBox has no in-place unroll, so this closes the connection: the router rolls back a session that ends
        /// in Safe Mode, within about 100 ms of the close (measured on 7.24.5, TCP and MAC). <see cref="ITikConnection.IsOpened"/>
        /// is <c>false</c> afterwards; open a new connection to continue.
        /// </remarks>
        public void SafeModeUnroll()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;
            Close();
        }

        /// <inheritdoc/>
        public bool SafeModeGet() => SafeModeHeld;

        // ── Streaming monitor (ExecuteWithCallback / LoadWithCallback): WinboxNativeMonitor ─────────

        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            => _monitor.RunMonitorAsync(descriptor, onRow, onError, onDone);

        // ── IPollingMonitorHost (shared listen/async-list scaffolding lives in PollingMonitorEngine) ──

        /// <inheritdoc/>
        bool IPollingMonitorHost.IsOpen => IsOpened;

        /// <inheritdoc/>
        IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
        {
            // The engine owns no gate, so the snapshot enters it here and calls the ungated core (see RunPrint).
            using (EnterCommand())
                return RunPrintCore(printDescriptor);
        }

        /// <inheritdoc/>
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex)
            => ex is WinboxM2OperationException m
                ? new TikTrapSentenceResult(m.Message, $"0x{m.Code:X}", m.ErrorText)
                : TikTrapSentenceResult.FromException(ex);

        // Query-filter evaluation (postfix stack) is shared with the CLI async-list path — see
        // tik4net.Connection.TikQueryStack.

        /// <summary>
        /// The one place that reports "this transport cannot address that path". It is a CLIENT-side gap —
        /// the router was never asked — so it must never be reported as a router fact ("the package may not
        /// be installed"): the same path very likely works over API/CLI. <see cref="TikPathNotMappedException"/>
        /// carries that distinction in the type, so callers can branch on it instead of matching message text.
        /// </summary>
        private TikPathNotMappedException PathNotMapped(string commandText, string apiPath)
        {
            var cmd = CommandFor(commandText);

            // A path WinBox has no window for is a different answer from a path we failed to map, and the
            // advice is the opposite: there is no alias to add, because there is nothing to point one at.
            // Telling the caller to write one would send them looking for a window that does not exist.
            if (WinboxHandlerMap.NoWinboxWindow.TryGetValue(WinboxHandlerMap.Normalize(apiPath), out string? why))
                return new TikPathNotMappedException(cmd, apiPath,
                    $"WinBox native: '{apiPath}' has no WinBox window. {why}. This is not a mapping gap — " +
                    "the request was never sent, and no PathAlias or PathOverride can help. Use an " +
                    "Api/REST/CLI connection for this path.");

            return new TikPathNotMappedException(cmd, apiPath,
                $"WinBox native: no M2 handler mapping for path '{apiPath}'. " +
                $"This is a gap in tik4net's WinBox path map, not a statement about the router — the request " +
                $"was never sent, and the same path is expected to work over the API and CLI transports. " +
                $"Add a mapping via connection.PathAlias(\"{apiPath}\", \"/winbox/menu/label-path\") " +
                $"(the labels WinBox shows for that window), or connection.PathOverride(\"{apiPath}\", " +
                $"new[]{{maj,min}}) for a raw handler, or use a WinboxCli connection.");
        }

        // ── Handler resolution and M2 errors ───────────────────────────────────

        private (int[] handler, WinboxFieldResolver resolver) ResolveHandlerAndFields(string apiPath)
        {
            int[]? handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
                // Surface an unmapped write path exactly as reads do, so callers get one exception type
                // across read and write — and one that says plainly this is our mapping gap, not a router
                // refusal.
                throw PathNotMapped(apiPath, apiPath);
            }
            // The same board-gated swap the read path makes. Without it a write to /system/health resolved
            // the RouterBOARD map window, which is not a singleton, so the write asked for a record .id the
            // path has no records to supply and failed with "could not resolve record .id ''" — a read that
            // works and a write that cannot is the split this line closes.
            handler = PreferSingletonHealthHandler(apiPath, handler);
            var resolver = MakeResolver(apiPath, handler);
            return (handler, resolver);
        }

        // Translate a native M2 operation error into the matching public tik4net exception, so WinboxNative
        // callers see the same exception types as the API/CLI transports. The router's error string (e.g.
        // "already have such address") is preserved in the message. The numeric M2 code is not a reliable
        // discriminator on its own (RouterOS returns 0xFE0006 'action failed' for "already have such
        // address"), so the error text is matched alongside the well-known codes.
        private TikCommandException TranslateM2Error(WinboxM2OperationException ex, string commandText)
        {
            var trap = new TikTrapSentenceResult(ex.Message, $"0x{ex.Code:X}", ex.ErrorText);
            return TikTrapClassifier.ToException(KindOf(ex, commandText), CommandFor(commandText), trap);
        }

        private static TikTrapKind KindOf(WinboxM2OperationException ex, string commandText)
        {
            string t = (ex.ErrorText ?? string.Empty).ToLowerInvariant();

            if (ex.Code == WinboxM2Protocol.Error.AlreadyExists
                || t.Contains("already have") || t.Contains("already exists"))
                return TikTrapKind.AlreadyHaveSuchItem;

            if (ex.Code == WinboxM2Protocol.Error.ObjectNonexistent || ex.Code == 0xFE0011
                || t.Contains("no such") || t.Contains("not found")
                || t.Contains("does not exist") || t.Contains("doesn't exist"))
                return TikTrapKind.NoSuchItem;

            // 'Not implemented' on a verb that creates, removes or reorders rows is the menu lacking that verb — a
            // single-item menu answering 'add' (RouterOS 6's /interface/ovpn-server/server) — which the API reports as
            // "no such command". Never on a read: there it means we sent the wrong command for the window kind.
            if ((ex.Code == WinboxM2Protocol.Error.NotImplemented || ex.Code == WinboxM2Protocol.Error.NotImplemented2)
                && IsRowSetVerb(TikPath.Verb(commandText)))
                return TikTrapKind.NoSuchCommand;

            // A non-zero M2 status with code + error text is a genuine router-reported error (a trap),
            // not a protocol-shape violation. Surface it as TikCommandTrapException to match the
            // generic-error fallback of the API/CLI/REST transports.
            return TikTrapKind.Generic;
        }

        private static bool IsRowSetVerb(string verb) => verb == "add" || verb == "remove" || verb == "move";

        internal static string? FindParam(TikCommandDescriptor descriptor, string name)
        {
            foreach (var p in descriptor.Parameters)
                if (p.Name == name) return p.Value;
            return null;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void EnsureNativeOpen()
        {
            if (!IsOpened || _ops == null)
                throw NotOpen();
        }

        private IReadOnlyDictionary<string, int> OverridesFor(string apiPath)
        {
            return _fieldOverrides.TryGetValue(WinboxHandlerMap.Normalize(apiPath), out var map)
                ? map
                : new Dictionary<string, int>();
        }

        /// <summary>
        /// Diagnostics for audits (the DefaultValue / applicability probe): how this router's catalog sees the fields of
        /// <paramref name="apiPath"/> — each API name the resolver decodes, its <c>.jg</c> field, and a decoder that
        /// renders one M2 value of that field as the API prints it. <c>null</c> when the path has no handler.
        /// </summary>
        internal IReadOnlyList<(string ApiName, WinboxJgField Field, Func<object, string?> Render)>? DescribeFields(string apiPath)
        {
            int[]? handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
                return null;
            var resolver = MakeResolver(apiPath, handler);
            var names = resolver.BuildKeyToApiName();
            var fields = resolver.BuildKeyToField();
            var codec = _codec;   // the open connection's decoder: DescribeFields needs an opened catalog anyway
            var result = new List<(string, WinboxJgField, Func<object, string?>)>();
            foreach (var pair in fields)
            {
                if (!names.TryGetValue(pair.Key, out string? apiName))
                    continue;
                var field = pair.Value;
                result.Add((apiName, field, value =>
                {
                    var rec = new Dictionary<int, Tuple<string, object>> { [field.Key] = Tuple.Create(field.WireType, value) };
                    return codec.DecodeRecord(rec, names, fields).TryGetValue(apiName, out string? text) ? text : null;
                }));
            }
            return result;
        }

        /// <summary>The <c>.jg</c> label a catalog field was declared with (see <see cref="DescribeFields"/>).</summary>
        internal string? CatalogLabelOf(WinboxJgField field) => _catalog.LabelOf(field);

        /// <summary>
        /// Builds the field resolver for a path, telling it which WINDOW the path resolves to. That matters
        /// wherever several windows share one handler: every interface subtype reads <c>[20,0]</c> but
        /// declares its own field keys, so a resolver that only knew the handler could not tell EoIP's
        /// 'Remote Address' from GRE's.
        /// </summary>
        private WinboxFieldResolver MakeResolver(string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames,
                                       _handlerMap.ResolveDerivedKey(apiPath), labelHints: _labelHints.Value);

        /// <summary>
        /// The resolver for an ACTION invocation: the path's ordinary resolver with the action window's own
        /// arguments laid over it. Without the overlay an argument that shares its label with a record column
        /// resolves to the column — and a read-only column encodes to nothing, so the argument never leaves
        /// the client (see <see cref="WinboxJgCatalog.GetActionFields"/>).
        /// </summary>
        private WinboxFieldResolver MakeActionResolver(string apiPath, int[] handler, string actionLabel)
            => new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames,
                                       _handlerMap.ResolveDerivedKey(apiPath),
                                       _catalog.GetActionFields(handler, actionLabel), _labelHints.Value);

        /// <summary>
        /// Whether the window <paramref name="apiPath"/> resolves to is a singleton (get-singleton /
        /// set-singleton) rather than a record list. Asks the catalog about the WINDOW first and falls back to
        /// the handler-level answer for paths reached by a raw <see cref="PathOverride"/>, where there is no
        /// window to ask about.
        /// </summary>
        private bool IsSingletonWindow(string apiPath, int[] handler)
        {
            // Only when the window the path resolves to is the one actually being read. A caller may hand us
            // a DIFFERENT handler than the path resolves to — PreferSingletonHealthHandler swaps
            // /system/health from its map window [24,29] to the board-gated singleton [24,14] — and answering
            // "that window is a list" about a handler it no longer describes turns a get-singleton into a
            // getall the router rejects (0xFE0003, measured live on 7.23.2).
            string? derivedKey = _handlerMap.ResolveDerivedKey(apiPath);
            if (derivedKey != null && SameHandler(_handlerMap.Resolve(apiPath), handler)
                && _catalog.TryIsSingletonPath(derivedKey, out bool singleton))
                return singleton;
            return _catalog.IsSingletonHandler(handler);
        }

        private static bool SameHandler(int[]? a, int[]? b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Board-gated singleton recovery for /system/health. The WinBox menu has a name/value 'map' window
        // (RouterBOARD, [24,29]) and a hardware-sensor 'item' singleton window (x86, [24,14]) under the same
        // "Health" label; the shipped path alias resolves to the map handler, which answers getall with
        // NotImplemented on x86/CHR (verified live). When the resolved handler is NOT a singleton, prefer the
        // catalog's singleton health window (read via get-singleton) — the one webfig opens on x86. The handler
        // number is read live from the .jg, so this stays version-portable. Returns the original handler
        // unchanged for every other path (and when no singleton health window exists in the catalog).
        private int[] PreferSingletonHealthHandler(string apiPath, int[] handler)
        {
            // handler's declared type is non-nullable, but this defensive check predates nullable annotations
            // and some resolver paths do pass a null handler through; preserved as-is (no behaviour change).
            if (handler == null) return handler!;
            if (_catalog.IsSingletonHandler(handler)) return handler;
            if (!string.Equals(WinboxHandlerMap.Normalize(apiPath), "/system/health", StringComparison.OrdinalIgnoreCase))
                return handler;
            // handler is non-null here (the guard on the first line already returned for a null one).
            return _catalog.FindSingletonHandlerByLeaf("health") ?? handler!;
        }

        // "/interface/print" → "/interface": strips ONLY a trailing read verb segment (print/getall/get),
        // keeping action/deeper paths intact (e.g. "/system/script/run" stays as-is). Distinct from the
        // blind TikPath.Parent, hence its own wrapper.
        internal static string ApiPathOf(string commandText)
        {
            string p = TikPath.Normalize(commandText);
            string verb = TikPath.Verb(p);
            // The CRUD verbs are stripped as well as the read ones. Only the read verbs were, so a write
            // asked about "/ip/upnp/interfaces/set" — a path no alias names — the per-PATH answer missed and
            // the per-HANDLER fallback took over. [28,0] hosts two windows, the UPnP settings `item` and the
            // interfaces `map`, so the fallback said singleton and the write went out as a SET-SINGLETON:
            // accepted with status 0, and the row never changed. Everything else on that handler read
            // correctly, which is why it survived until something wrote to it.
            //
            // Deliberately not a blanket strip: an ACTION path (/system/script/run) is the whole path and
            // has no parent table, and answering about its parent would describe a different window.
            switch (verb)
            {
                case "print":
                case "getall":
                case "get":
                case "set":
                case "add":
                case "remove":
                case "unset":
                case "enable":
                case "disable":
                case "move":
                    return TikPath.Parent(p);
                default:
                    return p;
            }
        }
    }
}
