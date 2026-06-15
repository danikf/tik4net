using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Winbox
{
    /// <summary>
    /// Translates decoded WinBox M2 records (numeric <c>key → (wireType, value)</c>) into RouterOS API field
    /// dictionaries (<c>apiName → stringValue</c>), applying the version-matched <c>.jg</c> UI-semantic types
    /// (IPs, networks, MACs, static enums, dynamic enum references). Pure decode logic split out of
    /// <c>WinboxNativeConnection</c> so it can be reasoned about and unit-tested without the connection.
    /// Depends only on the M2 operations channel (for dynamic-reference name lookups) and the <c>.jg</c> catalog.
    /// </summary>
    internal sealed class WinboxRecordCodec
    {
        private readonly WinboxNativeM2Operations _ops;
        private readonly WinboxJgCatalog _catalog;

        // id → name cache per referenced table, built lazily from one getall. Names are stable enough within a
        // session; this avoids a getall per referenced field per row.
        private readonly Dictionary<string, Dictionary<int, string>> _refNameCache =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        internal WinboxRecordCodec(WinboxNativeM2Operations ops, WinboxJgCatalog catalog)
        {
            _ops = ops;
            _catalog = catalog;
        }

        /// <summary>
        /// Translates a decoded M2 record (<c>key → (wireType, value)</c>) into a RouterOS API field
        /// dictionary (<c>apiName → stringValue</c>). Unknown keys are dropped; <c>.id</c> is emitted as
        /// the RouterOS <c>*HEX</c> handle form so it round-trips through the O/R mapper.
        /// </summary>
        internal Dictionary<string, string> DecodeRecord(
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
                                // range:1 → the maskid sibling is the range-END address, not a netmask. A single
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
                        // Bitmask flag set → comma-joined labels (.jg map key = bit index). The opt/not flag
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
    }
}
