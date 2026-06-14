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
    /// <see cref="CatalogCachePath"/>); the apiNameâ†”label mapping is a stable normalizer plus
    /// session overrides.</para>
    /// <para>Streaming monitors are supported via <c>ExecuteAsync</c>/<c>LoadAsync</c> (capability
    /// <see cref="TikConnectionCapability.Listen"/>): <c>.jg</c> <c>type:'query'</c> windows such as
    /// <c>/tool/torch</c>/<c>/tool/profile</c> are polled startâ†’pollâ†’cancel on a background worker.</para>
    /// </remarks>
    public sealed class WinboxNativeConnection : TikCommandConnectionBase, ITikMonitorTransport
    {
        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

        /// <summary>
        /// Login timeout in milliseconds â€” the maximum time to wait for authentication / first M2 reply.
        /// Set before calling <see cref="Open(string, string, string)"/>.
        /// </summary>
        public int ConnectTimeout { get; set; } = 15000;

        /// <summary>
        /// Directory under which version-matched <c>.jg</c> catalogs are cached
        /// (<c>&lt;CatalogCachePath&gt;/&lt;routerVersion&gt;/*.jg</c>).
        /// Defaults to <c>%TEMP%/tik4net/</c>. Set before opening to change.
        /// </summary>
        public string CatalogCachePath { get; set; } =
            Path.Combine(Path.GetTempPath(), "tik4net");

        private readonly WinboxHandlerMap _handlerMap = new WinboxHandlerMap();
        // apiPath â†’ (apiName â†’ key) session field overrides
        private readonly Dictionary<string, Dictionary<string, int>> _fieldOverrides =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        private WinboxM2Session _session;
        private WinboxNativeM2Operations _ops;
        private readonly WinboxJgCatalog _catalog = new WinboxJgCatalog();
        private string _routerVersion;

        // â”€â”€ Session configuration (set before/after open) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Adds a session field override <c>apiName â†’ key</c> for the given API path. Takes priority over
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
        /// Adds a session override mapping an API path to a WinBox M2 handler array
        /// (e.g. <c>/ppp/secret</c> â†’ <c>[20, 12]</c>). Takes priority over the seed table.
        /// </summary>
        public void PathOverride(string apiPath, int[] handler)
        {
            _handlerMap.AddOverride(apiPath, handler);
        }

        // â”€â”€ Open / Close â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <inheritdoc/>
        public override void Open(string host, string user, string password)
            => Open(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override void Open(string host, int port, string user, string password)
        {
            var session = new WinboxM2Session();
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
            => OpenAsync(host, DefaultPort, user, password);

        /// <inheritdoc/>
        public override Task OpenAsync(string host, int port, string user, string password)
        {
            // WinboxM2Session.Open is synchronous (blocking socket); run it on a worker thread so
            // OpenAsync does not block the caller's thread.
            return Task.Run(() => Open(host, port, user, password));
        }

        private void InitAfterAuth(WinboxM2Session session)
        {
            _session = session;
            _ops = new WinboxNativeM2Operations(session, ConnectTimeout);
            // Participate in the shared row-level diagnostics: render each raw M2 request/reply to the
            // OnWriteRow/OnReadRow events (gated so the describe is only built when something listens).
            _ops.OnRequest = msg => { if (RowTracingEnabled) FireWriteRow(M2Message.Describe(msg)); };
            _ops.OnResponse = msg => { if (RowTracingEnabled) FireReadRow(M2Message.Describe(msg)); };
            try { _routerVersion = _ops.GetRouterVersion(); }
            catch { _routerVersion = null; }
            try { _catalog.EnsureLoaded(session, _routerVersion, CatalogCachePath, ConnectTimeout); }
            catch { /* catalog is best-effort; seeds + normalizer still work */ }
            // Feed the .jg-derived apiPathâ†’handler map into the handler resolver (after session overrides,
            // before the shipped override tail).
            _handlerMap.SetDerivedPaths(_catalog.GetDerivedPaths());
            _handlerMap.SetSubtypeFilters(_catalog.GetSubtypeFilters());
            SetOpened();
        }

        /// <inheritdoc/>
        public override void Close()
        {
            _session?.Dispose();
            _session = null;
            _ops = null;
            SetClosed();
        }

        // â”€â”€ Native read overrides â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <inheritdoc/>
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
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

                // The path may be an action verb (e.g. /system/script/run) rather than a table â€” a .jg
                // doit/action SYS_CMD on the parent handler. Dispatch it before reporting "no such command".
                if (TryRunActionVerb(descriptor, out var actionRows)) return actionRows;

                // No native handler mapping for this read path. Surface it like other transports surface a
                // missing command, so EnsureCommandAvailable / TikNoSuchCommandException handling kicks in
                // (e.g. /interface/wireless on a router without the wireless package).
                var cmd = new TikGenericCommand(this, descriptor.CommandText);
                throw new TikNoSuchCommandException(cmd, new TikTrapSentenceResult(
                    $"WinBox native: no M2 handler mapping for path '{apiPath}'. " +
                    $"Add one via connection.PathOverride(\"{apiPath}\", new[]{{maj,min}}) " +
                    $"or use a WinboxCli connection."));
            }
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath));
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
                rows.Add(new TikRecordSentence(DecodeRecord(rec, keyToName, keyToField)));

            // Apply Filter parameters (?name=value) in-memory — RouterOS-side filtering is not used here.
            // The filters form a postfix query stack (?#| OR, ?#& AND, ?#! NOT), so they are evaluated as such
            // rather than a naive AND-of-equalities; leftover predicates are implicitly ANDed.
            var filters = descriptor.Parameters
                .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter)
                .ToList();
            if (filters.Count > 0)
                rows = rows.Where(r => MatchesQueryStack(r, filters)).ToList();

            return rows;
        }

        // Attempts a "monitor [once]" snapshot (e.g. /interface/ethernet/monitor numbers=ether1). The monitored
        // values (rate, link status, auto-negotiation, full-duplex) are read-only fields on the parent
        // interface record â€” webfig surfaces them as a Status tab of the [20,0] window, not a separate handler.
        // A getall on the parent handler filtered to the named interface yields the same single snapshot the
        // RouterOS "monitor once" returns. Returns false (fall through) when this is not a monitor path.
        private bool TryRunMonitor(TikCommandDescriptor descriptor, out IList<TikRecordSentence> rows)
        {
            rows = null;
            if (!string.Equals(VerbOf(descriptor.CommandText), "monitor", StringComparison.OrdinalIgnoreCase))
                return false;

            string parentPath = StripVerb(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(parentPath);
            if (handler == null) return false;

            // The interface is named via 'numbers' (RouterOS monitor convention), or 'interface'/'.id'.
            string target = FindParam(descriptor, "numbers")
                ?? FindParam(descriptor, "interface")
                ?? FindParam(descriptor, TikSpecialProperties.Id);
            if (string.IsNullOrEmpty(target)) return false;

            var resolver = new WinboxFieldResolver(parentPath, handler, _catalog, OverridesFor(parentPath));
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
                var decoded = DecodeRecord(rec, keyToName, keyToField);
                if (decoded.TryGetValue("name", out var nm) && string.Equals(nm, target, StringComparison.Ordinal))
                {
                    result.Add(new TikRecordSentence(decoded));
                    break;
                }
            }
            rows = result;
            return true;
        }

        // Attempts to dispatch an action verb (e.g. /system/script/run) whose last path segment matches a
        // .jg doit/action on the parent handler. On a match: resolves the optional target .id, invokes the
        // SYS_CMD, and returns an empty row set (the action produces no record rows over native M2, mirroring
        // how the CLI terminals run fire-and-forget). Returns false when the path is not such an action.
        private bool TryRunActionVerb(TikCommandDescriptor descriptor, out IList<TikRecordSentence> rows)
        {
            rows = null;
            string verb = VerbOf(descriptor.CommandText);
            string parentPath = StripVerb(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(parentPath);
            if (handler == null) return false;

            var actions = _catalog.GetHandlerActions(handler);
            if (actions == null) return false;

            int cmd = -1;
            foreach (var kv in actions)
                if (ActionMatchesVerb(kv.Key, verb)) { cmd = kv.Value; break; }
            if (cmd < 0) return false;

            var resolver = new WinboxFieldResolver(parentPath, handler, _catalog, OverridesFor(parentPath));
            int id = ResolveRecordId(handler, resolver, descriptor, required: false);
            try { _ops.InvokeAction(handler, cmd, id); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }

            rows = new List<TikRecordSentence>();
            return true;
        }

        // True when a .jg action label maps to the RouterOS API verb: exact match, or the label's first
        // hyphen-token equals the verb ("run" â†” "run-script").
        private static bool ActionMatchesVerb(string normalizedLabel, string verb)
        {
            if (string.Equals(normalizedLabel, verb, StringComparison.OrdinalIgnoreCase)) return true;
            int dash = normalizedLabel.IndexOf('-');
            string first = dash > 0 ? normalizedLabel.Substring(0, dash) : normalizedLabel;
            return string.Equals(first, verb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Translates a decoded M2 record (<c>key â†’ (wireType, value)</c>) into a RouterOS API field
        /// dictionary (<c>apiName â†’ stringValue</c>). Unknown keys are dropped; <c>.id</c> is emitted as
        /// the RouterOS <c>*HEX</c> handle form so it round-trips through the O/R mapper.
        /// </summary>
        private Dictionary<string, string> DecodeRecord(
            Dictionary<int, Tuple<string, object>> rec, IReadOnlyDictionary<int, string> keyToName,
            IReadOnlyDictionary<int, WinboxJgField> keyToField)
        {
            // Keys consumed by an owning field, not emitted on their own: a network field's netmask sibling,
            // and the opt/not flag bools of an optional/invertible field (its value rides on the leaf key).
            var consumedKeys = new HashSet<int>();
            if (keyToField != null)
                foreach (var f in keyToField.Values)
                {
                    if (f.UiType == "network" && f.MaskKey != 0) consumedKeys.Add(f.MaskKey);
                    if (f.OptKey != 0) consumedKeys.Add(f.OptKey);
                    if (f.NotKey != 0) consumedKeys.Add(f.NotKey);
                }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rec)
            {
                if (consumedKeys.Contains(kv.Key)) continue;
                if (!keyToName.TryGetValue(kv.Key, out var apiName)) continue;
                if (fields.ContainsKey(apiName)) continue;

                if (apiName == TikSpecialProperties.Id)
                {
                    fields[apiName] = FormatId(kv.Value.Item2);
                    continue;
                }

                WinboxJgField jf = null;
                keyToField?.TryGetValue(kv.Key, out jf);
                fields[apiName] = FormatTyped(jf, kv.Value.Item1, kv.Value.Item2, rec);
            }
            return fields;
        }

        // Format an M2 value to its RouterOS API text using the .jg UI-semantic type: IPs unpack from u32,
        // a network field renders "addr/prefixlen" (pulling the netmask from its maskid sibling key), MACs
        // from raw bytes, static enums back to their string label, dynamic enum references back to the
        // referenced record's name. Falls back to the wire-type formatter.
        private string FormatTyped(WinboxJgField jf, string wireType, object value,
            Dictionary<int, Tuple<string, object>> rec)
        {
            if (jf != null && value != null)
            {
                switch (jf.UiType)
                {
                    case "ipaddr":
                        return WinboxFieldResolver.IpFromU32(value);
                    case "network":
                    {
                        string addr = WinboxFieldResolver.IpFromU32(value);
                        if (jf.MaskKey != 0 && rec.TryGetValue(jf.MaskKey, out var mt) && mt.Item2 != null)
                        {
                            if (jf.IsRange)
                            {
                                // range:1 â†’ the maskid sibling is the range-END address, not a netmask. A single
                                // host (start==end) renders as the bare address; a span as "start-end" (API form).
                                string end = WinboxFieldResolver.IpFromU32(mt.Item2);
                                return addr == end ? addr : addr + "-" + end;
                            }
                            return addr + "/" + WinboxFieldResolver.MaskToPrefix(mt.Item2);
                        }
                        return addr;
                    }
                    case "macaddr":
                        return WinboxFieldResolver.MacFromBytes(value);
                    case "set":
                    {
                        // Bitmask flag set â†’ comma-joined labels (.jg map key = bit index). The opt/not flag
                        // keys are consumed separately in DecodeRecord, so only the value rides here. A set
                        // 'not' flag (key NotKey) renders as the RouterOS '!' negation prefix on the whole
                        // value (CLI/API form, e.g. "!established,related").
                        if (jf.EnumMap == null) break;
                        long bits;
                        try { bits = Convert.ToInt64(value); } catch { break; }
                        var labels = jf.EnumMap.Where(kv => (bits & (1L << kv.Key)) != 0)
                            .OrderBy(kv => kv.Key).Select(kv => kv.Value);
                        string joined = string.Join(",", labels);
                        bool negated = jf.NotKey != 0 && rec.TryGetValue(jf.NotKey, out var nt)
                            && nt.Item2 is bool nb && nb;
                        return negated ? "!" + joined : joined;
                    }
                }
                // dynamic enum reference: render the referenced object's name (e.g. interface id â†’ "ether1").
                if (jf.RefHandler != null)
                {
                    string name = ResolveRefName(jf.RefHandler, value);
                    if (name != null) return name;
                }
                // static enum: map the numeric value back to its API string label.
                if (jf.EnumMap != null)
                {
                    try
                    {
                        int iv = unchecked((int)Convert.ToInt64(value));
                        if (jf.EnumMap.TryGetValue(iv, out var label)) return label;
                    }
                    catch { /* not numeric â€” fall through */ }
                }
            }
            return FormatValue(wireType, value);
        }

        // id â†’ name cache per referenced table, built lazily from one getall. Names are stable enough within
        // a session; this avoids a getall per referenced field per row.
        private readonly Dictionary<string, Dictionary<int, string>> _refNameCache =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        // Resolve a dynamic-enum reference value (the referenced record's numeric id) back to its name.
        private string ResolveRefName(int[] refHandler, object idValue)
        {
            int id;
            try { id = unchecked((int)Convert.ToInt64(idValue)); }
            catch { return null; }

            string key = string.Join(",", refHandler);
            if (!_refNameCache.TryGetValue(key, out var map))
            {
                map = new Dictionary<int, string>();
                var refResolver = new WinboxFieldResolver(null, refHandler, _catalog, EmptyOverrides);
                var k2n = refResolver.BuildKeyToApiName();
                int nameKey = -1, idKey = WinboxM2Protocol.RecordKey.Id;
                foreach (var kv in k2n) if (kv.Value == "name") { nameKey = kv.Key; break; }
                try
                {
                    foreach (var r in _ops.GetAll(refHandler))
                        if (r.TryGetValue(idKey, out var idt) && idt.Item2 != null
                            && nameKey >= 0 && r.TryGetValue(nameKey, out var nt) && nt.Item2 != null)
                        {
                            try { map[unchecked((int)Convert.ToInt64(idt.Item2))] = nt.Item2.ToString(); }
                            catch { /* skip */ }
                        }
                }
                catch { /* reference table unreadable â€” leave numeric */ }
                _refNameCache[key] = map;
            }
            return map.TryGetValue(id, out var n) ? n : null;
        }

        // RouterOS .id is the "*HEX" handle form. The M2 record id is a numeric u8/u32.
        private static string FormatId(object value)
        {
            if (value == null) return "*0";
            try { return "*" + Convert.ToUInt64(value).ToString("X"); }
            catch { return value.ToString(); }
        }

        private static string FormatValue(string wireType, object value)
        {
            if (value == null) return "";
            if (wireType == "bool") return (value is bool b && b) ? "true" : "false";
            return value.ToString();
        }

        // â”€â”€ Writes â€” Phase F2 (set / add / remove / move) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <inheritdoc/>
        internal override string RunAdd(TikCommandDescriptor descriptor)
        {
            EnsureNativeOpen();
            // descriptor.CommandText is "/path/add"; the resolution path is the parent.
            string apiPath = StripVerb(descriptor.CommandText);
            var (handler, resolver) = ResolveHandlerAndFields(apiPath);

            var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId: true);
            try
            {
                int newId = _ops.Add(handler, fields);
                return newId >= 0 ? "*" + ((uint)newId).ToString("X") : null;
            }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
        }

        /// <inheritdoc/>
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
        {
            EnsureNativeOpen();
            string verb = VerbOf(descriptor.CommandText);
            string apiPath = StripVerb(descriptor.CommandText);
            var (handler, resolver) = ResolveHandlerAndFields(apiPath);

            try { RunVerb(verb, apiPath, handler, resolver, descriptor); }
            catch (WinboxM2OperationException ex) { throw TranslateM2Error(ex, descriptor.CommandText); }
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
                    throw new NotSupportedException(
                        $"WinBox native: command verb '{verb}' on '{apiPath}' is not supported. " +
                        $"Use a WinboxCli or Api connection.");
            }
        }

        /// <inheritdoc/>
        internal override string RunScalarGet(string cliText)
            => throw new NotSupportedException(
                "WinBox native scalar get is not implemented yet. Use ExecuteList/LoadAll, or a WinboxCli/Api connection.");

        // â”€â”€ Streaming monitor (ExecuteAsync / LoadAsync) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Native WinBox M2 supports streaming monitors (<c>.jg</c> <c>type:'query'</c> / poll-action windows),
        /// so it reports <see cref="TikConnectionCapability.Listen"/> on top of <see cref="TikConnectionCapability.Crud"/>.
        /// </summary>
        public override TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen;

        /// <summary>
        /// Runs a streaming-monitor command (e.g. <c>/tool/torch</c>, <c>/tool/profile</c>) on a background
        /// worker that polls the router every <c>autorefresh</c> ms over the normal M2 channel â€” start â†’ poll â†’
        /// cancel (webfig <c>ObjectQuery</c>; see <c>_notes/winbox-native-m2-plan.md</c> Â§20). Each polled record
        /// is decoded to API field names and pushed to <paramref name="onRow"/>; <paramref name="onDone"/> fires
        /// when the worker stops (cancelled, the router's "finished" flag, or an error â€” reported via
        /// <paramref name="onError"/>). Request parameters (NameValue) are encoded as the monitor's request fields.
        /// </summary>
        /// <remarks>The worker owns the M2 channel while polling; issuing concurrent CRUD on the same connection
        /// from another thread while a native monitor is active is not supported (the transport is request/reply).</remarks>
        TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            EnsureNativeOpen();
            string verb = VerbOf(descriptor.CommandText);

            // /path/listen â€” RouterOS pushes add/change/delete deltas over the API. WinBox M2 has no server
            // push, so webfig (and we) emulate it the way it polls live config tables: getall on a timer and
            // diff snapshots by .id (see _notes/winbox-native-m2-plan.md Â§20). Deleted rows are surfaced as a
            // synthetic ".dead=true" record so the O/R LoadListenAsync handler routes them to onDeleted.
            if (verb == "listen")
            {
                string listPath = StripVerb(descriptor.CommandText);
                var printDescriptor = new TikCommandDescriptor(listPath + "/print", descriptor.Parameters);
                // Diff config fields only — runtime counters (ro:1: rx-byte, link-downs, …) tick every poll and
                // would otherwise make every row look "changed", whereas RouterOS listen emits on real changes.
                var volatileFields = ReadOnlyFieldNames(listPath);
                return StartWorker("winbox-native-listen",
                    handle => ListenLoop(printDescriptor, volatileFields, handle, onRow, onError, onDone));
            }

            // /path/print (LoadAsync) â€” a one-shot async list, not a streaming window: run the print off the
            // calling thread, emit each row, then complete. No monitor cycle is involved.
            if (verb == "print" || verb == "getall")
            {
                return StartWorker("winbox-native-asynclist",
                    handle => AsyncListOnce(descriptor, handle, onRow, onError, onDone));
            }

            // Otherwise a streaming-monitor window (/tool/torch, /tool/profile, /interface/monitor-traffic, â€¦).
            // The monitor path is the command path itself or carries a trailing verb; try the plain path first,
            // then the verb-stripped parent.
            string apiPath = ApiPathOf(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(apiPath);
            WinboxMonitorSpec spec = _catalog.GetMonitorByHandler(handler);
            if (spec == null)
            {
                string parent = StripVerb(descriptor.CommandText);
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

            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath));
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            return StartWorker("winbox-native-monitor",
                handle => MonitorLoop(spec, descriptor, resolver, keyToName, keyToField, handle, onRow, onError, onDone));
        }

        // Spins up a background worker bound to a fresh TikMonitorHandle and returns the handle.
        private TikMonitorHandle StartWorker(string name, Action<TikMonitorHandle> body)
        {
            var handle = new TikMonitorHandle();
            var worker = new Thread(() => body(handle)) { IsBackground = true, Name = name };
            handle.AttachThread(worker);
            worker.Start();
            return handle;
        }

        // The monitor worker: encode request fields â†’ start â†’ poll loop (emit decoded rows, sleep autorefresh,
        // honour cancel/finished) â†’ cancel. Request-field encoding runs here (not on the caller) so a runtime
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
                    // Encode the caller's NameValue parameters as the monitor's request fields (interface, cpu, â€¦).
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
                        onRow?.Invoke(new TikRecordSentence(DecodeRecord(rec, keyToName, keyToField)));

                    if (done) break;

                    // Sleep the autorefresh interval in short slices so Cancel is responsive.
                    int slept = 0, interval = Math.Max(100, spec.AutorefreshMs);
                    while (slept < interval && !handle.CancelRequested) { Thread.Sleep(50); slept += 50; }
                }
            }
            catch (WinboxM2OperationException ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message, $"0x{ex.Code:X}", ex.ErrorText));
            }
            catch (Exception ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
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

        // A monitor worker is "stopping" (so a transport error is expected, not reported) when the caller
        // cancelled or the connection was closed out from under the poll — both are graceful, not failures.
        private bool MonitorStopping(TikMonitorHandle handle) => handle.CancelRequested || !IsOpened;

        // Async one-shot list (LoadAsync on a /print path): one getall off-thread, emit rows, complete.
        private void AsyncListOnce(TikCommandDescriptor descriptor, TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                IList<TikRecordSentence> rows;
                _cmdLock.Wait();
                try { rows = RunPrint(descriptor); }
                finally { _cmdLock.Release(); }

                foreach (var row in rows)
                {
                    if (handle.CancelRequested) break;
                    onRow?.Invoke(row);
                }
            }
            catch (WinboxM2OperationException ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message, $"0x{ex.Code:X}", ex.ErrorText));
            }
            catch (Exception ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally { onDone?.Invoke(); }
        }

        // /listen emulation: poll the table and diff snapshots by .id. The first pass seeds silently (RouterOS
        // listen only pushes future deltas, never replays the table); afterwards an added/changed row is emitted
        // as itself, and a vanished .id as a synthetic ".dead=true" record. onDone fires once when cancelled.
        private void ListenLoop(TikCommandDescriptor printDescriptor, ICollection<string> volatileFields,
            TikMonitorHandle handle,
            Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
        {
            try
            {
                var lastSig = new Dictionary<string, string>(StringComparer.Ordinal); // .id â†’ row signature
                bool seeded = false;
                while (!handle.CancelRequested)
                {
                    IList<TikRecordSentence> rows;
                    _cmdLock.Wait();
                    try { rows = RunPrint(printDescriptor); }
                    finally { _cmdLock.Release(); }

                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in rows)
                    {
                        string rid = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                        if (rid == null) continue;
                        seen.Add(rid);
                        string sig = RowSignature(row, volatileFields);
                        bool isNewOrChanged = !lastSig.TryGetValue(rid, out var prev) || prev != sig;
                        lastSig[rid] = sig;
                        if (seeded && isNewOrChanged)
                            onRow?.Invoke(row);
                    }

                    if (seeded)
                        foreach (var goneId in lastSig.Keys.Where(k => !seen.Contains(k)).ToList())
                        {
                            lastSig.Remove(goneId);
                            onRow?.Invoke(new TikRecordSentence(new Dictionary<string, string>
                            {
                                { TikSpecialProperties.Id, goneId },
                                { ".dead", "true" },
                            }));
                        }
                    seeded = true;

                    int slept = 0;
                    while (slept < 1000 && !handle.CancelRequested) { Thread.Sleep(50); slept += 50; }
                }
            }
            catch (WinboxM2OperationException ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message, $"0x{ex.Code:X}", ex.ErrorText));
            }
            catch (Exception ex)
            {
                if (!MonitorStopping(handle)) onError?.Invoke(new TikTrapSentenceResult(ex.Message));
            }
            finally { onDone?.Invoke(); }
        }

        // Canonical signature of a record's config fields (sorted key=value), used to detect changes between
        // listen polls. Volatile runtime fields (ro:1 counters/status) are excluded so the diff reflects real
        // config/identity changes, not every counter tick.
        private static string RowSignature(TikRecordSentence row, ICollection<string> volatileFields)
        {
            return string.Join("|", row.Words
                .Where(kv => volatileFields == null || !volatileFields.Contains(kv.Key))
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value));
        }

        // The set of read-only (ro:1) field names for a table's handler — the volatile runtime fields a listen
        // diff must ignore. Empty when the path has no handler/catalog entry (then all fields are compared).
        private HashSet<string> ReadOnlyFieldNames(string apiPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int[] handler = _handlerMap.Resolve(apiPath);
            if (handler == null) return set;
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath));
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            foreach (var kv in keyToField)
                if (kv.Value != null && kv.Value.ReadOnly && keyToName.TryGetValue(kv.Key, out var n))
                    set.Add(n);
            return set;
        }

        // Evaluate RouterOS query filters as a postfix stack: each ?name=value pushes (field == value), ?name
        // pushes (field present), ?<name=v / ?>name=v push a comparison; ?#| / ?#& pop two and push OR/AND,
        // ?#! pops one and negates. Whatever predicates remain on the stack at the end are implicitly ANDed
        // (an empty stack — no filters — matches everything). Mirrors RouterOS-side query evaluation.
        private static bool MatchesQueryStack(TikRecordSentence row, IReadOnlyList<ITikCommandParameter> filters)
        {
            var stack = new Stack<bool>();
            foreach (var f in filters)
            {
                string name = f.Name;
                if (name == "#|") { bool a = Pop(stack), b = Pop(stack); stack.Push(a || b); }
                else if (name == "#&") { bool a = Pop(stack), b = Pop(stack); stack.Push(a && b); }
                else if (name == "#!") { stack.Push(!Pop(stack)); }
                else if (name.StartsWith("#")) { /* unsupported stack op — leave stack unchanged */ }
                else if (name.StartsWith(".") && name != TikSpecialProperties.Id) { stack.Push(true); }
                else stack.Push(EvalPredicate(row, name, f.Value));
            }
            return stack.All(b => b);
        }

        private static bool Pop(Stack<bool> s) => s.Count > 0 && s.Pop();

        private static bool EvalPredicate(TikRecordSentence row, string name, string value)
        {
            char op = name.Length > 0 ? name[0] : '=';
            string field = (op == '<' || op == '>') ? name.Substring(1) : name;
            bool has = row.TryGetResponseField(field, out var v);
            if (op == '<' || op == '>')
            {
                if (!has) return false;
                if (double.TryParse(v, out var dv) && double.TryParse(value, out var dq))
                    return op == '<' ? dv < dq : dv > dq;
                int cmp = string.CompareOrdinal(v, value);
                return op == '<' ? cmp < 0 : cmp > 0;
            }
            // existence (?name) vs equality (?name=value)
            if (string.IsNullOrEmpty(value))
                return has && !string.IsNullOrEmpty(v);
            return has && string.Equals(v, value, StringComparison.Ordinal);
        }

        // â”€â”€ Write helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private (int[] handler, WinboxFieldResolver resolver) ResolveHandlerAndFields(string apiPath)
        {
            int[] handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
                // Surface an unmapped write path the same way reads do â€” as "no such command" â€” so callers
                // get a consistent exception type across read and write (e.g. invalid path, or a path under a
                // package the router lacks).
                var c = new TikGenericCommand(this, apiPath);
                throw new TikNoSuchCommandException(c, new TikTrapSentenceResult(
                    $"WinBox native: no M2 handler mapping for path '{apiPath}'. " +
                    $"Add one via connection.PathOverride(\"{apiPath}\", new[]{{maj,min}}) " +
                    $"or use a WinboxCli connection."));
            }
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath));
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

            return new TikCommandUnexpectedResponseException(ex.Message, cmd, trap);
        }

        // Encode every NameValue parameter (except client-side markers and, optionally, .id) into M2 fields.
        // Read-only fields (per .jg) are skipped by the encoder (returns no bytes). A network field expands
        // to two entries (address + mask); a dynamic enum reference is resolved nameâ†’id via getall.
        private List<byte[]> EncodeNameValueFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId,
            bool allowReadOnly = false)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter) continue;
                if (p.Name.StartsWith(".") && p.Name != TikSpecialProperties.Id) continue; // .proplist/.tag/â€¦
                if (p.Name == TikSpecialProperties.Id) { if (skipId) continue; }
                if (p.Name == "move-before" || p.Name == "destination") continue; // handled by move dest
                fields.AddRange(resolver.EncodeField(p.Name, p.Value, ResolveReference, allowReadOnly));
            }
            return fields;
        }

        // Resolves a dynamic enum reference (the referenced table handler + a friendly name) to that
        // record's numeric M2 id, by listing the referenced table and matching its 'name' field.
        private int? ResolveReference(int[] refHandler, string name)
        {
            var refResolver = new WinboxFieldResolver(null, refHandler, _catalog, EmptyOverrides);
            int id = FindIdByName(refHandler, refResolver, name);
            return id >= 0 ? (int?)id : null;
        }

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        // Resolve the M2 numeric record id from the command's .id parameter. The .id may be the RouterOS
        // "*HEX" handle form, or a friendly name (e.g. "ether1") â€” names are resolved via getall.
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
                int byName = FindIdByName(handler, resolver, idParam);
                if (byName >= 0) return byName;
            }

            if (required)
            {
                // The set/remove/move target does not exist (unresolvable .id) â€” same outcome as the API/CLI
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
            int byName = FindIdByName(handler, resolver, dest);
            return byName; // -1 if not found â†’ move to end
        }

        // Look up an M2 record id whose 'name' field equals the given value (used to map friendly names).
        private int FindIdByName(int[] handler, WinboxFieldResolver resolver, string name)
        {
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            foreach (var rec in _ops.GetAll(handler))
            {
                var decoded = DecodeRecord(rec, keyToName, keyToField);
                if (decoded.TryGetValue("name", out var nm) && string.Equals(nm, name, StringComparison.Ordinal)
                    && decoded.TryGetValue(TikSpecialProperties.Id, out var idStr)
                    && idStr.StartsWith("*") &&
                    int.TryParse(idStr.Substring(1), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int id))
                    return id;
            }
            return -1;
        }

        private static string FindParam(TikCommandDescriptor descriptor, string name)
        {
            foreach (var p in descriptor.Parameters)
                if (p.Name == name) return p.Value;
            return null;
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // "/interface/print" â†’ "/interface"; strips the trailing read verb segment.
        private static string ApiPathOf(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText)) return "";
            string p = commandText.Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            int lastSlash = p.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string verb = p.Substring(lastSlash + 1).ToLowerInvariant();
                // strip only known read-ish verb segments; keep deeper paths intact otherwise
                if (verb == "print" || verb == "getall" || verb == "get")
                    p = p.Substring(0, lastSlash);
            }
            return p.TrimEnd('/');
        }

        // Last path segment, lower-cased: "/interface/set" â†’ "set".
        private static string VerbOf(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText)) return "print";
            var segs = commandText.Trim().TrimStart('/').Split('/');
            return segs[segs.Length - 1].ToLowerInvariant();
        }

        // Strip the trailing write-verb segment: "/interface/set" â†’ "/interface".
        private static string StripVerb(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText)) return "";
            string p = commandText.Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            int lastSlash = p.LastIndexOf('/');
            if (lastSlash > 0) p = p.Substring(0, lastSlash);
            return p.TrimEnd('/');
        }
    }
}
