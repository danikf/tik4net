using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
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
    /// connect time from the version-matched <c>.jg</c> catalog (cached under
    /// <see cref="CatalogCachePath"/>); the apiName↔label mapping is a stable normalizer plus
    /// session overrides.</para>
    /// <para>Streaming monitors are supported via <c>ExecuteAsync</c>/<c>LoadAsync</c> (capability
    /// <see cref="TikConnectionCapability.Listen"/>): <c>.jg</c> <c>type:'query'</c> windows such as
    /// <c>/tool/torch</c>/<c>/tool/profile</c> are polled start→poll→cancel on a background worker.</para>
    /// </remarks>
    public class WinboxNativeConnection : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost
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
        /// Login timeout in milliseconds — the maximum time to wait for authentication / first M2 reply.
        /// Set before calling <see cref="Open(string, string, string)"/>.
        /// </summary>
        public int ConnectTimeout { get; set; } = 15000;

        /// <summary>
        /// Directory under which version-matched <c>.jg</c> catalogs are cached
        /// (<c>&lt;CatalogCachePath&gt;/&lt;routerVersion&gt;/*.jg</c>).
        /// Defaults to <c>%TEMP%/tik4net/</c>. Set before opening to change.
        /// Supports environment variables (<c>%APPDATA%</c>, <c>$HOME</c>, …) and relative paths
        /// (resolved against <see cref="Environment.CurrentDirectory"/> at open time).
        /// </summary>
        public string CatalogCachePath { get; set; } =
            Path.Combine(Path.GetTempPath(), "tik4net");

        private readonly WinboxHandlerMap _handlerMap = new WinboxHandlerMap();
        // apiPath → (apiName → key) session field overrides
        private readonly Dictionary<string, Dictionary<string, int>> _fieldOverrides =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        private IWinboxM2Channel _session;
        private WinboxNativeM2Operations _ops;
        private WinboxRecordCodec _codec;   // M2 record → API field decoder (see WinboxRecordCodec)
        private WinboxIdResolver _idResolver;   // friendly-name → M2 id lookup (see WinboxIdResolver)
        private readonly WinboxJgCatalog _catalog = new WinboxJgCatalog();
        private string _routerVersion;

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
            var session = CreateChannel();
            try
            {
                session.Open(host, port, user, password, ConnectTimeout);
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
            InitAfterAuth(session);
        }

        /// <inheritdoc/>
        public override Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, DefaultPortValue, user, password);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password)
        {
            // WinboxM2Session.Open is synchronous (blocking socket); run it on a worker thread so
            // OpenAsync does not block the caller's thread.
            return Task.Run(() => Open(host, port, user, password));
        }

        private void InitAfterAuth(IWinboxM2Channel session)
        {
            _session = session;
            _ops = new WinboxNativeM2Operations(session, ConnectTimeout);
            // Participate in the shared row-level diagnostics: render each raw M2 request/reply to the
            // OnWriteRow/OnReadRow events (gated so the describe is only built when something listens).
            _ops.OnRequest = msg => { if (RowTracingEnabled) FireWriteRow(M2Message.Describe(msg)); };
            _ops.OnResponse = msg => { if (RowTracingEnabled) FireReadRow(M2Message.Describe(msg)); };
            try { _routerVersion = _ops.GetRouterVersion(); }
            catch { _routerVersion = null; }
            try { _catalog.EnsureLoaded(session, _routerVersion, ResolvePath(CatalogCachePath), ConnectTimeout); }
            catch { /* catalog is best-effort; seeds + normalizer still work */ }
            // Feed the .jg-derived apiPath→handler map into the handler resolver (after session overrides,
            // before the shipped override tail).
            _handlerMap.SetDerivedPaths(_catalog.GetDerivedPaths());
            _handlerMap.SetSubtypeFilters(_catalog.GetSubtypeFilters());
            _codec = new WinboxRecordCodec(_ops, _catalog);
            _idResolver = new WinboxIdResolver(_ops, _codec, _catalog);
            SetOpened();
        }

        /// <inheritdoc/>
        public override void Close()
        {
            _session?.Dispose();
            _session = null;
            _ops = null;
            _codec = null;
            _idResolver = null;
            SetClosed();
        }

        // ── Native read overrides ───────────────────────────────────────────────

        /// <inheritdoc/>
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
        {
            // Serialise the M2 channel: the transport is request/reply, so a concurrent CRUD call or
            // monitor poll on another thread must not interleave its request with ours. Background workers
            // already hold _cmdLock and call RunPrintCore directly (the semaphore is not reentrant).
            _cmdLock.Wait();
            try { return RunPrintCore(descriptor); }
            finally { _cmdLock.Release(); }
        }

        private IList<TikRecordSentence> RunPrintCore(TikCommandDescriptor descriptor)
        {
            EnsureNativeOpen();

            string apiPath = ApiPathOf(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
                // A "monitor once" snapshot (e.g. /interface/ethernet/monitor numbers=ether1): the live
                // values are read-only fields on the parent interface record, so a getall + name filter gives
                // the snapshot. Tried before the action-verb path (monitor is not a doit/action cmd).
                if (TryRunMonitor(descriptor, out var monitorRows)) return monitorRows;

                // The path may be an action verb (e.g. /system/script/run) rather than a table — a .jg
                // doit/action SYS_CMD on the parent handler. Actions perform work and yield no rows, so they
                // belong on the non-query path: reject the read misuse explicitly (R7) and guide to
                // ExecuteNonQuery (RunVerb dispatches the SYS_CMD) before reporting "no such command".
                if (IsActionVerbPath(descriptor))
                    throw ActionVerbOnReadPath(descriptor.CommandText);

                // No native handler mapping for this read path. Surface it like other transports surface a
                // missing command, so EnsureCommandAvailable / TikNoSuchCommandException handling kicks in
                // (e.g. /interface/wireless on a router without the wireless package).
                var cmd = new TikGenericCommand(this, descriptor.CommandText);
                throw new TikNoSuchCommandException(cmd, new TikTrapSentenceResult(
                    $"WinBox native: no M2 handler mapping for path '{apiPath}'. " +
                    $"Add one via connection.PathAlias(\"{apiPath}\", \"/winbox/menu/label-path\") " +
                    $"(the labels WinBox shows for that window), or connection.PathOverride(\"{apiPath}\", " +
                    $"new[]{{maj,min}}) for a raw handler, or use a WinboxCli connection."));
            }
            handler = PreferSingletonHealthHandler(apiPath, handler);
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();

            // Singleton tables (type:'item' window, e.g. /system/resource, /ip/dns) expose a single record
            // read via get-singleton; everything else lists via getall.
            List<Dictionary<int, Tuple<string, object>>> records;
            try
            {
                if (_catalog.IsSingletonHandler(handler))
                {
                    var one = _ops.GetSingleton(handler);
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
                    records = _ops.GetAll(handler, flags);
                }
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }

            // Interface subtype paths (e.g. /interface/bridge) share the generic interface handler; keep only the
            // rows whose numeric type field matches the subtype's discriminator (derived from the .jg typevalue).
            if (_handlerMap.TryResolveSubtypeFilter(apiPath, out int typeKey, out int typeValue))
                records = records.Where(r =>
                {
                    if (!r.TryGetValue(typeKey, out var t) || t.Item2 == null) return false;
                    try { return Convert.ToInt64(t.Item2) == typeValue; } catch { return false; }
                }).ToList();

            var rows = new List<TikRecordSentence>(records.Count);
            foreach (var rec in records)
                rows.Add(new TikRecordSentence(_codec.DecodeRecord(rec, keyToName, keyToField)));

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
        private bool TryRunMonitor(TikCommandDescriptor descriptor, out IList<TikRecordSentence> rows)
        {
            rows = null;
            if (!string.Equals(TikPath.Verb(descriptor.CommandText), "monitor", StringComparison.OrdinalIgnoreCase))
                return false;

            string parentPath = TikPath.Parent(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(parentPath);
            if (handler == null) return false;

            // The interface is named via 'numbers' (RouterOS monitor convention), or 'interface'/'.id'.
            string target = FindParam(descriptor, "numbers")
                ?? FindParam(descriptor, "interface")
                ?? FindParam(descriptor, TikSpecialProperties.Id);
            if (string.IsNullOrEmpty(target)) return false;

            var resolver = new WinboxFieldResolver(parentPath, handler, _catalog, OverridesFor(parentPath), _useGuiNames);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            int flags = WinboxM2Protocol.GetAllFlags
                | (_catalog.HasDynamicFields(handler) ? WinboxM2Protocol.GetAllStatsFlag : 0);

            List<Dictionary<int, Tuple<string, object>>> records;
            try { records = _ops.GetAll(handler, flags); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }

            var result = new List<TikRecordSentence>();
            foreach (var rec in records)
            {
                var decoded = _codec.DecodeRecord(rec, keyToName, keyToField);
                if (decoded.TryGetValue("name", out var nm) && string.Equals(nm, target, StringComparison.Ordinal))
                {
                    result.Add(new TikRecordSentence(decoded));
                    break;
                }
            }
            rows = result;
            return true;
        }

        // True when the command's last path segment matches a .jg doit/action on the parent handler
        // (e.g. /system/script/run). Detection only — does NOT invoke. Used by the read path to reject the
        // misuse; the non-query path dispatches the SYS_CMD via DispatchActionVerb.
        private bool IsActionVerbPath(TikCommandDescriptor descriptor)
        {
            int[] handler = _handlerMap.Resolve(TikPath.Parent(descriptor.CommandText));
            if (handler == null) return false;
            var actions = _catalog.GetHandlerActions(handler);
            if (actions == null) return false;
            string verb = TikPath.Verb(descriptor.CommandText);
            foreach (var kv in actions)
                if (ActionMatchesVerb(kv.Key, verb)) return true;
            return false;
        }

        // Invokes the action verb's .jg doit/SYS_CMD on its (already-resolved) parent handler with the
        // optional target .id, mirroring how CLI terminals run actions fire-and-forget (no rows). Throws
        // NotSupported when the verb is not a known action on the handler.
        private void DispatchActionVerb(string verb, string apiPath, int[] handler,
            WinboxFieldResolver resolver, TikCommandDescriptor descriptor)
        {
            int cmd = -1;
            var actions = _catalog.GetHandlerActions(handler);
            if (actions != null)
                foreach (var kv in actions)
                    if (ActionMatchesVerb(kv.Key, verb)) { cmd = kv.Value; break; }
            if (cmd < 0)
                throw new NotSupportedException(
                    $"WinBox native: command verb '{verb}' on '{apiPath}' is not supported. " +
                    "Use a WinboxCli or Api connection.");

            int id = ResolveRecordId(handler, resolver, descriptor, required: false);
            try { _ops.InvokeAction(handler, cmd, id); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
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
        {
            EnsureNativeOpen();
            // descriptor.CommandText is "/path/add"; the resolution path is the parent.
            string apiPath = TikPath.Parent(descriptor.CommandText);

            // Serialise the request/reply channel against concurrent CRUD / monitor polls (see RunPrint).
            _cmdLock.Wait();
            try
            {
                var (handler, resolver) = ResolveHandlerAndFields(apiPath);
                var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId: true);
                int newId = _ops.Add(handler, fields);
                return newId >= 0 ? "*" + ((uint)newId).ToString("X") : null;
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
            finally { _cmdLock.Release(); }
        }

        /// <inheritdoc/>
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
        {
            EnsureNativeOpen();
            string verb = TikPath.Verb(descriptor.CommandText);
            string apiPath = TikPath.Parent(descriptor.CommandText);

            // Serialise the request/reply channel against concurrent CRUD / monitor polls (see RunPrint).
            _cmdLock.Wait();
            try
            {
                var (handler, resolver) = ResolveHandlerAndFields(apiPath);
                RunVerb(verb, apiPath, handler, resolver, descriptor);
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
            finally { _cmdLock.Release(); }
        }

        private void RunVerb(string verb, string apiPath, int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor)
        {
            switch (verb)
            {
                case "add":
                {
                    // /path/add invoked via ExecuteNonQuery (the new id, if any, is discarded here).
                    var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId: true);
                    _ops.Add(handler, fields);
                    break;
                }
                case "set":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId: true);
                    _ops.Set(handler, id, fields);
                    break;
                }
                case "enable":
                case "disable":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    var f = resolver.EncodeField("disabled", verb == "disable" ? "true" : "false");
                    _ops.Set(handler, id, f);
                    break;
                }
                case "unset":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    // unset = set the named field(s) back to empty/default.
                    var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId: true);
                    _ops.Set(handler, id, fields);
                    break;
                }
                case "remove":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    _ops.Remove(handler, id);
                    break;
                }
                case "move":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    int dest = ResolveMoveDest(handler, resolver, descriptor);
                    _ops.Move(handler, id, dest);
                    break;
                }
                default:
                    // Action verb (e.g. /system/script/run → a .jg doit/SYS_CMD on this handler), invoked via
                    // ExecuteNonQuery. Dispatch it fire-and-forget; throws NotSupported if it is not an action.
                    DispatchActionVerb(verb, apiPath, handler, resolver, descriptor);
                    break;
            }
        }


        // ── Streaming monitor (ExecuteAsync / LoadAsync) ─────────────────────────

        /// <summary>
        /// Native WinBox M2 supports streaming monitors (<c>.jg</c> <c>type:'query'</c> / poll-action windows),
        /// so it reports <see cref="TikConnectionCapability.Listen"/> on top of <see cref="TikConnectionCapability.Crud"/>.
        /// </summary>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen | TikConnectionCapability.SafeMode;

        // ── Safe Mode (system handler [17]) ──────────────────────────────────────
        // Take/release map to the webfig toggleSafeMode() M2 commands. WebFig exposes no in-place
        // unroll/get, so SafeModeUnroll throws (drop the connection to roll back) and SafeModeGet
        // reports the client-side held flag.

        private uint _safeModeId;

        /// <inheritdoc/>
        public override void SafeModeTake()
        {
            EnsureOpened();
            if (SafeModeHeld) return;
            _safeModeId = _ops.SafeModeTake();
            SafeModeHeld = true;
        }

        /// <inheritdoc/>
        public override void SafeModeRelease()
        {
            EnsureOpened();
            if (!SafeModeHeld) return;
            _ops.SafeModeRelease(_safeModeId);
            SafeModeHeld = false;
            _safeModeId = 0;
        }

        /// <inheritdoc/>
        public override void SafeModeUnroll()
            => throw new NotSupportedException(
                "Native WinBox exposes only take/release for Safe Mode (no in-place unroll). " +
                "To roll back, close the connection without calling SafeModeRelease — RouterOS reverts " +
                "the changes automatically. For an explicit unroll use the binary API or a CLI transport.");

        /// <summary>
        /// Runs a streaming-monitor command (e.g. <c>/tool/torch</c>, <c>/tool/profile</c>) on a background
        /// worker that polls the router every <c>autorefresh</c> ms over the normal M2 channel — start → poll →
        /// cancel (webfig <c>ObjectQuery</c>; see <c>_notes/winbox-native-m2-plan.md</c> §20). Each polled record
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
            // diff snapshots by .id (see _notes/winbox-native-m2-plan.md §20). Deleted rows are surfaced as a
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

            // Otherwise a streaming-monitor window (/tool/torch, /tool/profile, /interface/monitor-traffic, …).
            // The monitor path is the command path itself or carries a trailing verb; try the plain path first,
            // then the verb-stripped parent.
            string apiPath = ApiPathOf(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(apiPath);
            WinboxMonitorSpec spec = _catalog.GetMonitorByHandler(handler);
            if (spec == null)
            {
                string parent = TikPath.Parent(descriptor.CommandText);
                int[] ph = _handlerMap.Resolve(parent);
                var pspec = _catalog.GetMonitorByHandler(ph);
                if (pspec != null) { apiPath = parent; handler = ph; spec = pspec; }
            }
            if (spec == null)
            {
                var cmd = new TikGenericCommand(this, descriptor.CommandText);
                throw new TikNoSuchCommandException(cmd, new TikTrapSentenceResult(
                    $"WinBox native: '{descriptor.CommandText}' is not a streaming-monitor window in the .jg catalog. " +
                    $"Add a PathOverride(\"{apiPath}\", new[]{{maj,min}}) to a monitor handler, or use a CLI transport."));
            }

            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            return PollingMonitorEngine.StartWorker("winbox-native-monitor",
                handle => MonitorLoop(spec, descriptor, resolver, keyToName, keyToField, handle, onRow, onError, onDone));
        }

        // ── IPollingMonitorHost (shared listen/async-list scaffolding lives in PollingMonitorEngine) ──

        /// <inheritdoc/>
        bool IPollingMonitorHost.IsOpen => IsOpened;

        /// <inheritdoc/>
        IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor)
        {
            // Serialise the M2 channel against concurrent CRUD / monitor polls (see RunPrint). The engine
            // owns no lock, so the snapshot acquires it here and calls the unlocked core.
            _cmdLock.Wait();
            try { return RunPrintCore(printDescriptor); }
            finally { _cmdLock.Release(); }
        }

        /// <inheritdoc/>
        TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex)
            => ex is WinboxM2OperationException m
                ? new TikTrapSentenceResult(m.Message, $"0x{m.Code:X}", m.ErrorText)
                : new TikTrapSentenceResult(ex.Message);

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
                _cmdLock.Wait();
                try
                {
                    // Encode the caller's NameValue parameters as the monitor's request fields (interface, cpu, …).
                    // allowReadOnly: a window's input fields are often .jg-marked ro (display) yet are the
                    // monitor's legitimate request inputs and must be sent (e.g. ping 'address').
                    var requestFields = EncodeNameValueFields(
                        spec.Handler, descriptor, resolver, skipId: true, allowReadOnly: true);
                    id = _ops.StartMonitor(spec.Handler, spec.StartCmd, requestFields);
                    started = true;
                }
                finally { _cmdLock.Release(); }

                while (!handle.CancelRequested)
                {
                    bool done;
                    List<Dictionary<int, Tuple<string, object>>> records;
                    _cmdLock.Wait();
                    try { (records, done) = _ops.PollMonitor(spec.Handler, spec.PollCmd, id, spec.IsQuery); }
                    finally { _cmdLock.Release(); }

                    foreach (var rec in records)
                        onRow?.Invoke(new TikRecordSentence(_codec.DecodeRecord(rec, keyToName, keyToField)));

                    if (done) break;

                    // Sleep the autorefresh interval in short slices so Cancel is responsive.
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
                    try { _cmdLock.Wait(); _ops.CancelMonitor(spec.Handler, spec.CancelCmd, id); }
                    catch { /* best-effort */ }
                    finally { _cmdLock.Release(); }
                }
                onDone?.Invoke();
            }
        }

        // The set of read-only (ro:1) field names for a table's handler — the volatile runtime fields a listen
        // diff must ignore. Empty when the path has no handler/catalog entry (then all fields are compared).
        private HashSet<string> ReadOnlyFieldNames(string apiPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[] handler = _handlerMap.Resolve(apiPath);
            if (handler == null) return set;
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames);
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            foreach (var kv in keyToField)
                if (kv.Value != null && kv.Value.ReadOnly && keyToName.TryGetValue(kv.Key, out var n))
                    set.Add(n);
            return set;
        }

        // Query-filter evaluation (postfix stack) is shared with the CLI async-list path — see
        // tik4net.Connection.TikQueryStack.

        // ── Write helpers ──────────────────────────────────────────────────────

        private (int[] handler, WinboxFieldResolver resolver) ResolveHandlerAndFields(string apiPath)
        {
            int[] handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
                // Surface an unmapped write path the same way reads do — as "no such command" — so callers
                // get a consistent exception type across read and write (e.g. invalid path, or a path under a
                // package the router lacks).
                var c = new TikGenericCommand(this, apiPath);
                throw new TikNoSuchCommandException(c, new TikTrapSentenceResult(
                    $"WinBox native: no M2 handler mapping for path '{apiPath}'. " +
                    $"Add one via connection.PathAlias(\"{apiPath}\", \"/winbox/menu/label-path\") " +
                    $"(the labels WinBox shows for that window), or connection.PathOverride(\"{apiPath}\", " +
                    $"new[]{{maj,min}}) for a raw handler, or use a WinboxCli connection."));
            }
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath), _useGuiNames);
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

        // Encode every NameValue parameter (except client-side markers and, optionally, .id) into M2 fields.
        // Read-only fields (per .jg) are skipped by the encoder (returns no bytes). A network field expands
        // to two entries (address + mask); a dynamic enum reference is resolved name→id via getall.
        private List<byte[]> EncodeNameValueFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId,
            bool allowReadOnly = false)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter) continue;
                if (p.Name.StartsWith(".") && p.Name != TikSpecialProperties.Id) continue; // .proplist/.tag/…
                if (p.Name == TikSpecialProperties.Id) { if (skipId) continue; }
                if (p.Name == "move-before" || p.Name == "destination") continue; // handled by move dest
                fields.AddRange(resolver.EncodeField(p.Name, p.Value, _idResolver.ResolveReference, allowReadOnly));
            }
            return fields;
        }

        // Resolve the M2 numeric record id from the command's .id parameter. The .id may be the RouterOS
        // "*HEX" handle form, or a friendly name (e.g. "ether1") — names are resolved via getall.
        private int ResolveRecordId(int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, bool required)
        {
            string idParam = FindParam(descriptor, TikSpecialProperties.Id);
            if (!string.IsNullOrEmpty(idParam))
            {
                if (idParam.StartsWith("*") &&
                    int.TryParse(idParam.Substring(1), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                    return hexId;

                // Friendly name (or a where-style key): match against the record 'name' field via getall.
                int byName = _idResolver.FindIdByName(handler, resolver, idParam);
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
        private int ResolveMoveDest(int[] handler, WinboxFieldResolver resolver, TikCommandDescriptor descriptor)
        {
            string dest = FindParam(descriptor, "destination") ?? FindParam(descriptor, "move-before");
            if (string.IsNullOrEmpty(dest)) return -1; // move to end
            if (dest.StartsWith("*") &&
                int.TryParse(dest.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                return hexId;
            int byName = _idResolver.FindIdByName(handler, resolver, dest);
            return byName; // -1 if not found → move to end
        }

        private static string FindParam(TikCommandDescriptor descriptor, string name)
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

        // Board-gated singleton recovery for /system/health. The WinBox menu has a name/value 'map' window
        // (RouterBOARD, [24,29]) and a hardware-sensor 'item' singleton window (x86, [24,14]) under the same
        // "Health" label; the shipped path alias resolves to the map handler, which answers getall with
        // NotImplemented on x86/CHR (verified live). When the resolved handler is NOT a singleton, prefer the
        // catalog's singleton health window (read via get-singleton) — the one webfig opens on x86. The handler
        // number is read live from the .jg, so this stays version-portable. Returns the original handler
        // unchanged for every other path (and when no singleton health window exists in the catalog).
        private int[] PreferSingletonHealthHandler(string apiPath, int[] handler)
        {
            if (handler == null || _catalog.IsSingletonHandler(handler)) return handler;
            if (!string.Equals(WinboxHandlerMap.Normalize(apiPath), "/system/health", StringComparison.OrdinalIgnoreCase))
                return handler;
            return _catalog.FindSingletonHandlerByLeaf("health") ?? handler;
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
