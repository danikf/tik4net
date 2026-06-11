using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Winbox
{
    /// <summary>
    /// Resolves between RouterOS API field names and numeric WinBox M2 field keys for one handler.
    /// Stability split (per the design): the volatile <c>key↔type</c> mapping comes from the live
    /// <c>.jg</c> catalog (<see cref="WinboxJgCatalog"/>); the stable <c>label↔apiName</c> mapping comes
    /// from a normalizer plus protocol-constant seeds plus session overrides.
    /// </summary>
    /// <remarks>
    /// For the F1 read milestone the important direction is <c>key → apiName</c> (decoding records back
    /// to API field names). The forward direction (<c>apiName → key</c>, for writes) is also exposed for F2.
    /// Ambiguity (an API name that maps to two different keys) throws a clear exception telling the caller
    /// to add a session field override or use a <c>WinboxCli</c> connection instead.
    /// </remarks>
    internal sealed class WinboxFieldResolver
    {
        private readonly int[] _handler;
        private readonly string _apiPath;
        private readonly WinboxJgCatalog _catalog;
        // session overrides apiName → key (highest priority)
        private readonly IReadOnlyDictionary<string, int> _overrides;

        internal WinboxFieldResolver(string apiPath, int[] handler, WinboxJgCatalog catalog,
            IReadOnlyDictionary<string, int> overrides)
        {
            _apiPath = apiPath;
            _handler = handler;
            _catalog = catalog;
            _overrides = overrides ?? new Dictionary<string, int>();
        }

        // ── Protocol-constant seed (stable, hardcoded) ─────────────────────────
        // Well-known system keys shared by all config tables. apiName → key.
        private static readonly Dictionary<string, int> SeedByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [".id"]     = WinboxM2Protocol.RecordKey.Id,      // 0xFE0001
            ["comment"] = WinboxM2Protocol.RecordKey.Comment, // 0xFE0009
            ["name"]    = WinboxM2Protocol.RecordKey.Name,    // 0x10006
        };

        // ── key → apiName (decode records) ─────────────────────────────────────

        /// <summary>
        /// Builds the <c>key → apiName</c> map for this handler by inverting the seed table, the
        /// <c>.jg</c> catalog fields, and the session overrides (overrides and seeds win over the catalog).
        /// </summary>
        internal IReadOnlyDictionary<int, string> BuildKeyToApiName()
        {
            var map = new Dictionary<int, string>();

            // 1. catalog fields (lowest priority)
            var jg = _catalog?.GetHandlerFields(_handler);
            if (jg != null)
                foreach (var f in jg.Values)
                    if (!map.ContainsKey(f.Key)) map[f.Key] = f.ApiName;

            // 2. protocol-constant seeds (override catalog — these are authoritative)
            foreach (var kv in SeedByName)
                map[kv.Value] = kv.Key;

            // 3. session overrides (highest priority)
            foreach (var kv in _overrides)
                map[kv.Value] = kv.Key;

            return map;
        }

        // ── apiName → key (forward; for writes / filters) ──────────────────────

        /// <summary>
        /// Resolves an API field name to its M2 key. Throws <see cref="WinboxFieldResolutionException"/>
        /// when the name is unknown or ambiguous, with guidance to add a session override or use WinboxCli.
        /// </summary>
        internal int ResolveKey(string apiName)
        {
            if (_overrides.TryGetValue(apiName, out int ov)) return ov;
            if (SeedByName.TryGetValue(apiName, out int seed)) return seed;

            var jg = _catalog?.GetHandlerFields(_handler);
            if (jg != null && jg.TryGetValue(apiName, out var f)) return f.Key;

            throw new WinboxFieldResolutionException(
                $"WinBox native: cannot resolve API field '{apiName}' on '{_apiPath}' to an M2 key. " +
                $"Add a session field override (connection.FieldOverride(\"{_apiPath}\", \"{apiName}\", key)) " +
                $"or use a WinboxCli connection instead.");
        }

        // ── Field encode (writes) ──────────────────────────────────────────────

        /// <summary>
        /// Encodes an API field write (<paramref name="apiName"/> = <paramref name="value"/>) into its M2
        /// wire field bytes, using the resolved key and the <c>.jg</c> wire type (string/u32/bool/raw/addr,
        /// with enum string→numeric when the field carries a static value map). Returns <c>null</c> when the
        /// field is read-only in the catalog (read-only fields are not sent). Throws
        /// <see cref="WinboxFieldResolutionException"/> when the name cannot be resolved.
        /// </summary>
        internal byte[] EncodeField(string apiName, string value)
        {
            int key = ResolveKey(apiName);

            // Look up the .jg field (wire type, ro, enum map). Seeds (.id/comment/name) have none — they
            // default to string, which is correct for comment/name.
            WinboxJgField jg = null;
            _catalog?.GetHandlerFields(_handler)?.TryGetValue(apiName, out jg);

            if (jg != null && jg.ReadOnly) return null;

            string wireType = jg?.WireType ?? "string";
            value = value ?? "";

            // enum: encode the API string to its numeric index (falls through to the type encoder if the
            // value is already numeric or no map matches).
            if (jg?.EnumMap != null)
            {
                foreach (var kv in jg.EnumMap)
                    if (string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase))
                        return EncodeU32(key, kv.Key);
            }

            switch (wireType)
            {
                case "bool":
                    return M2Message.BoolSys(key, ParseBool(value));
                case "u32":
                case "u64":
                case "i32":
                case "dur":
                case "time":
                    if (long.TryParse(value, out long n))
                        return EncodeU32(key, n);
                    return M2Message.StringSys(key, value); // non-numeric (e.g. "auto") — send as string
                case "raw":
                    return M2Message.RawSys(key, ParseRaw(value));
                case "addr":
                case "ip6":
                    return M2Message.StringSys(key, value); // ip/ip6 round-trip as string text
                default: // "string" and unknowns
                    return M2Message.StringSys(key, value);
            }
        }

        private static byte[] EncodeU32(int key, long n)
            => (n >= 0 && n <= 255) ? M2Message.U8Sys(key, (byte)n) : M2Message.U32Sys(key, (int)n);

        private static bool ParseBool(string v)
            => v == "true" || v == "yes" || v == "1" ||
               string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);

        // Parse a MAC ("AA:BB:..."/"AABB...") or hex blob into raw bytes.
        private static byte[] ParseRaw(string v)
        {
            string hex = (v ?? "").Replace(":", "").Replace("-", "").Replace(" ", "");
            if (hex.Length % 2 != 0) return Encoding.UTF8.GetBytes(v ?? "");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                if (!byte.TryParse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out bytes[i]))
                    return Encoding.UTF8.GetBytes(v ?? "");
            return bytes;
        }

        // ── label normalizer (stable text) ─────────────────────────────────────

        // Irregular WinBox labels whose plain normalization does not match the API field name.
        private static readonly Dictionary<string, string> LabelOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mac address"]     = "mac-address",
            ["mtu"]             = "mtu",
            ["actual mtu"]      = "actual-mtu",
            ["l2 mtu"]          = "l2mtu",
            ["arp"]             = "arp",
            ["tx"]              = "tx-byte",
            ["rx"]              = "rx-byte",
        };

        /// <summary>
        /// Normalizes a WinBox UI label to a RouterOS API field name: trims, lower-cases, collapses
        /// whitespace to single '-', and applies a small irregular-label override map.
        /// </summary>
        internal static string NormalizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            string trimmed = label.Trim();
            if (LabelOverride.TryGetValue(trimmed, out var ovr)) return ovr;

            var sb = new StringBuilder(trimmed.Length);
            bool lastDash = false;
            foreach (char c in trimmed)
            {
                if (char.IsWhiteSpace(c) || c == '_')
                {
                    if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastDash = false;
                }
            }
            return sb.ToString().Trim('-');
        }
    }

    /// <summary>
    /// Thrown when the WinBox native field resolver cannot unambiguously map an API field name to an
    /// M2 key. The message tells the caller how to recover (session override or a WinboxCli connection).
    /// </summary>
    public sealed class WinboxFieldResolutionException : TikConnectionException
    {
        /// <summary>.ctor</summary>
        public WinboxFieldResolutionException(string message) : base(message) { }
    }
}
