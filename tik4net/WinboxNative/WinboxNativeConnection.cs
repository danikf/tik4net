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

            // Singleton tables (type:'item' window, e.g. /system/resource, /ip/dns) expose a single record
            // read via get-singleton; everything else lists via getall.
            List<Dictionary<int, Tuple<string, object>>> records;
            if (_catalog.IsSingletonHandler(handler))
            {
                var one = _ops.GetSingleton(handler);
                records = (one != null && one.Count > 0)
                    ? new List<Dictionary<int, Tuple<string, object>>> { one }
                    : new List<Dictionary<int, Tuple<string, object>>>();
            }
            else
            {
                records = _ops.GetAll(handler);
            }

            var rows = new List<TikRecordSentence>(records.Count);
            foreach (var rec in records)
                rows.Add(new TikRecordSentence(DecodeRecord(rec, keyToName)));

            // Apply Filter parameters (?name=value) in-memory — RouterOS-side filtering is not used here.
            var filters = descriptor.Parameters
                .Where(p => p.ParameterFormat == TikCommandParameterFormat.Filter
                            && !p.Name.StartsWith("#") && !p.Name.StartsWith("."))
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
        private static Dictionary<string, string> DecodeRecord(
            Dictionary<int, Tuple<string, object>> rec, IReadOnlyDictionary<int, string> keyToName)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rec)
            {
                if (!keyToName.TryGetValue(kv.Key, out var apiName)) continue;
                if (fields.ContainsKey(apiName)) continue;

                if (apiName == TikSpecialProperties.Id)
                    fields[apiName] = FormatId(kv.Value.Item2);
                else
                    fields[apiName] = FormatValue(kv.Value.Item1, kv.Value.Item2);
            }
            return fields;
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

            var fields = EncodeNameValueFields(descriptor, resolver, skipId: true);
            int newId = _ops.Add(handler, fields);
            return newId >= 0 ? "*" + ((uint)newId).ToString("X") : null;
        }

        /// <inheritdoc/>
        internal override void RunNonQuery(TikCommandDescriptor descriptor)
        {
            EnsureNativeOpen();
            string verb = VerbOf(descriptor.CommandText);
            string apiPath = StripVerb(descriptor.CommandText);
            var (handler, resolver) = ResolveHandlerAndFields(apiPath);

            switch (verb)
            {
                case "set":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    var fields = EncodeNameValueFields(descriptor, resolver, skipId: true);
                    _ops.Set(handler, id, fields);
                    break;
                }
                case "enable":
                case "disable":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    var f = resolver.EncodeField("disabled", verb == "disable" ? "true" : "false");
                    _ops.Set(handler, id, f != null ? new List<byte[]> { f } : new List<byte[]>());
                    break;
                }
                case "unset":
                {
                    int id = ResolveRecordId(handler, resolver, descriptor, required: true);
                    // unset = set the named field(s) back to empty/default.
                    var fields = EncodeNameValueFields(descriptor, resolver, skipId: true);
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
            int[] handler = _handlerMap.Resolve(apiPath)
                ?? throw new NotSupportedException(
                    $"WinBox native: no M2 handler mapping for path '{apiPath}'. " +
                    $"Add one via connection.PathOverride(\"{apiPath}\", new[]{{maj,min}}) " +
                    $"or use a WinboxCli connection.");
            var resolver = new WinboxFieldResolver(apiPath, handler, _catalog, OverridesFor(apiPath));
            return (handler, resolver);
        }

        // Encode every NameValue parameter (except client-side markers and, optionally, .id) into M2 fields.
        // Read-only fields (per .jg) are skipped by the encoder (returns null).
        private static List<byte[]> EncodeNameValueFields(
            TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                if (p.ParameterFormat == TikCommandParameterFormat.Filter) continue;
                if (p.Name.StartsWith(".") && p.Name != TikSpecialProperties.Id) continue; // .proplist/.tag/…
                if (p.Name == TikSpecialProperties.Id) { if (skipId) continue; }
                if (p.Name == "move-before" || p.Name == "destination") continue; // handled by move dest
                byte[] enc = resolver.EncodeField(p.Name, p.Value);
                if (enc != null) fields.Add(enc);
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
                int byName = FindIdByName(handler, resolver, idParam);
                if (byName >= 0) return byName;
            }

            if (required)
                throw new NotSupportedException(
                    $"WinBox native: could not resolve record .id '{idParam}' on handler " +
                    $"[{string.Join(",", handler)}]. Provide the *HEX .id or a matching name.");
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
            foreach (var rec in _ops.GetAll(handler))
            {
                var decoded = DecodeRecord(rec, keyToName);
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
