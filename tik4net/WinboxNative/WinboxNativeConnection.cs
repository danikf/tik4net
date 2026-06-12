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
    /// <para>Listen/Streaming/Async are not supported (capability <see cref="TikConnectionCapability.Crud"/>).</para>
    /// </remarks>
    public sealed class WinboxNativeConnection : TikCommandConnectionBase
    {
        /// <summary>Default WinBox TCP port.</summary>
        public const int DefaultPort = 8291;

        /// <summary>
        /// Login timeout in milliseconds — the maximum time to wait for authentication / first M2 reply.
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
        // apiPath → (apiName → key) session field overrides
        private readonly Dictionary<string, Dictionary<string, int>> _fieldOverrides =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        private WinboxM2Session _session;
        private WinboxNativeM2Operations _ops;
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
        /// Adds a session override mapping an API path to a WinBox M2 handler array
        /// (e.g. <c>/ppp/secret</c> → <c>[20, 12]</c>). Takes priority over the seed table.
        /// </summary>
        public void PathOverride(string apiPath, int[] handler)
        {
            _handlerMap.AddOverride(apiPath, handler);
        }

        // ── Open / Close ───────────────────────────────────────────────────────

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
            try { _routerVersion = _ops.GetRouterVersion(); }
            catch { _routerVersion = null; }
            try { _catalog.EnsureLoaded(session, _routerVersion, CatalogCachePath, ConnectTimeout); }
            catch { /* catalog is best-effort; seeds + normalizer still work */ }
            // Feed the .jg-derived apiPath→handler map into the handler resolver (after session overrides,
            // before the shipped override tail).
            _handlerMap.SetDerivedPaths(_catalog.GetDerivedPaths());
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

        // ── Native read overrides ───────────────────────────────────────────────

        /// <inheritdoc/>
        internal override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
        {
            EnsureNativeOpen();

            string apiPath = ApiPathOf(descriptor.CommandText);
            int[] handler = _handlerMap.Resolve(apiPath);
            if (handler == null)
            {
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

            var rows = new List<TikRecordSentence>(records.Count);
            foreach (var rec in records)
                rows.Add(new TikRecordSentence(DecodeRecord(rec, keyToName, keyToField)));

            // Apply Filter parameters (?name=value) in-memory — RouterOS-side filtering is not used here.
            // .id is kept (LoadById issues ?.id=*HEX); other "." / "#" control words are dropped.
            var filters = descriptor.Parameters
                .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter
                            && (p.Name == TikSpecialProperties.Id
                                || (!p.Name.StartsWith("#") && !p.Name.StartsWith("."))))
                .ToList();
            if (filters.Count > 0)
                rows = rows.Where(r => filters.All(f =>
                    r.TryGetResponseField(f.Name, out var v) &&
                    string.Equals(v, f.Value, StringComparison.Ordinal))).ToList();

            return rows;
        }

        /// <summary>
        /// Translates a decoded M2 record (<c>key → (wireType, value)</c>) into a RouterOS API field
        /// dictionary (<c>apiName → stringValue</c>). Unknown keys are dropped; <c>.id</c> is emitted as
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
                            return addr + "/" + WinboxFieldResolver.MaskToPrefix(mt.Item2);
                        return addr;
                    }
                    case "macaddr":
                        return WinboxFieldResolver.MacFromBytes(value);
                    case "set":
                    {
                        // Bitmask flag set → comma-joined labels (.jg map key = bit index). The opt/not flag
                        // keys are consumed separately in DecodeRecord, so only the value rides here.
                        if (jf.EnumMap == null) break;
                        long bits;
                        try { bits = Convert.ToInt64(value); } catch { break; }
                        var labels = jf.EnumMap.Where(kv => (bits & (1L << kv.Key)) != 0)
                            .OrderBy(kv => kv.Key).Select(kv => kv.Value);
                        return string.Join(",", labels);
                    }
                }
                // dynamic enum reference: render the referenced object's name (e.g. interface id → "ether1").
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
                    catch { /* not numeric — fall through */ }
                }
            }
            return FormatValue(wireType, value);
        }

        // id → name cache per referenced table, built lazily from one getall. Names are stable enough within
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
                catch { /* reference table unreadable — leave numeric */ }
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

        // ── Writes — Phase F2 (set / add / remove / move) ──────────────────────

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
        // to two entries (address + mask); a dynamic enum reference is resolved name→id via getall.
        private List<byte[]> EncodeNameValueFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter) continue;
                if (p.Name.StartsWith(".") && p.Name != TikSpecialProperties.Id) continue; // .proplist/.tag/…
                if (p.Name == TikSpecialProperties.Id) { if (skipId) continue; }
                if (p.Name == "move-before" || p.Name == "destination") continue; // handled by move dest
                fields.AddRange(resolver.EncodeField(p.Name, p.Value, ResolveReference));
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
                int byName = FindIdByName(handler, resolver, idParam);
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
            int byName = FindIdByName(handler, resolver, dest);
            return byName; // -1 if not found → move to end
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

        // ── Helpers ─────────────────────────────────────────────────────────────

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

        // "/interface/print" → "/interface"; strips the trailing read verb segment.
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

        // Last path segment, lower-cased: "/interface/set" → "set".
        private static string VerbOf(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText)) return "print";
            var segs = commandText.Trim().TrimStart('/').Split('/');
            return segs[segs.Length - 1].ToLowerInvariant();
        }

        // Strip the trailing write-verb segment: "/interface/set" → "/interface".
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
