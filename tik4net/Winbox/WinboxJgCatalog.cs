using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net.Winbox
{
    /// <summary>
    /// Loads and parses the WinBox <c>.jg</c> plugin catalogs (tolerant JS object literals) and exposes,
    /// per M2 handler path, the field map <c>apiName → (key, wireType, ro)</c>. The <c>.jg</c> files are the
    /// version-volatile source of truth for field keys/types; <see cref="WinboxFieldResolver"/> layers a
    /// stable label↔apiName normalizer and overrides on top.
    /// </summary>
    /// <remarks>
    /// <para>Source: live fetch over the WinBox mproxy file handler (<c>[2,2]</c> cmd=3, gzip-compressed
    /// <c>&lt;name&gt;.jg.gz</c>), cached to <c>&lt;CatalogCachePath&gt;/&lt;routerVersion&gt;/&lt;name&gt;.jg</c>.</para>
    /// <para>The parser is a C# port of <c>_notes/WinboxMessage/jg_analyze.py</c> — it walks the object tree
    /// and, for every node carrying a <c>path:[…]</c>, attributes the enclosing field <c>id:'&lt;prefix&gt;&lt;hex&gt;'</c>
    /// entries to that handler. Multiple windows may target the same handler; their fields are merged.</para>
    /// </remarks>
    internal sealed class WinboxJgCatalog
    {
        // handler-path-string ("20,0") → (apiName → field)
        private readonly Dictionary<string, Dictionary<string, WinboxJgField>> _byHandler =
            new Dictionary<string, Dictionary<string, WinboxJgField>>(StringComparer.Ordinal);

        // derived API path (e.g. "/ip/address") → handler array, harvested from the menu tree:
        // every type:'map'/'query' WINDOW node contributes its path:[…] handler, keyed by the
        // normalized (enclosing group + node name). First window wins for a given derived path.
        private readonly Dictionary<string, int[]> _derivedPaths =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        // id-prefix letter → wire-type name (lower=scalar, upper=array). Mirrors jg_analyze.py PREFIX.
        private static readonly Dictionary<char, string> Prefix = new Dictionary<char, string>
        {
            ['u'] = "u32",    ['U'] = "u32[]",
            ['s'] = "string", ['S'] = "string[]",
            ['b'] = "bool",   ['B'] = "bool[]",
            ['r'] = "raw",    ['R'] = "raw[]",
            ['m'] = "addr",   ['M'] = "addr[]",
            ['a'] = "ip6",    ['A'] = "ip6[]",
            ['x'] = "u64",    ['X'] = "u64[]",
            ['q'] = "u64",    ['i'] = "i32", ['d'] = "dur", ['t'] = "time",
        };

        /// <summary>True once at least one <c>.jg</c> window has been parsed into the catalog.</summary>
        internal bool HasData => _byHandler.Count > 0;

        /// <summary>
        /// Returns the field map for <paramref name="handler"/> as <c>apiName → field</c>, or <c>null</c>
        /// when no window targeting that handler was found in the catalog.
        /// </summary>
        internal IReadOnlyDictionary<string, WinboxJgField> GetHandlerFields(int[] handler)
        {
            return _byHandler.TryGetValue(HandlerKey(handler), out var map) ? map : null;
        }

        /// <summary>
        /// API paths derived from the <c>.jg</c> menu tree: <c>derivedApiPath → handler</c>.
        /// Built from every <c>type:'map'/'query'</c> window node (its <c>path:[…]</c> handler keyed by
        /// the normalized enclosing <c>group</c> + node <c>name</c>). Dropdown references
        /// (<c>type:'enm'</c>) that reuse a handler are not included.
        /// </summary>
        internal IReadOnlyDictionary<string, int[]> GetDerivedPaths() => _derivedPaths;

        // ── Loading ────────────────────────────────────────────────────────────

        /// <summary>
        /// Ensures the catalog is populated: loads cached <c>.jg</c> for <paramref name="routerVersion"/> from
        /// <paramref name="cacheDir"/> if present, otherwise fetches via mproxy and caches. Failures are
        /// tolerated — the resolver falls back to its seed table and the normalizer.
        /// </summary>
        internal void EnsureLoaded(IWinboxM2Channel channel, string routerVersion, string cacheDir, int timeoutMs)
        {
            if (HasData) return;

            string versionDir = null;
            if (!string.IsNullOrEmpty(cacheDir) && !string.IsNullOrEmpty(routerVersion))
            {
                try
                {
                    versionDir = Path.Combine(cacheDir, SanitizeVersion(routerVersion));
                    if (Directory.Exists(versionDir))
                    {
                        foreach (var fp in Directory.GetFiles(versionDir, "*.jg"))
                            TryParseInto(File.ReadAllText(fp, Encoding.UTF8));
                        if (HasData) return;
                    }
                }
                catch { /* cache read is best-effort */ }
            }

            // Live fetch: roteros.jg holds the core config windows (interface, ip, …).
            foreach (var name in new[] { "roteros.jg" })
            {
                try
                {
                    string text = FetchJg(channel, name, timeoutMs);
                    if (string.IsNullOrEmpty(text)) continue;
                    TryParseInto(text);
                    if (versionDir != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(versionDir);
                            File.WriteAllText(Path.Combine(versionDir, name), text, new UTF8Encoding(false));
                        }
                        catch { /* cache write is best-effort */ }
                    }
                }
                catch { /* one plugin failing must not break read */ }
            }
        }

        private void TryParseInto(string text)
        {
            try
            {
                object tree = new JgParser(text).Parse();
                Walk(tree, null, null);
            }
            catch { /* tolerant: skip unparsable plugin */ }
        }

        // ── mproxy .jg fetch (gzip <name>.jg.gz over [2,2] cmd=3) ──────────────

        private static string FetchJg(IWinboxM2Channel channel, string name, int timeoutMs)
        {
            // The on-disk file in /home/web/webfig/ is "<name>.jg.gz" (gzip), served by the
            // mproxy static handler via cmd=7 (NOT cmd=3 = /var/pckg, which CHR denies).
            // Try cmd=7 on the .gz name first (proven path), then fall back to cmd=3 / plain.
            int handle = MproxyOpen(channel, name + ".gz", 7, timeoutMs);
            if (handle < 0) handle = MproxyOpen(channel, name, 7, timeoutMs);
            if (handle < 0) handle = MproxyOpen(channel, name + ".gz", 3, timeoutMs);
            if (handle < 0) handle = MproxyOpen(channel, name, 3, timeoutMs);
            if (handle < 0) return null;

            byte[] raw = MproxyRead(channel, handle, timeoutMs);
            if (raw == null || raw.Length == 0) return null;

            // .jg.gz is gzip; a plain .jg is already text. Detect the gzip magic.
            if (raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B)
                raw = GzipDecompress(raw);
            return Encoding.UTF8.GetString(raw);
        }

        private static int MproxyOpen(IWinboxM2Channel channel, string filename, int openCmd, int timeoutMs)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true), channel.NextReqIdField(),
                M2Message.U8Sys(0xFF0007, (byte)openCmd),  // 7 = /home/web/webfig static, 3 = /var/pckg
                M2Message.StringUser(1, filename));
            byte[] resp = channel.SendReceive(msg, timeoutMs);
            try { return M2Message.ParseSessionId(resp); }
            catch { return -1; }
        }

        private const int MproxyChunk = 32768;

        private static byte[] MproxyRead(IWinboxM2Channel channel, int handle, int timeoutMs)
        {
            var all = new List<byte>();
            for (int guard = 0; guard < 256; guard++)
            {
                byte[] msg = M2Message.BuildM2(
                    M2Message.SysToArr(2, 2), M2Message.SysFrom(),
                    M2Message.BoolSys(0xFF0005, true), channel.NextReqIdField(),
                    M2Message.SessionIdField(handle),
                    M2Message.U32User(2, MproxyChunk),
                    M2Message.U8Sys(0xFF0007, 4));      // cmd=4 = read chunk
                byte[] resp = channel.SendReceive(msg, timeoutMs);
                byte[] chunk = ExtractMproxyChunk(resp);
                if (chunk == null || chunk.Length == 0) break;
                all.AddRange(chunk);
                if (chunk.Length < MproxyChunk) break;
            }
            return all.Count > 0 ? all.ToArray() : null;
        }

        // Pull the raw/string file-content field (user namespace) out of a read response.
        private static byte[] ExtractMproxyChunk(byte[] resp)
        {
            if (resp == null || resp.Length < 2 || resp[0] != 'M' || resp[1] != '2') return resp;
            int pos = 2;
            while (pos + 4 <= resp.Length)
            {
                int ns = resp[pos + 2], type = resp[pos + 3];
                pos += 4;
                if (ns == 0x00 && (type == 0x31 || type == 0x30 || type == 0x21 || type == 0x20))
                {
                    int len = (type == 0x31 || type == 0x21)
                        ? resp[pos++]
                        : (int)BitConverter.ToUInt16(resp, (pos += 2) - 2);
                    if (len > 0 && pos + len <= resp.Length)
                        return resp.Skip(pos).Take(len).ToArray();
                }
                pos += M2Message.SkipTypeBytes(type, resp, pos);
            }
            return null;
        }

        private static byte[] GzipDecompress(byte[] compressed)
        {
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
        }

        // ── Tree walk + field extraction (port of jg_analyze.py walk) ──────────

        private void Walk(object node, string handlerKey, string group)
        {
            if (node is Dictionary<string, object> dict)
            {
                string owner = handlerKey;
                int[] handlerInts = null;
                if (dict.TryGetValue("path", out var pv) && pv is List<object> pathList && pathList.Count > 0)
                {
                    var ints = new List<int>();
                    bool ok = true;
                    foreach (var p in pathList)
                    {
                        if (p is int pi) ints.Add(pi);
                        else { ok = false; break; }
                    }
                    if (ok) { handlerInts = ints.ToArray(); owner = HandlerKey(handlerInts); }
                }

                // A menu may carry a 'group' (e.g. group:'IP'); it scopes its descendant windows.
                string childGroup = group;
                if (dict.TryGetValue("group", out var gv) && gv is string gs && gs.Length > 0)
                    childGroup = gs;

                // Harvest derived API path → handler for WINDOW nodes only (type:'map'/'query' with a
                // handler path). Dropdown references (type:'enm',values:{type:'dynamic',path:…}) reuse the
                // same handler but are NOT windows — skip them.
                if (handlerInts != null
                    && dict.TryGetValue("type", out var tyv) && tyv is string ty
                    && (ty == "map" || ty == "query"))
                {
                    string nodeName = (dict.TryGetValue("name", out var nnv) && nnv is string nns && nns.Length > 0)
                        ? nns
                        : (dict.TryGetValue("title", out var ntv) && ntv is string nts ? nts : "");
                    string apiPath = DeriveApiPath(group, nodeName);
                    if (apiPath != null && !_derivedPaths.ContainsKey(apiPath))
                        _derivedPaths[apiPath] = handlerInts;
                }

                if (owner != null && dict.TryGetValue("id", out var idv) && idv is string idStr)
                {
                    var dec = DecodeId(idStr);
                    if (dec != null)
                    {
                        string label = (dict.TryGetValue("name", out var nv) && nv is string ns && ns.Length > 0)
                            ? ns
                            : (dict.TryGetValue("title", out var tv) && tv is string ts ? ts : "");
                        bool ro = dict.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
                        AddField(owner, label, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(dict));
                    }
                }

                foreach (var kv in dict)
                {
                    if (kv.Key == "id") continue;
                    Walk(kv.Value, owner, childGroup);
                }
            }
            else if (node is List<object> list)
            {
                foreach (var it in list)
                    Walk(it, handlerKey, group);
            }
        }

        // Build "/ip/address" from group='IP' + nodeName='Address', or "/interface" from a bare
        // nodeName='Interface'. Segments are lower-cased with spaces→'-'. Returns null for empty names.
        private static string DeriveApiPath(string group, string nodeName)
        {
            string leaf = WinboxFieldResolver.NormalizeLabel(nodeName);
            if (string.IsNullOrEmpty(leaf)) return null;
            string prefix = WinboxFieldResolver.NormalizeLabel(group);
            return string.IsNullOrEmpty(prefix) ? "/" + leaf : "/" + prefix + "/" + leaf;
        }

        private void AddField(string handlerKey, string label, int key, string wireType, bool ro,
            IReadOnlyDictionary<int, string> enumMap)
        {
            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return;
            if (!_byHandler.TryGetValue(handlerKey, out var map))
            {
                map = new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase);
                _byHandler[handlerKey] = map;
            }
            // first label wins for a given apiName; do not let later, less-specific windows clobber it.
            if (!map.ContainsKey(apiName))
                map[apiName] = new WinboxJgField(apiName, key, wireType, ro, enumMap);
        }

        // Pulls a static enum value list (values:{type:'static',map:['off','on',…]}) → {0:'off',1:'on',…}.
        // Only the array form is captured (index = numeric value); object-form maps are skipped.
        private static IReadOnlyDictionary<int, string> ExtractEnumMap(Dictionary<string, object> node)
        {
            if (!node.TryGetValue("values", out var vv) || !(vv is Dictionary<string, object> vals)) return null;
            if (!vals.TryGetValue("map", out var mv) || !(mv is List<object> arr)) return null;
            var map = new Dictionary<int, string>();
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is string s) map[i] = WinboxFieldResolver.NormalizeLabel(s);
            return map.Count > 0 ? map : null;
        }

        // 's10006' → (key=0x10006, type="string"); prefix is exactly one leading letter, rest is hex.
        private static (int key, string type)? DecodeId(string idStr)
        {
            var m = Regex.Match(idStr, "^([a-zA-Z])([0-9a-fA-F]+)$");
            if (!m.Success) return null;
            char pfx = m.Groups[1].Value[0];
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int key))
                return null;
            string type = Prefix.TryGetValue(pfx, out var t) ? t : "?";
            return (key, type);
        }

        internal static string HandlerKey(int[] handler) => string.Join(",", handler);

        private static string SanitizeVersion(string v)
        {
            var sb = new StringBuilder(v.Length);
            foreach (char c in v)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
            return sb.ToString();
        }

        // ── Tolerant JS-object-literal parser (port of jg_analyze.py JgParser) ─
        // Produces nested Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / int.
        private sealed class JgParser
        {
            private readonly string _s;
            private int _i;
            private readonly int _n;

            internal JgParser(string text) { _s = text ?? ""; _n = _s.Length; }

            internal object Parse() { Ws(); return Value(); }

            private void Ws() { while (_i < _n && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n')) _i++; }

            private object Value()
            {
                Ws();
                if (_i >= _n) return null;
                char c = _s[_i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '\'' || c == '"') return Str();
                return Scalar();
            }

            private Dictionary<string, object> Obj()
            {
                _i++; // {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                Ws();
                if (_i < _n && _s[_i] == '}') { _i++; return d; }
                while (_i < _n)
                {
                    Ws();
                    string key;
                    if (_i < _n && (_s[_i] == '\'' || _s[_i] == '"'))
                        key = Str();
                    else
                    {
                        int start = _i;
                        while (_i < _n && ":,}]".IndexOf(_s[_i]) < 0 && !char.IsWhiteSpace(_s[_i])) _i++;
                        key = _s.Substring(start, _i - start);
                    }
                    Ws();
                    if (_i >= _n || _s[_i] != ':') throw new FormatException("expected ':'");
                    _i++;
                    object val = Value();
                    d[key] = val; // last duplicate wins (we only read scalar leaves we care about)
                    Ws();
                    if (_i < _n && _s[_i] == ',')
                    {
                        _i++; Ws();
                        if (_i < _n && _s[_i] == '}') { _i++; return d; } // trailing comma
                        continue;
                    }
                    if (_i < _n && _s[_i] == '}') { _i++; return d; }
                    throw new FormatException("obj parse error");
                }
                return d;
            }

            private List<object> Arr()
            {
                _i++; // [
                var a = new List<object>();
                Ws();
                if (_i < _n && _s[_i] == ']') { _i++; return a; }
                while (_i < _n)
                {
                    a.Add(Value());
                    Ws();
                    if (_i < _n && _s[_i] == ',')
                    {
                        _i++; Ws();
                        if (_i < _n && _s[_i] == ']') { _i++; return a; }
                        continue;
                    }
                    if (_i < _n && _s[_i] == ']') { _i++; return a; }
                    throw new FormatException("arr parse error");
                }
                return a;
            }

            private string Str()
            {
                char q = _s[_i]; _i++;
                var sb = new StringBuilder();
                while (_i < _n)
                {
                    char c = _s[_i];
                    if (c == '\\') { if (_i + 1 < _n) sb.Append(_s[_i + 1]); _i += 2; continue; }
                    if (c == q) { _i++; return sb.ToString(); }
                    sb.Append(c); _i++;
                }
                throw new FormatException("unterminated string");
            }

            private object Scalar()
            {
                int start = _i;
                while (_i < _n && ",}]".IndexOf(_s[_i]) < 0 && !char.IsWhiteSpace(_s[_i])) _i++;
                string tok = _s.Substring(start, _i - start);
                if (Regex.IsMatch(tok, "^-?\\d+$") && int.TryParse(tok, out int iv))
                    return iv;
                return tok;
            }
        }
    }
}
