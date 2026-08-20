using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// </summary>
    /// <remarks>
    /// <para>Full CRUD: <see cref="ITikConnection.CreateCommand()"/> + <c>ExecuteList</c>/<c>LoadAll</c>
    /// route reads through native M2 <c>getall</c>/<c>get-one</c>; <c>Save</c>/<c>Add</c>/<c>Delete</c>/<c>Move</c>
    /// route writes through native <c>set</c>/<c>add</c>/<c>remove</c>/<c>move</c>.</para>
    /// <para>Authentication and the encrypted channel are reused from the shared
    /// <see cref="WinboxM2Session"/> (EC-SRP5 / legacy-MD5, AES-128-CBC). Field keys/types are loaded at
    /// connect time from the <c>.jg</c> catalog the router itself advertises (cached under
    /// <see cref="CatalogCachePath"/>); the apiName↔label mapping is a stable normalizer plus
    /// session overrides.</para>
    /// <para>Streaming monitors are supported via <c>ExecuteAsync</c>/<c>LoadAsync</c> (capability
    /// <see cref="TikConnectionCapability.Listen"/>): <c>.jg</c> <c>type:'query'</c> windows such as
    /// <c>/tool/torch</c>/<c>/tool/profile</c> are polled start→poll→cancel on a background worker.</para>
    /// <para><see cref="ITikConnection.ConnectTimeout"/> bounds the connect handshake and then the
    /// authentication exchange, but not the <c>.jg</c> catalog load that follows them.</para>
    /// </remarks>
    public class WinboxNativeConnection : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost,
        ITikSafeModeConnection
    {
        // Only constructible via TikConnectionSetup/ConnectionFactory (same assembly); the MAC-layer
        // subclass constructor is internal too and calls this one.
        internal WinboxNativeConnection() { }

        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

        /// <summary>
        /// The port the parameterless-port <see cref="Open(string,string,string)"/> overloads forward to the
        /// channel. Defaults to <see cref="DefaultPort"/> (8291); the MAC-layer subclass overrides this instead
        /// of <c>new</c>-shadowing the const (which would resolve on the static reference type — see F12/R11).
        /// </summary>
        private protected virtual int DefaultPortValue => DefaultPort;

        /// <summary>
        /// Directory under which the router's <c>.jg</c> menu catalogs are cached, as
        /// <c>&lt;CatalogCachePath&gt;/plugins/&lt;unique&gt;.jg</c>.
        /// Defaults to <c>%TEMP%/tik4net/</c>. Set before opening to change.
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
        public string CatalogCachePath { get; set; } =
            Path.Combine(Path.GetTempPath(), "tik4net");

        /// <summary>
        /// Number of M2 handlers the <c>.jg</c> catalog supplied for this connection; <c>0</c> until
        /// <see cref="Open(string, string, string)"/> completes.
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

        /// <summary>
        /// Adds a session field override <c>apiName → key</c> for the given API path. Takes priority over
        /// the <c>.jg</c> catalog and the normalizer when resolving fields on that path.
        /// </summary>
        /// <param name="apiPath">API path, e.g. <c>/interface</c>.</param>
        /// <param name="apiName">RouterOS API field name, e.g. <c>mtu</c>.</param>
        /// <param name="key">M2 numeric field key.</param>
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

        /// <summary>
        /// Maps an API path to the path of <b>labels shown in the WinBox GUI menu tree</b> — the window's
        /// breadcrumb plus its record label, lower-cased with spaces as dashes (e.g. <c>/ppp/secret</c> →
        /// <c>PPP ▸ Secrets ▸ PPP Secret</c> = <c>/ppp/secrets/ppp-secret</c>). The numeric handler behind that
        /// window is still read live from the router's version-matched <c>.jg</c> catalog, so this mapping keeps
        /// working across RouterOS upgrades. <b>Prefer this over <see cref="PathOverride"/></b>, which pins a
        /// handler number that may move.
        /// </summary>
        /// <param name="apiPath">RouterOS API path, e.g. <c>/ppp/secret</c>.</param>
        /// <param name="winboxMenuPath">WinBox menu-label path, e.g. <c>/ppp/secrets/ppp-secret</c>.</param>
        public void PathAlias(string apiPath, string winboxMenuPath)
        {
            _handlerMap.AddAlias(apiPath, winboxMenuPath);
        }

        /// <summary>
        /// Adds a session override mapping an API path directly to a WinBox M2 handler array
        /// (e.g. <c>/ppp/secret</c> → <c>[20, 12]</c>). Highest priority — it wins over the catalog, over
        /// <see cref="PathAlias"/> and over subtype filtering, and is taken at face value. Use it only when the
        /// GUI label is not usable (no window in the menu tree, or a wrong/ambiguous label); the numbers are
        /// version-specific, so re-verify them after a RouterOS upgrade.
        /// </summary>
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
        /// Creates the (not-yet-opened) M2 channel this connection rides on. The base uses the TCP
        /// WinBox session (port 8291); <see cref="WinboxNativeMac.WinboxNativeMacConnection"/> overrides it
        /// to ride the MAC-layer channel (UDP 20561).
        /// </summary>
        private protected virtual IWinboxM2Channel CreateChannel() => new WinboxM2Session();

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPortValue, user, password);

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
            // A refused handshake leaves the channel unusable, so the retry builds a fresh one rather
            // than reopening this one — see RouterLoginRetry for why a WinBox login is retried at all.
            Winbox.RouterLoginRetry.Run(() =>
            {
                session = CreateChannel();
                try
                {
                    session.Open(host, port, user, password, ConnectTimeout, ReceiveTimeout);
                }
                catch (TikConnectionLoginException)
                {
                    session.Dispose();
                    throw;
                }
                catch (Exception ex)
                {
                    session.Dispose();
                    throw new TikConnectionLoginException(ex);
                }
            });
            // Run() either assigns session above or throws, so it is always set here.
            InitAfterAuth(session!, host);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, DefaultPortValue, user, password);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password)
        {
            // A Task.Run façade, and knowingly so — the one place on this transport that still is, now that
            // the command surface awaits for real (P2.8). Opening means a blocking connect followed by the
            // EC-SRP5 handshake and the .jg catalog fetch, none of which has an awaitable form:
            // IWinboxM2Channel.Open is synchronous down through WinboxTcpTransport and the crypto, and that
            // is reverse-engineered code with no deterministic coverage. So this holds a thread-pool thread
            // for the duration of the open, which is what D5 says not to do; it is called out here rather
            // than left to be discovered. It does at least keep its promise to the CALLER — control returns
            // immediately — unlike a hook that would claim async and then block the caller.
            //
            // A6 settled it: this stays. AsyncCommands is a claim about COMMANDS, and an open is once per
            // connection; buying it back means an awaitable form of the EC-SRP5 handshake, i.e. rewriting
            // the one part of this transport that has no deterministic coverage, for a thread held once.
            // MAC-Telnet and WinBox CLI open the same way and for the same reason; the capability's own
            // documentation says opening is excluded rather than leaving each site to explain itself.
            return Task.Run(() => Open(host, port, user, password));
        }

        private void InitAfterAuth(IWinboxM2Channel session, string routerKey)
        {
            _session = session;
            // ReceiveTimeout, not ConnectTimeout: this bounds each M2 operation, not the connect phase.
            // (P1.8 left this as ConnectTimeout because per-read socket deadlines made the distinction
            // moot; with per-request deadlines in the multiplexer it is now the value that actually fires.)
            _ops = new WinboxNativeM2Operations(session, ReceiveTimeout);
            // Participate in the shared row-level diagnostics: render each raw M2 request/reply to the
            // OnWriteRow/OnReadRow events (gated so the describe is only built when something listens).
            _ops.OnRequest = msg => { if (RowTracingEnabled) FireWriteRow(M2Message.Describe(msg)); };
            _ops.OnResponse = msg => { if (RowTracingEnabled) FireReadRow(M2Message.Describe(msg)); };
            // Through _ops, not the raw channel: the catalog's mproxy transfer must share the same
            // request-id correlation as every other operation, or a stray frame during it desyncs the
            // channel for the rest of the connection (worst on MAC, which has no stale drain).
            try { _catalog = WinboxJgCatalog.Load(_ops, ResolvePath(CatalogCachePath), routerKey); }
            catch { /* catalog is best-effort; seeds + normalizer still work */ }
            // Never silently: a seeds-only catalog answers "no" to singleton/dynamic-field/monitor lookups,
            // which returns wrong values rather than errors (P2.23). CatalogHandlerCount makes it visible.
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
        /// request id instead of serializing on <c>_cmdLock</c>.
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
        private CommandGate EnterCommand() => new CommandGate(_mux == null ? _cmdLock : null);

        /// <summary>
        /// <see cref="EnterCommand"/> for an awaiting caller: on the multiplexed path there is nothing to
        /// acquire, so it completes synchronously; on the lockstep path it waits for the semaphore without
        /// blocking a thread.
        /// </summary>
        private async Task<CommandGate> EnterCommandAsync(CancellationToken cancellationToken)
        {
            if (_mux != null) return default;
            await _cmdLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new CommandGate(_cmdLock, alreadyHeld: true);
        }

        /// <summary>Scope object for <see cref="EnterCommand"/>; releases the semaphore if one was taken.</summary>
        private readonly struct CommandGate : IDisposable
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
        // synchronous ones block on them (D5: async is the primitive; nothing is pushed onto a thread-pool
        // thread to look asynchronous). Every await carries ConfigureAwait(false), which is also what keeps
        // the blocking wrappers safe under a UI / ASP.NET-classic SynchronizationContext.
        //
        // That includes the name→id round trips field encoding and record decoding need, which used to be the
        // one blocking hole left in an awaited command (P2.8). They are still issued by SYNCHRONOUS code —
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
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            => RunPrintAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        internal override async Task<IList<TikRecordSentence>> RunPrintAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            int generation = _sessionGeneration;
            try
            {
                // Gate the M2 channel (see EnterCommand): a no-op when multiplexed, a real lock on the lockstep
                // MAC path, where a concurrent CRUD call or monitor poll would otherwise interleave with ours.
                // Background workers enter the gate themselves and call RunPrintCore directly (not reentrant).
                using (await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
                    return await RunPrintCoreAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
            catch (TikConnectionSessionClosedException) when (ReconnectAllowed)
            {
                // G1. The router dropped an idle session and said nothing; the carrier established that it
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
        /// Whether a dropped session may be rebuilt underneath the caller. Safe Mode is the one case where it
        /// must not be: dropping the session is precisely what rolls Safe Mode's changes back, so opening a
        /// new one would hide the event the caller asked to be protected by — and the new session would not
        /// hold Safe Mode either. Mirrors <c>MacTelnetConnection.ReconnectAllowed</c>.
        /// </summary>
        private bool ReconnectAllowed => !SafeModeHeld;

        /// <summary>
        /// Rebuilds the M2 channel after the router dropped the session (G1), unless another caller has
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
            EnsureNativeOpen();

            string apiPath = ApiPathOf(descriptor.CommandText);
            int[]? handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
                // A "monitor once" snapshot (e.g. /interface/ethernet/monitor numbers=ether1): the live
                // values are read-only fields on the parent interface record, so a getall + name filter gives
                // the snapshot. Tried before the action-verb path (monitor is not a doit/action cmd).
                var monitorRows = await TryRunMonitorAsync(descriptor, cancellationToken).ConfigureAwait(false);
                if (monitorRows != null) return monitorRows;

                // NOT tried here: the parent-handler fallback that RunMonitorAsync uses (ResolveMonitorWindow).
                // For /interface/monitor-traffic it lands on the generic [20,0] interface window, whose field
                // map decodes the monitor's rows under the wrong names (the rows come back without so much as
                // a 'name'). A read that answers with mislabelled data is worse than one that says it cannot
                // do this at all, so this path keeps the actionable "no M2 handler mapping … add a PathAlias"
                // below. Measured while fixing P2.51; the async path has the same mis-decode and its test only
                // counts rows, which is why nobody had noticed.

                // The path may be an action verb (e.g. /system/script/run) rather than a table — a .jg
                // doit/action SYS_CMD on the parent handler. Actions perform work and yield no rows, so they
                // belong on the non-query path: reject the read misuse explicitly (R7) and guide to
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
            // success with zero rows (P2.51).
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
                    return await RunMonitorWindowAsync(monitorSpec, apiPath, handler, descriptor, cancellationToken)
                        .ConfigureAwait(false);
            }

            var resolver = MakeResolver(apiPath, handler);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();

            // Singleton tables (type:'item' window, e.g. /system/resource, /ip/dns) expose a single record
            // read via get-singleton; everything else lists via getall.
            List<Dictionary<int, Tuple<string, object>>> records;
            try
            {
                if (IsSingletonWindow(apiPath, handler))
                {
                    var one = await _ops.GetSingletonAsync(handler, cancellationToken).ConfigureAwait(false);
                    records = (one != null && one.Count > 0)
                        ? new List<Dictionary<int, Tuple<string, object>>> { one }
                        : new List<Dictionary<int, Tuple<string, object>>>();
                }
                else
                {
                    // autorefresh windows (e.g. firewall rules) carry runtime counters the base flag omits;
                    // OR the stats bit so getall returns bytes/packets, matching RouterOS `print`.
                    int flags = WinboxM2Protocol.GetAllFlags
                        | (_catalog.HasDynamicFields(handler) ? WinboxM2Protocol.GetAllStatsFlag : 0);
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
                    // none" (P2.25). It is still dropped (including it would be a guess), but it says so.
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

            var rows = new List<TikRecordSentence>(records.Count);
            foreach (var rec in records)
                rows.Add(new TikRecordSentence(_codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields)));

            // Apply Filter parameters (?name=value) in-memory — RouterOS-side filtering is not used here.
            // The filters form a postfix query stack (?#| OR, ?#& AND, ?#! NOT), so they are evaluated as such
            // rather than a naive AND-of-equalities; leftover predicates are implicitly ANDed.
            var filters = descriptor.Parameters
                .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter)
                .ToList();
            if (filters.Count > 0)
                rows = rows.Where(r => TikQueryStack.Matches(r, filters)).ToList();

            return rows;
        }

        // Attempts a "monitor [once]" snapshot (e.g. /interface/ethernet/monitor numbers=ether1). The monitored
        // values (rate, link status, auto-negotiation, full-duplex) are read-only fields on the parent
        // interface record — webfig surfaces them as a Status tab of the [20,0] window, not a separate handler.
        // A getall on the parent handler filtered to the named interface yields the same single snapshot the
        // RouterOS "monitor once" returns. Returns false (fall through) when this is not a monitor path.
        /// <summary>
        /// True when the path is a "monitor once"-style snapshot served from the parent record rather than
        /// from a monitor window of its own — the async counterpart of the <see cref="TryRunMonitorAsync"/>
        /// test, kept next to it so the two stay in step.
        /// </summary>
        private bool IsSnapshotMonitorPath(string commandText)
            => IsSnapshotMonitorVerb(TikPath.Verb(commandText))
               && _handlerMap.Resolve(ApiPathOf(commandText)) == null
               && _handlerMap.Resolve(TikPath.Parent(commandText)) != null;

        // The monitor verbs whose readings are fields of the parent record rather than a window of their own.
        private static bool IsSnapshotMonitorVerb(string verb)
            => string.Equals(verb, "monitor", StringComparison.OrdinalIgnoreCase)
               || string.Equals(verb, "monitor-traffic", StringComparison.OrdinalIgnoreCase);

        /// <returns>
        /// The snapshot rows, or <c>null</c> when this is not a snapshot-monitor path and the caller should
        /// fall through. A null return rather than a <c>bool</c> + <c>out</c> because an <c>out</c> parameter
        /// cannot be written across an <c>await</c>.
        /// </returns>
        private async Task<IList<TikRecordSentence>?> TryRunMonitorAsync(
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            if (!IsSnapshotMonitorVerb(TikPath.Verb(descriptor.CommandText)))
                return null;

            string parentPath = TikPath.Parent(descriptor.CommandText);
            int[]? handler = _handlerMap.Resolve(parentPath);
            if (handler == null) return null;

            // The interface is named via 'numbers' (RouterOS monitor convention), or 'interface'/'.id'.
            string? target = FindParam(descriptor, "numbers")
                ?? FindParam(descriptor, "interface")
                ?? FindParam(descriptor, TikSpecialProperties.Id);
            if (string.IsNullOrEmpty(target)) return null;

            // Keys and overrides come from the PARENT window (that is where the values live), but the field
            // NAMES come from the monitor path's own alias set — the same record is called 'rx' in the
            // interface list and 'rx-bits-per-second' by /interface/monitor-traffic, and a caller asking for
            // the monitor must get the monitor's names.
            var resolver = new WinboxFieldResolver(ApiPathOf(descriptor.CommandText), handler, _catalog,
                OverridesFor(parentPath), _useGuiNames, _handlerMap.ResolveDerivedKey(parentPath));
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            int flags = WinboxM2Protocol.GetAllFlags
                | (_catalog.HasDynamicFields(handler) ? WinboxM2Protocol.GetAllStatsFlag : 0);

            List<Dictionary<int, Tuple<string, object>>> records;
            try { records = await _ops.GetAllAsync(handler, cancellationToken, flags).ConfigureAwait(false); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }

            await _codec.PrimeReferencesAsync(records, keyToName, keyToField, cancellationToken).ConfigureAwait(false);

            var result = new List<TikRecordSentence>();
            foreach (var rec in records)
            {
                var decoded = _codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields);
                if (decoded.TryGetValue("name", out var nm) && string.Equals(nm, target, StringComparison.Ordinal))
                {
                    result.Add(new TikRecordSentence(decoded));
                    break;
                }
            }
            return result;
        }

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
                // them apart. See P2.48, where '/log/error' turned out to be the first kind: the router's
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
            int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: false,
                cancellationToken: cancellationToken, alternateIdParam: "numbers").ConfigureAwait(false);

            // Everything else the caller passed is an argument of the action. allowReadOnly/includeFilters
            // mirror RunActionWindow: an action's inputs are often .jg-marked read-only (they are display
            // widgets in the GUI) yet are exactly the values to send, and a caller reaching an action through
            // a read method has had its parameters rewritten to Filter format.
            // cmd and actionLabel are always assigned together in the loop above, and cmd < 0 already
            // returned/threw, so actionLabel is guaranteed non-null here.
            var argResolver = MakeActionResolver(apiPath, handler, actionLabel!);
            var fields = await EncodeNameValueFieldsAsync(handler, WithoutRecordSelector(descriptor), argResolver,
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
            int cmd = _catalog.GetSoleActionCmd(handler, out string? actionLabel);
            if (cmd < 0)
                throw new NotSupportedException(
                    $"WinBox native: '{apiPath}' maps to an action window with no single action to invoke. " +
                    "Use a WinboxCli or Api connection.");

            // cmd and actionLabel are always assigned together; cmd >= 0 guarantees actionLabel non-null here
            // (same contract as the RunActionAsync call site above).
            var resolver = MakeActionResolver(apiPath, handler, actionLabel!);
            var fields = await EncodeNameValueFieldsAsync(handler, descriptor, resolver,
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

        // ── Writes — Phase F2 (set / add / remove / move) ──────────────────────

        /// <inheritdoc/>
        internal override string RunAdd(TikCommandDescriptor descriptor)
            => RunAddAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        internal override async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureNativeOpen();
            // descriptor.CommandText is "/path/add"; the resolution path is the parent.
            string apiPath = TikPath.Parent(descriptor.CommandText);

            try
            {
                using (await EnterCommandAsync(cancellationToken).ConfigureAwait(false))   // see RunPrint
                {
                    var (handler, resolver) = ResolveHandlerAndFields(apiPath);
                    var fields = await EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true,
                        cancellationToken).ConfigureAwait(false);
                    int newId = await _ops.AddAsync(handler, fields, cancellationToken).ConfigureAwait(false);
                    // RunAddAsync's declared return type is non-nullable, matching RunAdd's contract across
                    // every transport, but a failed add (newId < 0) genuinely yields null here (see the report).
                    return (newId >= 0 ? "*" + ((uint)newId).ToString("X") : null)!;
                }
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
        }

        /// <inheritdoc/>
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
            => RunNonQueryAsync(descriptor, CancellationToken.None).GetAwaiter().GetResult();

        /// <inheritdoc/>
        internal override async Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureNativeOpen();
            // Try the whole command text as a standalone action window first (e.g. /tool/wol). Splitting it
            // into parent+verb would resolve '/tool' and look for a 'wol' action there, which is not how
            // these windows are addressed — the path IS the operation.
            // (ExecuteNonQuery on such a path is legitimate too — the caller simply discards the row the read
            // path would have produced.)
            int[]? actionHandler = _handlerMap.Resolve(ApiPathOf(descriptor.CommandText));
            if (_catalog.IsActionOnlyHandler(actionHandler))
            {
                using (await EnterCommandAsync(cancellationToken).ConfigureAwait(false))
                    // actionHandler is non-null here: IsActionOnlyHandler answers false for a null handler.
                    await RunActionWindowAsync(ApiPathOf(descriptor.CommandText), actionHandler!, descriptor, cancellationToken)
                        .ConfigureAwait(false);
                return;
            }

            string verb = TikPath.Verb(descriptor.CommandText);
            string apiPath = TikPath.Parent(descriptor.CommandText);

            try
            {
                using (await EnterCommandAsync(cancellationToken).ConfigureAwait(false))   // see RunPrint
                {
                    var (handler, resolver) = ResolveHandlerAndFields(apiPath);
                    await RunVerbAsync(verb, apiPath, handler, resolver, descriptor, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
        }

        private async Task RunVerbAsync(string verb, string apiPath, int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            switch (verb)
            {
                case "add":
                {
                    // /path/add invoked via ExecuteNonQuery (the new id, if any, is discarded here).
                    var fields = await EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true,
                        cancellationToken).ConfigureAwait(false);
                    await _ops.AddAsync(handler, fields, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "set":
                {
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "enable":
                case "disable":
                {
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => Task.FromResult(resolver.EncodeField("disabled", verb == "disable" ? "true" : "false")),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "unset":
                {
                    // unset names its target in a PSEUDO-parameter — '=value-name=<field>' — exactly as the
                    // binary API spells it; the field itself carries no value. Encoding the parameter list
                    // verbatim therefore asked the resolver for an M2 key for a field called 'value-name'
                    // and threw WinboxFieldResolutionException. Translate instead: unset = set the named
                    // field back to empty/default.
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => EncodeUnsetFieldsAsync(handler, descriptor, resolver, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "comment":
                {
                    // A real RouterOS menu command, and on the M2 layer simply a write of the comment
                    // field — WinBox has no separate comment operation. Without this it reached
                    // DispatchActionVerb and threw "not an action verb".
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "remove":
                {
                    int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                        cancellationToken).ConfigureAwait(false);
                    await _ops.RemoveAsync(handler, id, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "move":
                {
                    int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                        cancellationToken, alternateIdParam: "numbers").ConfigureAwait(false);
                    int dest = await ResolveMoveDestAsync(handler, resolver, descriptor, cancellationToken)
                        .ConfigureAwait(false);
                    await _ops.MoveAsync(handler, id, dest, cancellationToken).ConfigureAwait(false);
                    break;
                }
                default:
                    // Action verb (e.g. /system/script/run → a .jg doit/SYS_CMD on this handler), invoked via
                    // ExecuteNonQuery. Dispatch it fire-and-forget; throws NotSupported if it is not an action.
                    await DispatchActionVerbAsync(verb, apiPath, handler, resolver, descriptor, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }


        // ── Streaming monitor (ExecuteAsync / LoadAsync) ─────────────────────────

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
        /// </remarks>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.CancelInFlight;

        // ── Safe Mode (system handler [17]) ──────────────────────────────────────
        // Take/release map to the webfig toggleSafeMode() M2 commands. WebFig exposes no in-place
        // unroll/get, so SafeModeUnroll throws (drop the connection to roll back) and SafeModeGet
        // reports the client-side held flag.

        private uint _safeModeId;

        /// <inheritdoc/>
        public void SafeModeTake()
        {
            EnsureOpened();
            if (SafeModeHeld) return;
            _safeModeId = _ops.SafeModeTake();
            SafeModeHeld = true;
        }

        /// <inheritdoc/>
        public void SafeModeRelease()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;
            _ops.SafeModeRelease(_safeModeId);
            SafeModeHeld = false;
            _safeModeId = 0;
        }

        /// <inheritdoc/>
        public void SafeModeUnroll()
            => throw new NotSupportedException(
                "Native WinBox exposes only take/release for Safe Mode (no in-place unroll). " +
                "To roll back, close the connection without calling SafeModeRelease — RouterOS reverts " +
                "the changes automatically. For an explicit unroll use the binary API or a CLI transport.");

        /// <inheritdoc/>
        public bool SafeModeGet() => SafeModeHeld;

        /// <summary>
        /// Runs a streaming-monitor command (e.g. <c>/tool/torch</c>, <c>/tool/profile</c>) on a background
        /// worker that polls the router every <c>autorefresh</c> ms over the normal M2 channel — start → poll →
        /// cancel (webfig <c>ObjectQuery</c>; see <c>Docs/winbox-native-m2-protocol.md</c> §20). Each polled record
        /// is decoded to API field names and pushed to <paramref name="onRow"/>; <paramref name="onDone"/> fires
        /// when the worker stops (cancelled, the router's "finished" flag, or an error — reported via
        /// <paramref name="onError"/>). Request parameters (NameValue) are encoded as the monitor's request fields.
        /// </summary>
        /// <remarks>The worker owns the M2 channel while polling; issuing concurrent CRUD on the same connection
        /// from another thread while a native monitor is active is not supported (the transport is request/reply).</remarks>
        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            EnsureNativeOpen();
            string verb = TikPath.Verb(descriptor.CommandText);

            // /path/listen — RouterOS pushes add/change/delete deltas over the API. WinBox M2 has no server
            // push, so webfig (and we) emulate it the way it polls live config tables: getall on a timer and
            // diff snapshots by .id (see Docs/winbox-native-m2-protocol.md §20). Deleted rows are surfaced as a
            // synthetic ".dead=true" record so the O/R LoadListenAsync handler routes them to onDeleted.
            if (verb == "listen")
            {
                string listPath = TikPath.Parent(descriptor.CommandText);
                var printDescriptor = new TikCommandDescriptor(listPath + "/print", descriptor.Parameters);
                // Diff config fields only — runtime counters (ro:1: rx-byte, link-downs, …) tick every poll and
                // would otherwise make every row look "changed", whereas RouterOS listen emits on real changes.
                var volatileFields = ReadOnlyFieldNames(listPath);
                return PollingMonitorEngine.StartWorker("winbox-native-listen",
                    handle => PollingMonitorEngine.ListenLoop(this, printDescriptor, volatileFields, 1000, handle, onRow, onError, onDone));
            }

            // /path/print (LoadAsync) — a one-shot async list, not a streaming window: run the print off the
            // calling thread, emit each row, then complete. No monitor cycle is involved.
            if (verb == "print" || verb == "getall")
            {
                return PollingMonitorEngine.StartWorker("winbox-native-asynclist",
                    handle => PollingMonitorEngine.AsyncListOnce(this, descriptor, handle, onRow, onError, onDone));
            }

            // A snapshot monitor with no window of its own (/interface/monitor-traffic,
            // /interface/ethernet/monitor): its values are read-only fields of the PARENT record, which the
            // synchronous path reads with a getall + name filter (TryRunMonitor). Async re-reads that same
            // snapshot on a timer instead of resolving a monitor window, because the parent-handler fallback
            // below lands on the generic [20,0] interface window — a real monitor window, but the wrong one:
            // it started a monitor cycle whose records decoded to EMPTY rows, and the caller got two blank
            // records per second that its row count read as success (P2.52).
            if (IsSnapshotMonitorPath(descriptor.CommandText))
                return PollingMonitorEngine.StartWorker("winbox-native-monitor-snapshot",
                    handle => PollingMonitorEngine.SnapshotLoop(this, descriptor, 1000, handle, onRow, onError, onDone));

            // Otherwise a streaming-monitor window (/tool/torch, /tool/profile, …).
            WinboxMonitorSpec? spec = ResolveMonitorWindow(descriptor.CommandText, out string apiPath, out int[]? handler);
            if (spec == null)
            {
                var cmd = new TikGenericCommand(this, descriptor.CommandText);
                throw new TikPathNotMappedException(cmd, apiPath,
                    $"WinBox native: '{descriptor.CommandText}' is not a streaming-monitor window in the .jg catalog. " +
                    $"Add a PathOverride(\"{apiPath}\", new[]{{maj,min}}) to a monitor handler, or use a CLI transport.");
            }

            // handler is non-null here: ResolveMonitorWindow assigns it whenever it returns a spec,
            // and the no-spec case threw above.
            var resolver = MakeResolver(apiPath, handler!);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            return PollingMonitorEngine.StartWorker("winbox-native-monitor",
                handle => MonitorLoop(spec, descriptor, resolver, keyToName, keyToField, handle, onRow, onError, onDone));
        }

        /// <summary>
        /// Finds the streaming-monitor window a command path names, or <c>null</c> when it names none. The
        /// monitor path is either the command path itself (<c>/ping</c>, <c>/interface/monitor-traffic</c>) or
        /// the parent of a trailing verb, so both are tried — in that order.
        /// </summary>
        /// <remarks>
        /// Shared by the async and the synchronous monitor paths deliberately: they must agree on WHICH window
        /// a command means, or the same command answers differently depending on the method used to call it —
        /// a defect nothing in the result can reveal.
        /// </remarks>
        private WinboxMonitorSpec? ResolveMonitorWindow(string commandText, out string apiPath, out int[]? handler)
        {
            apiPath = ApiPathOf(commandText);
            handler = _handlerMap.Resolve(apiPath);
            WinboxMonitorSpec? spec = _catalog.GetMonitorByHandler(handler);
            if (spec != null) return spec;

            string parent = TikPath.Parent(commandText);
            int[]? ph = _handlerMap.Resolve(parent);
            var pspec = _catalog.GetMonitorByHandler(ph);
            if (pspec == null) return null;

            apiPath = parent;
            handler = ph;
            return pspec;
        }

        /// <summary>
        /// Runs a monitor window to completion on the CALLING thread and returns everything it produced — the
        /// read-method (<c>ExecuteList</c>/<c>LoadList</c>) counterpart of <see cref="MonitorLoop"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Before this existed, a synchronous <c>/ping</c> over native fell through to the generic getall on the
        /// monitor handler, which the router answers with no records at all — so <c>ToolPing.Execute</c>
        /// returned an empty list and the caller was told the ping had succeeded with nothing to report
        ///. A monitor window is not a table: its rows only exist while a monitor cycle is running.
        /// </para>
        /// <para>
        /// "To completion" means: until the router sets Finished (a self-terminating command — <c>ping
        /// count=N</c>, <c>traceroute</c>), or until the first pass ends without it (a continuous window —
        /// <c>monitor-traffic</c>, <c>torch</c> — whose pass IS one snapshot). That is the same rule the CLI
        /// transports get from the <c>once</c>/<c>count=1</c> snapshot modifier, and it matches what the binary
        /// API returns for the same command. A monitor the caller never bounded (a <c>/ping</c> with no
        /// <c>count</c>) blocks until the connection is closed — exactly as it does on the binary API, which
        /// waits for a <c>!done</c> that never comes.
        /// </para>
        /// <para>
        /// This is also the one place on the native transport where cancelling a running command is a
        /// router-side stop rather than a local abandon: the <c>finally</c> sends the window's
        /// <c>.jg</c>-declared <c>cancelcmd</c>, which is what WinBox itself sends when its torch/ping/scan
        /// window is closed, and every streaming window in the 7.23.2 catalog declares one (68 of them,
        /// exactly one per <c>startcmd</c>). That is what <see cref="TikConnectionCapability.CancelInFlight"/>
        /// means on this transport — see the remarks on <see cref="Capabilities"/>.
        /// </para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>> RunMonitorWindowAsync(
            WinboxMonitorSpec spec, string apiPath, int[] handler, TikCommandDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            var resolver = MakeResolver(apiPath, handler);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();

            var rows = new List<TikRecordSentence>();
            uint? id = null;
            bool started = false;
            try
            {
                // allowReadOnly / includeFilters: a window's input fields are .jg-marked read-only (they are
                // display widgets in the GUI) yet are the monitor's request inputs, and the read path has
                // already rewritten the caller's parameters to Filter format — the same pair of exceptions
                // RunActionWindow makes, for the same reasons.
                var requestFields = await EncodeNameValueFieldsAsync(
                    spec.Handler, descriptor, resolver, skipId: true, cancellationToken, allowReadOnly: true,
                    includeFilters: true, skipSnapshotModifier: true).ConfigureAwait(false);
                id = await _ops.StartMonitorAsync(spec.Handler, spec.StartCmd, requestFields, cancellationToken)
                    .ConfigureAwait(false);
                started = true;

                // A self-terminating command's answer is everything it produces up to its own end, so a pass
                // that ends without Finished means "still working", not "that was the snapshot": a traceroute
                // republishes a longer table every autorefresh until the last hop is probed. A continuous
                // window has no end to wait for, so for those the first pass IS the answer.
                bool waitForDone = TikMonitorVerbs.SelfTerminating(TikPath.Verb(descriptor.CommandText));
                var deadline = DateTime.UtcNow.AddMilliseconds(ReceiveTimeout);

                WinboxM2Continuation? continuation = null;
                while (true)
                {
                    // contToken's parameter type isn't annotated nullable — PollMonitorRoundAsync lives in
                    // Winbox/, out of scope here — but null is its documented first-round value.
                    var (records, done, next) = await _ops
                        .PollMonitorRoundAsync(spec.Handler, spec.PollCmd, id, spec.IsQuery, continuation!, cancellationToken)
                        .ConfigureAwait(false);
                    continuation = next;

                    await _codec.PrimeReferencesAsync(records, keyToName, keyToField, cancellationToken).ConfigureAwait(false);
                    foreach (var rec in records)
                        rows.Add(new TikRecordSentence(_codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields)));

                    if (done) break;                    // router set Finished: the command is over
                    if (continuation != null) continue; // mid-pass: keep reading this one
                    if (!waitForDone) break;            // continuous window: one pass is the snapshot

                    // Bounded so a command that never finishes fails like any other unfinished read instead of
                    // hanging on this thread forever.
                    if (DateTime.UtcNow >= deadline)
                        throw new TikConnectionReceiveTimeoutException(ReceiveTimeout,
                            $"WinBox native: '{descriptor.CommandText}' produced {rows.Count} row(s) but never " +
                            $"reported itself finished within {ReceiveTimeout} ms.");
                    await Task.Delay(Math.Max(100, spec.AutorefreshMs), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
            finally
            {
                if (started && IsOpened)
                {
                    // Deliberately NOT passed the caller's token: this is the request that tells the router to
                    // stop, so cancelling it would leave the window running on the router — the exact failure
                    // the capability claims not to have. Best-effort otherwise (the monitor may have ended).
                    try { _ops.CancelMonitor(spec.Handler, spec.CancelCmd, id); }
                    catch { /* best-effort — the rows above are the result */ }
                }
            }
            return rows;
        }

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

        // The monitor worker: encode request fields → start → poll loop (emit decoded rows, sleep autorefresh,
        // honour cancel/finished) → cancel. Request-field encoding runs here (not on the caller) so a runtime
        // resolution failure (e.g. interface not found) surfaces async via onError, matching the API transport,
        // instead of throwing synchronously out of ExecuteAsync. onDone always fires exactly once.
        private void MonitorLoop(WinboxMonitorSpec spec, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            IReadOnlyDictionary<int, string> keyToName, IReadOnlyDictionary<int, WinboxJgField> keyToField,
            TikMonitorHandle handle, Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            uint? id = null;
            bool started = false;
            try
            {
                using (EnterCommand())
                {
                    // Encode the caller's NameValue parameters as the monitor's request fields (interface, cpu, …).
                    // allowReadOnly: a window's input fields are often .jg-marked ro (display) yet are the
                    // monitor's legitimate request inputs and must be sent (e.g. ping 'address').
                    var requestFields = EncodeNameValueFields(
                        spec.Handler, descriptor, resolver, skipId: true, _idResolver.ResolveReference,
                        allowReadOnly: true);
                    id = _ops.StartMonitor(spec.Handler, spec.StartCmd, requestFields);
                    started = true;
                }

                // The pass is driven here rather than inside the operations layer, because this is where the
                // cancel handle and the row callback live — and because a pass is unbounded in time (see
                // PollMonitorRound). `continuation != null` means the router is still mid-pass, so the next
                // round goes out immediately; the autorefresh sleep applies only BETWEEN passes.
                WinboxM2Continuation? continuation = null;
                while (!handle.CancelRequested)
                {
                    bool done;
                    List<Dictionary<int, Tuple<string, object>>> records;
                    // Gated per ROUND, not per pass: on a multiplexed connection this is a no-op, but a
                    // 30-second ping must not hold the lockstep gate for its whole duration (design §2).
                    // contToken's parameter type isn't annotated nullable (PollMonitorRound is out of scope
                    // here), but null is its documented first-round value.
                    using (EnterCommand())
                        (records, done, continuation) =
                            _ops.PollMonitorRound(spec.Handler, spec.PollCmd, id, spec.IsQuery, continuation!);

                    // Emitted per round, so a streaming window (ping, traceroute, torch) reaches the caller
                    // as the router produces it instead of in a lump when the pass ends.
                    foreach (var rec in records)
                        onRow?.Invoke(new TikRecordSentence(_codec.DecodeRecord(rec, keyToName, keyToField, resolver.DerivedBoolFields)));

                    if (done) break;              // router set Finished: the operation is over for good
                    if (continuation != null) continue;   // same pass, next record — no sleep

                    // The pass ended without Finished: an autorefresh snapshot window (Torch, Scan, …) whose
                    // getall lists what exists right now. Wait the interval, then start a fresh pass.
                    // Sleep in short slices so Cancel stays responsive.
                    int slept = 0, interval = Math.Max(100, spec.AutorefreshMs);
                    while (slept < interval && !handle.CancelRequested) { Thread.Sleep(50); slept += 50; }
                }
            }
            catch (WinboxM2OperationException ex)
            {
                if (!PollingMonitorEngine.Stopping(this, handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message, $"0x{ex.Code:X}", ex.ErrorText));
            }
            catch (Exception ex)
            {
                if (!PollingMonitorEngine.Stopping(this, handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally
            {
                if (started && IsOpened)
                {
                    // (The gate is entered inside the try: the previous shape released a semaphore it had
                    // not necessarily acquired when the Wait itself threw during teardown.)
                    try
                    {
                        using (EnterCommand())
                            _ops.CancelMonitor(spec.Handler, spec.CancelCmd, id);
                    }
                    catch { /* best-effort */ }
                }
                onDone?.Invoke();
            }
        }

        // The set of read-only (ro:1) field names for a table's handler — the volatile runtime fields a listen
        // diff must ignore. Empty when the path has no handler/catalog entry (then all fields are compared).
        private HashSet<string> ReadOnlyFieldNames(string apiPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[]? handler = _handlerMap.Resolve(apiPath);
            if (handler == null) return set;
            var resolver = MakeResolver(apiPath, handler);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            foreach (var kv in keyToField)
                if (kv.Value != null && kv.Value.ReadOnly && keyToName.TryGetValue(kv.Key, out var n))
                    set.Add(n);
            return set;
        }

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
            var cmd = new TikGenericCommand(this, commandText);

            // A path WinBox has no window for is a different answer from a path we failed to map, and the
            // advice is the opposite: there is no alias to add, because there is nothing to point one at.
            // Telling the caller to write one would send them looking for a window that does not exist.
            if (WinboxHandlerMap.NoWinboxWindow.TryGetValue(WinboxHandlerMap.Normalize(apiPath), out string why))
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

        // ── Write helpers ──────────────────────────────────────────────────────

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
            var cmd = new TikGenericCommand(this, commandText);
            var trap = new TikTrapSentenceResult(ex.Message, $"0x{ex.Code:X}", ex.ErrorText);
            string t = (ex.ErrorText ?? string.Empty).ToLowerInvariant();

            if (ex.Code == WinboxM2Protocol.Error.AlreadyExists
                || t.Contains("already have") || t.Contains("already exists"))
                return new TikAlreadyHaveSuchItemException(cmd, trap);

            if (ex.Code == WinboxM2Protocol.Error.ObjectNonexistent || ex.Code == 0xFE0011
                || t.Contains("no such") || t.Contains("not found")
                || t.Contains("does not exist") || t.Contains("doesn't exist"))
                return new TikNoSuchItemException(cmd, trap);

            // A non-zero M2 status with code + error text is a genuine router-reported error (a trap),
            // not a protocol-shape violation. Surface it as TikCommandTrapException to match the
            // generic-error fallback of the API/CLI/REST transports.
            return new TikCommandTrapException(cmd, trap);
        }

        /// <summary>
        /// Awaited form of <see cref="EncodeNameValueFields"/>: the same encoding, with the name→id lookups a
        /// dynamic enum reference needs done under <c>await</c> instead of blocking the calling thread.
        /// </summary>
        /// <remarks>
        /// Two passes over the encoder rather than an async encoder. The first pass runs with a delegate that
        /// RECORDS what it is asked to resolve and answers with a placeholder; its bytes are thrown away, and
        /// what it leaves behind is the list of lookups — taken from the encoder itself, so it cannot drift
        /// from the encoder's own notion of which fields are references (<c>EncodeField</c>'s <c>enm</c> case,
        /// reached only after canonicalizing the name, the read-only gate and the <c>!</c>-prefix strip). Those
        /// are resolved in one awaited batch, and the second pass encodes for real against the answers.
        /// <para>A command that references nothing — the overwhelmingly common case — collects nothing and
        /// keeps the first pass's bytes, so it costs exactly what it did before: one pass, no round trip.
        /// <c>EncodeField</c> itself is unchanged and still synchronous; making it async would have rippled
        /// through 1000 lines of reverse-engineered encoder that no deterministic test covers.</para>
        /// </remarks>
        private async Task<List<byte[]>> EncodeNameValueFieldsAsync(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId,
            CancellationToken cancellationToken, bool allowReadOnly = false, bool includeFilters = false,
            bool skipSnapshotModifier = false)
        {
            var requests = new List<KeyValuePair<int[], string>>();
            var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId, ReferenceCollector(requests),
                allowReadOnly, includeFilters, skipSnapshotModifier);
            if (requests.Count == 0) return fields;

            var resolveRef = await _idResolver.ResolveReferencesAsync(requests, cancellationToken).ConfigureAwait(false);
            return EncodeNameValueFields(handler, descriptor, resolver, skipId, resolveRef,
                allowReadOnly, includeFilters, skipSnapshotModifier);
        }

        /// <inheritdoc cref="EncodeUnsetFields"/>
        private async Task<List<byte[]>> EncodeUnsetFieldsAsync(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            CancellationToken cancellationToken)
        {
            var requests = new List<KeyValuePair<int[], string>>();
            var fields = EncodeUnsetFields(handler, descriptor, resolver, ReferenceCollector(requests));
            if (requests.Count == 0) return fields;

            var resolveRef = await _idResolver.ResolveReferencesAsync(requests, cancellationToken).ConfigureAwait(false);
            return EncodeUnsetFields(handler, descriptor, resolver, resolveRef);
        }

        // The delegate of the collecting pass (see EncodeNameValueFieldsAsync): notes down every lookup the
        // encoder asks for and answers with a placeholder id so the pass runs to the end and the questions are
        // complete. The bytes it produces are discarded — only a pass that collected nothing is kept, and that
        // pass never called this at all.
        private static Func<int[], string, int?> ReferenceCollector(List<KeyValuePair<int[], string>> into)
            => (refHandler, name) =>
            {
                into.Add(new KeyValuePair<int[], string>(refHandler, name));
                return 0;
            };

        // Encode every NameValue parameter (except client-side markers and, optionally, .id) into M2 fields.
        // Read-only fields (per .jg) are skipped by the encoder (returns no bytes). A network field expands
        // to two entries (address + mask); a dynamic enum reference is resolved name→id by resolveRef.
        private List<byte[]> EncodeNameValueFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId,
            Func<int[], string, int?> resolveRef,
            bool allowReadOnly = false, bool includeFilters = false, bool skipSnapshotModifier = false)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                // Filters are query predicates and never inputs — except on an action window, where there is
                // nothing to filter and every parameter is an argument. This matters because the read path
                // rewrites Default-format parameters to Filter (TikGenericCommand.ResolveParamsForRead), so
                // an action reached through ExecuteSingleRow would otherwise silently lose its arguments and
                // the router would answer "required parameter … missing".
                if (!includeFilters && p.ParameterFormat == TikCommandParameterFormat.Filter) continue;
                if (p.Name.StartsWith(".") && p.Name != TikSpecialProperties.Id) continue; // .proplist/.tag/…
                if (p.Name == TikSpecialProperties.Id) { if (skipId) continue; }
                if (p.Name == "move-before" || p.Name == "destination") continue; // handled by move dest
                if (skipSnapshotModifier && IsMonitorSnapshotModifier(p.Name)) continue;

                // A bad field VALUE is what the router itself would trap on over the API, so surface it as a
                // trap here too — the native transport catches it client-side only because it has to resolve
                // references before sending. Consumers then handle one exception type across all transports.
                try
                {
                    fields.AddRange(resolver.EncodeField(p.Name, p.Value, resolveRef, allowReadOnly));
                }
                catch (WinboxFieldValueException ex)
                {
                    throw new TikCommandTrapException(
                        new TikGenericCommand(this, descriptor.CommandText),
                        new TikTrapSentenceResult(ex.Message));
                }
            }
            return fields;
        }

        /// <summary>
        /// Parameters that ask a monitor for a SINGLE reading rather than naming one of its inputs.
        /// </summary>
        /// <remarks>
        /// RouterOS needs <c>once</c> because a monitor on the API or a terminal runs until something stops it.
        /// A WinBox monitor window has no such input: the client starts a cycle, reads it and cancels it, so
        /// "one reading" is decided here (see <c>RunMonitorWindowSync</c>) and there is no M2 key to encode it
        /// to. Sending it anyway is what made <c>/interface/monitor-traffic interface=ether1 once</c> fail
        /// resolution on the field the caller never meant as data. Same idea as <c>.proplist</c>/<c>detail</c>
        /// being dropped per transport — what is a wire word on one is a client-side instruction on another.
        /// </remarks>
        private static bool IsMonitorSnapshotModifier(string name)
            => string.Equals(name, "once", StringComparison.OrdinalIgnoreCase);

        // Encode an 'unset' into M2 fields: every 'value-name=<field>' pseudo-parameter names a field to be
        // written back as empty. The parameter's own name is NOT a router field, so it must never reach the
        // resolver — its VALUE is the field name and the new value is the empty string.
        private List<byte[]> EncodeUnsetFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            Func<int[], string, int?> resolveRef)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                if (!string.Equals(p.Name, TikSpecialProperties.UnsetValueName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(p.Value))
                    continue;

                try
                {
                    fields.AddRange(resolver.EncodeField(p.Value, string.Empty, resolveRef));
                }
                catch (WinboxFieldValueException ex)
                {
                    throw new TikCommandTrapException(
                        new TikGenericCommand(this, descriptor.CommandText),
                        new TikTrapSentenceResult(ex.Message));
                }
            }

            if (fields.Count == 0)
                throw new TikCommandTrapException(
                    new TikGenericCommand(this, descriptor.CommandText),
                    new TikTrapSentenceResult(
                        "unset requires at least one '" + TikSpecialProperties.UnsetValueName
                        + "=<field>' parameter naming the field to clear."));

            return fields;
        }

        // Write changed fields back to the router. An ordinary table addresses one record by .id (set); a
        // SINGLETON (.jg type:'item') window — /system/identity, /ip/dns, /ip/settings, /snmp, … — holds a
        // single object and no record list, so it has no .id to address and webfig writes it with
        // set-singleton instead (ObjectHolder.setObject). Without this split every singleton write over the
        // native transport failed with "no such item: could not resolve record .id ''", i.e. no
        // IsSingleton entity was saveable at all — the suite only ever read them, so it went unnoticed
        // (P2.44). <paramref name="encodeFields"/> is deferred so the .id still resolves before the fields
        // are encoded, keeping "no such item" the error a caller sees when both are wrong.
        private async Task WriteFieldsAsync(int[] handler, WinboxFieldResolver resolver, TikCommandDescriptor descriptor,
            Func<Task<List<byte[]>>> encodeFields, CancellationToken cancellationToken)
        {
            if (IsSingletonWindow(ApiPathOf(descriptor.CommandText), handler))
            {
                await _ops.SetSingletonAsync(handler, await encodeFields().ConfigureAwait(false),
                    SingletonIdOf(descriptor), cancellationToken).ConfigureAwait(false);
                return;
            }
            int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await _ops.SetAsync(handler, id, await encodeFields().ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }

        // The optional .id of a singleton write. Only the literal "*HEX" handle is honored — the hidden
        // holders that carry one address a record of another table (webfig's 'Change Password' holder targets
        // a user), and resolving a friendly name would mean a getall, which a singleton handler has no record
        // list to answer. Returns -1 (send no .id) otherwise, which is the normal case.
        private static int SingletonIdOf(TikCommandDescriptor descriptor)
        {
            string? idParam = FindParam(descriptor, TikSpecialProperties.Id);
            // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
            if (!string.IsNullOrEmpty(idParam) && idParam!.StartsWith("*")
                && int.TryParse(idParam.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                return hexId;
            return -1;
        }

        // Resolve the M2 numeric record id from the command's .id parameter. The .id may be the RouterOS
        // "*HEX" handle form, or a friendly name (e.g. "ether1") — names are resolved via getall.
        // alternateIdParam names the target record instead of ".id" on this verb, and is tried first. `move` is
        // the case: RouterOS spells its target "numbers", and that is what the mapper's Move<TEntity> sends.
        // Reading only ".id" made every mapper-level move fail here with "could not resolve record .id ''" — on
        // a command the API, REST and all four CLI transports carry out fine. It went unnoticed because the one
        // move test in the suite hand-builds the command with ".id".
        // (Prose kept as a plain comment: a private method with a lone <param> tag is an incomplete XML doc
        // comment — CS1573 for every other parameter — and the mapper's Move<TEntity> is in tik4net.objects,
        // which this assembly does not reference, so the cref could not resolve either (CS1574).)
        private async Task<int> ResolveRecordIdAsync(int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, bool required, CancellationToken cancellationToken,
            string? alternateIdParam = null)
        {
            string? idParam = alternateIdParam != null ? FindParam(descriptor, alternateIdParam) : null;
            if (string.IsNullOrEmpty(idParam))
                idParam = FindParam(descriptor, TikSpecialProperties.Id);
            if (!string.IsNullOrEmpty(idParam))
            {
                if (idParam!.StartsWith("*") &&
                    int.TryParse(idParam.Substring(1), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                    return hexId;

                // Friendly name (or a where-style key): match against the record 'name' field via getall.
                int byName = await _idResolver
                    .FindIdByNameAsync(handler, resolver, idParam, cancellationToken).ConfigureAwait(false);
                if (byName >= 0) return byName;
            }

            if (required)
            {
                // The set/remove/move target does not exist (unresolvable .id) — same outcome as the API/CLI
                // transports' "no such item".
                var cmd = new TikGenericCommand(this, descriptor.CommandText);
                throw new TikNoSuchItemException(cmd, new TikTrapSentenceResult(
                    $"no such item: could not resolve record .id '{idParam}' on '{descriptor.CommandText}'."));
            }
            return -1;
        }

        // Resolve the move destination (next-id) from a NameValue "destination"/"move-before" parameter.
        private async Task<int> ResolveMoveDestAsync(int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            string? dest = FindParam(descriptor, "destination") ?? FindParam(descriptor, "move-before");
            if (string.IsNullOrEmpty(dest)) return -1; // move to end
            if (dest!.StartsWith("*") &&
                int.TryParse(dest.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                return hexId;
            int byName = await _idResolver
                .FindIdByNameAsync(handler, resolver, dest, cancellationToken).ConfigureAwait(false);
            return byName; // -1 if not found → move to end
        }

        private static string? FindParam(TikCommandDescriptor descriptor, string name)
        {
            foreach (var p in descriptor.Parameters)
                if (p.Name == name) return p.Value;
            return null;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        // Expand environment variables and resolve relative paths against the current directory.
        // Called at open time so %VAR% and paths like ".\.tik4net" or "../cache" work transparently.
        private static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }

        private void EnsureNativeOpen()
        {
            if (!IsOpened || _ops == null)
                throw new TikConnectionNotOpenException("WinBox native connection is not open.");
        }

        private IReadOnlyDictionary<string, int> OverridesFor(string apiPath)
        {
            return _fieldOverrides.TryGetValue(WinboxHandlerMap.Normalize(apiPath), out var map)
                ? map
                : new Dictionary<string, int>();
        }

        /// <summary>
        /// Whether the window <paramref name="apiPath"/> resolves to is a singleton (get-singleton /
        /// set-singleton) rather than a record list. Asks the catalog about the WINDOW first and falls back to
        /// the handler-level answer for paths reached by a raw <see cref="PathOverride"/>, where there is no
        /// window to ask about.
        /// </summary>
        /// <summary>
        /// Builds the field resolver for a path, telling it which WINDOW the path resolves to. That matters
        /// wherever several windows share one handler: every interface subtype reads <c>[20,0]</c> but
        /// declares its own field keys, so a resolver that only knew the handler could not tell EoIP's
        /// 'Remote Address' from GRE's.
        /// </summary>
        private WinboxFieldResolver MakeResolver(string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames,
                                       _handlerMap.ResolveDerivedKey(apiPath));

        /// <summary>
        /// The resolver for an ACTION invocation: the path's ordinary resolver with the action window's own
        /// arguments laid over it. Without the overlay an argument that shares its label with a record column
        /// resolves to the column — and a read-only column encodes to nothing, so the argument never leaves
        /// the client (see <see cref="WinboxJgCatalog.GetActionFields"/>).
        /// </summary>
        private WinboxFieldResolver MakeActionResolver(string apiPath, int[] handler, string actionLabel)
            => new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames,
                                       _handlerMap.ResolveDerivedKey(apiPath),
                                       _catalog.GetActionFields(handler, actionLabel));

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
        private static string ApiPathOf(string commandText)
        {
            string p = TikPath.Normalize(commandText);
            string verb = TikPath.Verb(p);
            return (verb == "print" || verb == "getall" || verb == "get") ? TikPath.Parent(p) : p;
        }
    }
}
