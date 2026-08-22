using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Winbox
{
    /// <summary>
    /// Builds and parses WinBox M2 TLV messages.
    /// Wire format: <c>b'M2'</c> followed by concatenated TLV fields.
    /// </summary>
    /// <remarks>
    /// Each field: <c>[key_lo][key_hi][namespace][type][data…]</c>. Namespace 0x00 = user fields
    /// (1-based key), 0xFF = system fields, 0xFE = session fields. Shared by the TCP transport
    /// (<see cref="WinboxM2Session"/>) and — later — the MAC transport (chapter H).
    /// </remarks>
    internal static class M2Message
    {
        // ── TLV building ──────────────────────────────────────────────────────

        // M2 message = b'M2' + concatenated TLV fields
        internal static byte[] BuildM2(params byte[][] fields)
            => new byte[] { (byte)'M', (byte)'2' }.Concat(fields.SelectMany(f => f)).ToArray();

        // SYS_TO u32_array: key=(0x01,0x00,0xFF)
        internal static byte[] SysToArr(params int[] ids)
        {
            var b = new List<byte> { 0x01, 0x00, 0xFF, 0x88 };
            b.AddRange(BitConverter.GetBytes((ushort)ids.Length));
            foreach (int id in ids) b.AddRange(BitConverter.GetBytes((uint)id));
            return b.ToArray();
        }

        // SYS_FROM u32_array [0, srcId]: key=(0x02,0x00,0xFF)
        internal static byte[] SysFrom(int srcId = 8)
        {
            var b = new List<byte> { 0x02, 0x00, 0xFF, 0x88, 0x02, 0x00 };
            b.AddRange(BitConverter.GetBytes((uint)0));
            b.AddRange(BitConverter.GetBytes((uint)srcId));
            return b.ToArray();
        }

        // Bool field for system keys
        internal static byte[] BoolSys(int fullKey, bool val)
            => new byte[] { (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                            (byte)((fullKey >> 16) & 0xFF), val ? (byte)0x01 : (byte)0x00 };

        // u8 field for system keys
        internal static byte[] U8Sys(int fullKey, byte val)
            => new byte[] { (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                            (byte)((fullKey >> 16) & 0xFF), 0x09, val };

        // u32 field for system keys (used when value > 255)
        internal static byte[] U32Sys(int fullKey, int val)
        {
            var b = new List<byte> { (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                                     (byte)((fullKey >> 16) & 0xFF), 0x08 };
            b.AddRange(BitConverter.GetBytes((uint)val));
            return b.ToArray();
        }

        // u64 field (ftype 2 → type byte 0x10, eight little-endian bytes). NOT interchangeable with the u32
        // form: RouterOS reads the type byte, and a u64 field written as u32 is accepted and IGNORED — the
        // write reports success and the value on the router does not move. That is how /queue/simple's rate
        // fields were unwritable over native while resolving to the right keys.
        internal static byte[] U64Sys(int fullKey, ulong val)
        {
            var b = new List<byte> { (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                                     (byte)((fullKey >> 16) & 0xFF), 0x10 };
            b.AddRange(BitConverter.GetBytes(val));
            return b.ToArray();
        }

        // Nested message field (ftype 5): key + size-flagged type (0x29 1-byte len / 0x28 2-byte / 0x2A 4-byte)
        // + body. The body is a full submessage ('M2' + concatenated sub-field TLVs), parsed by ParseAllFields.
        // Used by compound webfig types such as 'addr' (IPv4 rides as u32 at 0xFEFF20 inside the field's object).
        internal static byte[] MessageSys(int fullKey, params byte[][] subFields)
        {
            var body = new List<byte> { (byte)'M', (byte)'2' };
            foreach (var f in subFields) if (f != null) body.AddRange(f);

            var b = new List<byte> { (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF), (byte)((fullKey >> 16) & 0xFF) };
            if (body.Count <= 0xFF) { b.Add(0x29); b.Add((byte)body.Count); }
            else if (body.Count <= 0xFFFF) { b.Add(0x28); b.Add((byte)(body.Count & 0xFF)); b.Add((byte)((body.Count >> 8) & 0xFF)); }
            else { b.Add(0x2A); b.AddRange(BitConverter.GetBytes((uint)body.Count)); }
            b.AddRange(body);
            return b.ToArray();
        }

        // SESSION_ID: key=(0x01,0x00,0xFE). RouterOS returns the mepty session id as a u32 when it
        // exceeds 255 (e.g. 265), so encode the same way: u8 (0x09) for small ids, u32 (0x08) otherwise.
        // Sending it back truncated to a single byte addresses the wrong session ("No SESSION_ID" /
        // dead terminal).
        internal static byte[] SessionIdField(int id) => SessionIdField(unchecked((uint)id));

        /// <summary>SESSION_ID/.id encoder taking the handle as a <see cref="uint"/> — the on-wire type. The
        /// id is genuinely a u32 (a streaming-monitor handle can exceed <see cref="int.MaxValue"/>, e.g. the
        /// Profile monitor's 0xFFFFFFFD), so callers that hold a u32 id should use this overload directly rather
        /// than round-tripping through a signed cast.</summary>
        internal static byte[] SessionIdField(uint id)
        {
            if (id <= 255)
                return new byte[] { 0x01, 0x00, 0xFE, 0x09, (byte)id };
            var b = new List<byte> { 0x01, 0x00, 0xFE, 0x08 };
            b.AddRange(BitConverter.GetBytes(id));
            return b.ToArray();
        }

        // String field, user namespace (key_id in 0x00 namespace)
        internal static byte[] StringUser(int keyId, string value)
        {
            byte kl = (byte)(keyId & 0xFF), kh = (byte)((keyId >> 8) & 0xFF);
            byte[] data = Encoding.UTF8.GetBytes(value);
            if (data.Length <= 255)
                return new byte[] { kl, kh, 0x00, 0x21, (byte)data.Length }.Concat(data).ToArray();
            var b = new List<byte> { kl, kh, 0x00, 0x20 };
            b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            b.AddRange(data);
            return b.ToArray();
        }

        // u32 field, user namespace
        internal static byte[] U32User(int keyId, int val)
        {
            byte kl = (byte)(keyId & 0xFF), kh = (byte)((keyId >> 8) & 0xFF);
            return new byte[] { kl, kh, 0x00, 0x08 }.Concat(BitConverter.GetBytes((uint)val)).ToArray();
        }

        // Raw bytes field, user namespace
        internal static byte[] RawUser(int keyId, byte[] data)
        {
            byte kl = (byte)(keyId & 0xFF), kh = (byte)((keyId >> 8) & 0xFF);
            if (data.Length <= 255)
                return new byte[] { kl, kh, 0x00, 0x31, (byte)data.Length }.Concat(data).ToArray();
            var b = new List<byte> { kl, kh, 0x00, 0x30 };
            b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            b.AddRange(data);
            return b.ToArray();
        }

        // String field for a full key (any namespace, e.g. 0xFE0009 = comment).
        internal static byte[] StringSys(int fullKey, string value)
        {
            byte[] data = Encoding.UTF8.GetBytes(value ?? "");
            byte kl = (byte)(fullKey & 0xFF), kh = (byte)((fullKey >> 8) & 0xFF), ns = (byte)((fullKey >> 16) & 0xFF);
            if (data.Length <= 255)
                return new byte[] { kl, kh, ns, 0x21, (byte)data.Length }.Concat(data).ToArray();
            var b = new List<byte> { kl, kh, ns, 0x20 };
            b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            b.AddRange(data);
            return b.ToArray();
        }

        // Raw bytes field for a full key (any namespace, e.g. mac/ip encoded as raw bytes).
        internal static byte[] RawSys(int fullKey, byte[] data)
        {
            data = data ?? new byte[0];
            byte kl = (byte)(fullKey & 0xFF), kh = (byte)((fullKey >> 8) & 0xFF), ns = (byte)((fullKey >> 16) & 0xFF);
            if (data.Length <= 255)
                return new byte[] { kl, kh, ns, 0x31, (byte)data.Length }.Concat(data).ToArray();
            var b = new List<byte> { kl, kh, ns, 0x30 };
            b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            b.AddRange(data);
            return b.ToArray();
        }

        /// <summary>
        /// IPv6 address field (webfig ftype 3, <c>FT_ADDR6</c> — type byte <c>0x18</c>): sixteen raw bytes
        /// with NO length prefix, unlike every other variable-width type.
        /// </summary>
        /// <remarks>
        /// It has its own field type; an IPv6 value is NOT a <c>raw</c> field. Sending one as raw puts a
        /// length byte where the router expects the first address byte, and the router answers as though the
        /// field had never been sent — an IPv6 ping comes back "no address was specified". Read from <c>master*.js</c>: <c>case'a':writeId(FT_ADDR6,r);for(let i=0;i&lt;16;++i)
        /// {arr[pos++]=val[i];}</c>.
        /// </remarks>
        internal static byte[] Addr6Sys(int fullKey, byte[] addr16)
        {
            if (addr16 == null || addr16.Length != 16)
                throw new ArgumentException("An FT_ADDR6 field is exactly 16 bytes.", nameof(addr16));
            byte kl = (byte)(fullKey & 0xFF), kh = (byte)((fullKey >> 8) & 0xFF), ns = (byte)((fullKey >> 16) & 0xFF);
            return new byte[] { kl, kh, ns, 0x18 }.Concat(addr16).ToArray();
        }

        // u32 array field for a system key (namespace 0xFF or 0xFE).
        internal static byte[] U32ArraySys(int fullKey, params int[] values)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                (byte)((fullKey >> 16) & 0xFF), 0x88
            };
            b.AddRange(BitConverter.GetBytes((ushort)values.Length));
            foreach (int v in values) b.AddRange(BitConverter.GetBytes((uint)v));
            return b.ToArray();
        }

        /// <summary>
        /// u64-array field (webfig ftype 18, <c>FT_U64_ARRAY</c> — type byte <c>0x90</c>): a 16-bit element
        /// COUNT followed by eight bytes per element, with no per-element length.
        /// </summary>
        /// <remarks>
        /// The counterpart of <see cref="U32ArraySys"/> one width wider. webfig's <c>id2int</c> gives the
        /// <c>.jg</c> prefix <c>Q</c> this ftype, which is what every <c>multibignumber</c> field is declared
        /// as.
        /// </remarks>
        internal static byte[] U64ArraySys(int fullKey, IList<ulong> values)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                (byte)((fullKey >> 16) & 0xFF), 0x90
            };
            b.AddRange(BitConverter.GetBytes((ushort)values.Count));
            foreach (ulong v in values) b.AddRange(BitConverter.GetBytes(v));
            return b.ToArray();
        }

        /// <summary>
        /// String-array field (webfig ftype 20, <c>FT_STRING_ARRAY</c> — type byte <c>0xA0</c>): a 16-bit
        /// element COUNT, then each element as a 16-bit byte length followed by its UTF-8 bytes.
        /// </summary>
        /// <remarks>
        /// Read from <c>master*.js</c>: <c>writeId(FT_STRING_ARRAY,r);write16(val.length);for(…)
        /// {write16(val[i].length);…}</c>. The count and the element lengths share one width — the reader
        /// takes both from the type's size flags — so the normal (non-short, non-long) form is 2 bytes for
        /// each. An empty array is a valid value and the only way to CLEAR the field.
        /// </remarks>
        internal static byte[] StringArraySys(int fullKey, IList<string> values)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                (byte)((fullKey >> 16) & 0xFF), 0xA0
            };
            b.AddRange(BitConverter.GetBytes((ushort)values.Count));
            foreach (string v in values)
            {
                byte[] data = Encoding.UTF8.GetBytes(v ?? "");
                b.AddRange(BitConverter.GetBytes((ushort)data.Length));
                b.AddRange(data);
            }
            return b.ToArray();
        }

        /// <summary>
        /// Raw-array field (webfig ftype 22, <c>FT_RAW_ARRAY</c> — type byte <c>0xB0</c>): the same shape as
        /// <see cref="StringArraySys"/>, with each element's own bytes instead of text.
        /// </summary>
        /// <remarks>
        /// webfig's writer has the two widths the wrong way round in this one case (it writes a 32-bit
        /// element length in the SHORT branch and a 16-bit one in the long branch), while its reader takes
        /// both from the type's size flags like every other array. The reader is what RouterOS agrees with,
        /// so both widths are 2 bytes here.
        /// </remarks>
        internal static byte[] RawArraySys(int fullKey, IList<byte[]> values)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                (byte)((fullKey >> 16) & 0xFF), 0xB0
            };
            b.AddRange(BitConverter.GetBytes((ushort)values.Count));
            foreach (byte[] v in values)
            {
                byte[] data = v ?? new byte[0];
                b.AddRange(BitConverter.GetBytes((ushort)data.Length));
                b.AddRange(data);
            }
            return b.ToArray();
        }

        /// <summary>
        /// IPv6-array field (webfig ftype 19, <c>FT_ADDR6_ARRAY</c> — type byte <c>0x98</c>): a 16-bit
        /// element count followed by sixteen raw bytes per element, with NO per-element length — the same
        /// fixed-width rule the scalar <see cref="Addr6Sys"/> follows.
        /// </summary>
        internal static byte[] Addr6ArraySys(int fullKey, IList<byte[]> values)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                (byte)((fullKey >> 16) & 0xFF), 0x98
            };
            b.AddRange(BitConverter.GetBytes((ushort)values.Count));
            foreach (byte[] v in values)
            {
                if (v == null || v.Length != 16)
                    throw new ArgumentException("An FT_ADDR6 element is exactly 16 bytes.", nameof(values));
                b.AddRange(v);
            }
            return b.ToArray();
        }

        /// <summary>
        /// Message-array field (webfig ftype 21, <c>FT_MESSAGE_ARRAY</c> — type byte <c>0xA8</c>): a 16-bit
        /// element count, then each element as a 16-bit byte length followed by a complete submessage
        /// (<c>'M2'</c> + its own TLVs) — the write side of <see cref="ParseRecords"/>.
        /// </summary>
        internal static byte[] MessageArraySys(int fullKey, IList<byte[][]> elements)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF),
                (byte)((fullKey >> 16) & 0xFF), 0xA8
            };
            b.AddRange(BitConverter.GetBytes((ushort)elements.Count));
            foreach (byte[][] fields in elements)
            {
                byte[] body = BuildM2(fields);
                b.AddRange(BitConverter.GetBytes((ushort)body.Length));
                b.AddRange(body);
            }
            return b.ToArray();
        }

        // u32 array, user namespace
        internal static byte[] U32ArrayUser(int keyId, params int[] values)
        {
            byte kl = (byte)(keyId & 0xFF), kh = (byte)((keyId >> 8) & 0xFF);
            var b = new List<byte> { kl, kh, 0x00, 0x88 };
            b.AddRange(BitConverter.GetBytes((ushort)values.Length));
            foreach (int v in values) b.AddRange(BitConverter.GetBytes((uint)v));
            return b.ToArray();
        }

        // ── Diagnostics ───────────────────────────────────────────────────────

        /// <summary>
        /// Renders an M2 message (request or reply) as a single compact, human-readable line for
        /// row-level tracing (<c>OnReadRow</c>/<c>OnWriteRow</c>). Shows the message length and every
        /// top-level field as <c>0x&lt;fullKey&gt;=&lt;type&gt;:&lt;value&gt;</c>; nested submessages and
        /// record arrays are expanded so the raw M2 field keys of each record are visible — which is the
        /// information needed to debug the native field mapping. Non-M2 / empty buffers are reported as
        /// a short hex preview.
        /// </summary>
        internal static string Describe(byte[] m2)
        {
            if (m2 == null) return "(null)";
            if (m2.Length < 2 || m2[0] != 'M' || m2[1] != '2')
            {
                int n = Math.Min(m2.Length, 16);
                string hex = n > 0 ? BitConverter.ToString(m2, 0, n).Replace("-", "") : "";
                return $"({m2.Length}B non-M2{(n < m2.Length ? "…" : "")}: {hex})";
            }

            var sb = new StringBuilder();
            sb.Append("M2[").Append(m2.Length).Append("B]");
            foreach (var kv in ParseAllFields(m2))
                sb.Append(' ').Append(RenderTraceKey(kv.Key)).Append('=')
                  .Append(kv.Value.Item1).Append(':').Append(RenderTraceValue(kv.Value.Item2));
            return sb.ToString();
        }

        // A key the parser had to qualify (two fields, one key — see ParseAllFields) would otherwise print as
        // 0x2000012, which is not a key the router ever sent. Render the wire key with the qualifier as a
        // suffix, so a trace can be matched against the .jg id ('u12'/'U12') by eye.
        private static string RenderTraceKey(int key)
            => WinboxM2Protocol.TypedKey.IsQualified(key)
                ? "0x" + WinboxM2Protocol.TypedKey.WireKeyOf(key).ToString("X")
                    + ((key & WinboxM2Protocol.TypedKey.Array) != 0 ? "~arr" : "~sca")
                : "0x" + key.ToString("X");

        // Compact renderer for a decoded M2 value: nested record dicts → {0xKEY=…,…}, record arrays →
        // [{…},{…}], scalars → ToString(). Depth-capped to keep trace lines bounded.
        private static string RenderTraceValue(object value, int depth = 0)
        {
            switch (value)
            {
                case null:
                    return "";
                case Dictionary<int, Tuple<string, object>> rec:
                {
                    if (depth >= 3) return "{…}";
                    var sb = new StringBuilder("{");
                    bool first = true;
                    foreach (var kv in rec)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(RenderTraceKey(kv.Key)).Append('=')
                          .Append(RenderTraceValue(kv.Value.Item2, depth + 1));
                    }
                    return sb.Append('}').ToString();
                }
                case List<Dictionary<int, Tuple<string, object>>> list:
                {
                    if (depth >= 3) return $"[{list.Count} rec]";
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(RenderTraceValue(list[i], depth + 1));
                    }
                    return sb.Append(']').ToString();
                }
                case byte[] bytes:
                    // An FT_ADDR6 value is a byte array; ToString() renders it as "System.Byte[]", which in a
                    // wire trace hides exactly the bytes the trace exists to show.
                    return BitConverter.ToString(bytes).Replace("-", "");
                default:
                    return value.ToString()!; // 'case null' above already excluded null
            }
        }

        // ── TLV parsing ───────────────────────────────────────────────────────

        // ── M2 type-byte decomposition (matches webfig master.js) ─────────────
        // The 4th header byte = (ftype<<3) | sizeFlags, where sizeFlags = FS_SHORT(0x01) | FS_LONG(0x02).
        // ftype categories (from webfig): 0=bool 1=u32 2=u64 3=addr6 4=string 5=message 6=raw
        //   16=bool[] 17=u32[] 18=u64[] 19=addr6[] 20=string[] 21=message[] 22=raw[]
        // Length/count encoding (webfig readLen): always 1 byte; +1 if NOT short; +2 more if long.
        //   → normal=2B, short=1B, long=4B.
        internal static int FType(int type) => type >> 3;
        private static bool IsShort(int type) => (type & 0x01) != 0;
        private static bool IsLong(int type) => (type & 0x02) != 0;

        // Read a length/count using the type's size flags; advances pos.
        private static int ReadLen(int type, byte[] d, ref int pos)
        {
            int len = pos < d.Length ? d[pos++] : 0;
            if (!IsShort(type) && pos < d.Length) len |= d[pos++] << 8;
            if (IsLong(type)) { if (pos < d.Length) len |= d[pos++] << 16; if (pos < d.Length) len |= d[pos++] << 24; }
            return len;
        }

        // Parse a message-array field (ftype 21) into a list of record field-dicts.
        // Each element on the wire is a full submessage ('M2' + TLVs). Returns the records
        // found under <paramref name="fullKey"/>, or an empty list if not present / wrong type.
        // This is the critical "records under 0xFE0002 (Mfe0002)" path used by getall/get-one.
        internal static List<Dictionary<int, Tuple<string, object>>> ParseRecords(byte[] m2, int fullKey)
        {
            var records = new List<Dictionary<int, Tuple<string, object>>>();
            if (m2 == null || m2.Length < 2 || m2[0] != 'M' || m2[1] != '2') return records;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos + 1], ns = m2[pos + 2], type = m2[pos + 3];
                int key = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                if (key == fullKey && FType(type) == 21)
                {
                    int cnt = ReadLen(type, m2, ref pos);
                    for (int i = 0; i < cnt && pos < m2.Length; i++)
                    {
                        int elen = ReadLen(type, m2, ref pos);
                        if (pos + elen > m2.Length) break;
                        records.Add(ParseAllFields(m2.Skip(pos).Take(elen).ToArray()));
                        pos += elen;
                    }
                    return records;
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            return records;
        }

        // Parse all TLV fields from an M2 message into a dict keyed by full_key.
        // Values: bool→bool, u8→byte, u32→uint, u64→ulong, str/str_l→string, raw→hex string,
        //   str[]→"[a,b,…]" string, message→nested dict, message-array→List&lt;dict&gt;.
        internal static Dictionary<int, Tuple<string, object>> ParseAllFields(byte[] m2)
        {
            var result = new Dictionary<int, Tuple<string, object>>();
            if (m2 == null || m2.Length < 2 || m2[0] != 'M' || m2[1] != '2') return result;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos + 1], ns = m2[pos + 2], type = m2[pos + 3];
                int fullKey = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                // The dictionary value type is object (not object?) across every consumer of ParseAllFields,
                // several of which are out of this file's scope. When a field's declared length runs past the
                // end of a truncated frame, the branches below leave val at this placeholder instead of a real
                // decoded value; the suppression documents that rather than widening the shared contract.
                object val = null!;
                string typeName = "?";
                switch (type)
                {
                    case 0x00: typeName = "bool"; val = false; break;
                    case 0x01: typeName = "bool"; val = true; break;
                    case 0x09:
                        typeName = "u8"; val = pos < m2.Length ? (object)m2[pos] : null!; pos += 1; break;
                    case 0x08:
                        typeName = "u32";
                        if (pos + 4 <= m2.Length) { val = BitConverter.ToUInt32(m2, pos); pos += 4; }
                        break;
                    case 0x10:
                        typeName = "u64";
                        if (pos + 8 <= m2.Length) { val = BitConverter.ToUInt64(m2, pos); pos += 8; }
                        break;
                    case 0x88:
                        typeName = "u32[]";
                        if (pos + 2 <= m2.Length)
                        {
                            int cnt = BitConverter.ToUInt16(m2, pos); pos += 2;
                            var arr = new uint[cnt];
                            for (int i = 0; i < cnt && pos + 4 <= m2.Length; i++, pos += 4)
                                arr[i] = BitConverter.ToUInt32(m2, pos);
                            val = "[" + string.Join(",", arr) + "]";
                        }
                        break;
                    case 0x90: case 0x91: case 0x92:
                    {
                        // FT_U64_ARRAY (ftype 18): count, then eight bytes per element and no per-element
                        // length — the same counted-array shape u32[] has, one width wider. Rendered as the
                        // same "[a,b,…]" text every other scalar array is, so the codec's list formatter
                        // reads it without a case of its own.
                        //
                        // Without this case the field fell to `default:` and was SKIPPED. SkipTypeBytes has
                        // always known the layout (0x90/0x91/0x92 → CountedArrayBytes(…, 8)), so the frame
                        // stayed in sync and nothing looked wrong; the value simply never reached anyone.
                        typeName = "u64[]";
                        int cnt = ReadLen(type, m2, ref pos);
                        var nums = new List<ulong>();
                        for (int i = 0; i < cnt && pos + 8 <= m2.Length; i++, pos += 8)
                            nums.Add(BitConverter.ToUInt64(m2, pos));
                        val = "[" + string.Join(",", nums) + "]";
                        break;
                    }
                    case 0x18:
                        // FT_ADDR6: 16 raw bytes, no length prefix (see Addr6Sys).
                        typeName = "ip6";
                        if (pos + 16 <= m2.Length) { val = m2.Skip(pos).Take(16).ToArray(); pos += 16; }
                        break;
                    case 0x21:
                        typeName = "str";
                        if (pos < m2.Length) { int l = m2[pos++]; val = Encoding.UTF8.GetString(m2, pos, Math.Min(l, m2.Length - pos)); pos += l; }
                        break;
                    case 0x20:
                        typeName = "str_l";
                        if (pos + 2 <= m2.Length) { int l = BitConverter.ToUInt16(m2, pos); pos += 2; val = Encoding.UTF8.GetString(m2, pos, Math.Min(l, m2.Length - pos)); pos += l; }
                        break;
                    case 0x31:
                        typeName = "raw";
                        if (pos < m2.Length) { int l = m2[pos++]; val = BitConverter.ToString(m2, pos, Math.Min(l, m2.Length - pos)).Replace("-", ""); pos += l; }
                        break;
                    case 0x30:
                        typeName = "raw_l";
                        if (pos + 2 <= m2.Length) { int l = BitConverter.ToUInt16(m2, pos); pos += 2; val = $"[{l}B]"; pos += l; }
                        break;
                    case 0xA0:
                        // str_array: 2B count + (2B len + data) per entry
                        typeName = "str[]";
                        if (pos + 1 < m2.Length)
                        {
                            int cnt = BitConverter.ToUInt16(m2, pos); pos += 2;
                            var strs = new List<string>();
                            for (int i = 0; i < cnt && pos + 1 < m2.Length; i++)
                            {
                                int slen = BitConverter.ToUInt16(m2, pos); pos += 2;
                                if (pos + slen <= m2.Length)
                                {
                                    strs.Add(Encoding.UTF8.GetString(m2, pos, slen));
                                    pos += slen;
                                }
                            }
                            val = "[" + string.Join(",", strs) + "]";
                        }
                        break;
                    case 0x98:
                    {
                        // addr6[] (ftype 19): count + 16 fixed bytes per element, no per-element length.
                        // Rendered like every other scalar array, one element per comma, so the codec's
                        // list formatter can hand each 16-byte address to the ip6 element formatter.
                        typeName = "ip6[]";
                        int cnt = ReadLen(type, m2, ref pos);
                        var addrs = new List<string>();
                        for (int i = 0; i < cnt && pos + 16 <= m2.Length; i++, pos += 16)
                            addrs.Add(BitConverter.ToString(m2, pos, 16).Replace("-", ""));
                        val = "[" + string.Join(",", addrs) + "]";
                        break;
                    }
                    case 0xB0: case 0xB1: case 0xB2:
                    {
                        // raw[] (ftype 22): count + (len + bytes) per element, each element rendered as the
                        // same hex text a scalar raw is. Without this case the whole field was skipped, so
                        // /ip/dhcp-server/alert's valid-servers and RoMON's path read as nothing at all.
                        typeName = "raw[]";
                        int cnt = ReadLen(type, m2, ref pos);
                        var blobs = new List<string>();
                        for (int i = 0; i < cnt && pos < m2.Length; i++)
                        {
                            int elen = ReadLen(type, m2, ref pos);
                            if (pos + elen > m2.Length) break;
                            blobs.Add(BitConverter.ToString(m2, pos, elen).Replace("-", ""));
                            pos += elen;
                        }
                        val = "[" + string.Join(",", blobs) + "]";
                        break;
                    }
                    case 0x28: case 0x29: case 0x2A:
                    {
                        // message (ftype 5): length via size flags, body is a submessage.
                        typeName = "msg";
                        int len = ReadLen(type, m2, ref pos);
                        if (pos + len <= m2.Length)
                        {
                            val = ParseAllFields(m2.Skip(pos).Take(len).ToArray());
                            pos += len;
                        }
                        break;
                    }
                    case 0xA8: case 0xA9: case 0xAA:
                    {
                        // message-array (ftype 21): count + (len + submessage) per element.
                        typeName = "msg[]";
                        int cnt = ReadLen(type, m2, ref pos);
                        var list = new List<Dictionary<int, Tuple<string, object>>>();
                        for (int i = 0; i < cnt && pos < m2.Length; i++)
                        {
                            int elen = ReadLen(type, m2, ref pos);
                            if (pos + elen > m2.Length) break;
                            list.Add(ParseAllFields(m2.Skip(pos).Take(elen).ToArray()));
                            pos += elen;
                        }
                        val = list;
                        break;
                    }
                    default:
                        pos += SkipTypeBytes(type, m2, pos);
                        continue;
                }
                if (!result.ContainsKey(fullKey))
                {
                    result[fullKey] = Tuple.Create(typeName, val);
                }
                else if (WinboxM2Protocol.TypedKey.IsArrayType(result[fullKey].Item1)
                         != WinboxM2Protocol.TypedKey.IsArrayType(typeName))
                {
                    // ONE record, ONE key, TWO fields. The .jg declares both — /ip/dhcp-client's window has
                    // 'Add Default Route' as u12 (a scalar enum) and 'DHCP Options' as U12 (a u32[]) — and the
                    // router sends both, so first-wins here silently dropped whichever came second: the
                    // add-default-route the API prints was never in the record the decoder saw. The array and
                    // the scalar are told apart by nothing but the TLV type, so the loser is filed under its
                    // arrayness-qualified key and the resolver looks it up there (WinboxFieldResolver's typed
                    // registrations). A duplicate of the SAME arrayness is still first-wins — that is the .jg's
                    // own 'freq'/'CPU Frequency' kind of alias, two names for one value.
                    result[WinboxM2Protocol.TypedKey.Qualify(fullKey, WinboxM2Protocol.TypedKey.IsArrayType(typeName))]
                        = Tuple.Create(typeName, val);
                }
            }
            return result;
        }

        internal static int ParseSessionId(byte[] m2)
        {
            if (m2 == null || m2.Length < 4 || m2[0] != 'M' || m2[1] != '2')
                throw new InvalidOperationException("Not a valid M2 response");
            if (TryParseSessionId(m2, out int sessionId))
                return sessionId;
            throw new InvalidOperationException("No SESSION_ID in M2 response");
        }

        /// <summary>
        /// Non-throwing <see cref="ParseSessionId"/>: <c>true</c> plus the id when the message carries a
        /// SESSION_ID field, <c>false</c> when it does not (or is not a valid M2 message). Used to route an
        /// unsolicited frame to the session it belongs to — a mepty terminal that has been replaced still
        /// emits its trailing output, and attributing it to the current session desyncs the reader.
        /// </summary>
        internal static bool TryParseSessionId(byte[] m2, out int sessionId)
        {
            sessionId = -1;
            if (m2 == null || m2.Length < 4 || m2[0] != 'M' || m2[1] != '2')
                return false;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                int fullKey = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                if (fullKey == 0xFE0001)
                {
                    // mepty returns the session id as u8 (0x09) for small ids and u32 (0x08) for ids > 255.
                    if (type == 0x09 && pos < m2.Length)         { sessionId = m2[pos]; return true; }
                    if (type == 0x08 && pos + 4 <= m2.Length)    { sessionId = (int)BitConverter.ToUInt32(m2, pos); return true; }
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            return false;
        }

        /// <summary>
        /// Returns the complete TLV bytes (4-byte header + payload) of the first field carrying
        /// <paramref name="fullKey"/>, or <c>null</c> when the message has no such field.
        /// </summary>
        /// <remarks>
        /// The slice is byte-identical to what the router sent, so it can be echoed back into a request
        /// without the client knowing how to decode it. That is what the getall continuation tokens need:
        /// <see cref="WinboxM2Protocol.RecordKey.ContinuationRaw"/> is an opaque cursor of an unknown shape,
        /// and decoding it only to re-encode it would be a guess at that shape — copying the bytes is not.
        /// </remarks>
        internal static byte[]? ExtractRawField(byte[] m2, int fullKey)
        {
            if (m2 == null || m2.Length < 2 || m2[0] != 'M' || m2[1] != '2') return null;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int key = (m2[pos + 2] << 16) | (m2[pos + 1] << 8) | m2[pos];
                int type = m2[pos + 3];
                int start = pos;
                pos += 4;
                pos += SkipTypeBytes(type, m2, pos);
                if (key != fullKey) continue;
                // A field whose declared length runs past the end of the frame is truncated: echoing a
                // malformed cursor would make the next request the router's problem to reject. Drop it and
                // let the caller treat the page as the last one.
                if (pos > m2.Length) return null;
                var slice = new byte[pos - start];
                Buffer.BlockCopy(m2, start, slice, 0, slice.Length);
                return slice;
            }
            return null;
        }

        // Parses the bytes of a user-namespace field; handles raw_s/raw_l and string_s/string_l.
        internal static byte[]? ParseUserBytes(byte[] m2, int keyId)
        {
            if (m2 == null || m2.Length < 4) return null;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                int userKey = kl | (kh << 8);
                pos += 4;
                if (ns == 0x00 && userKey == keyId)
                {
                    if (type == 0x31 || type == 0x21)
                    {
                        int len = m2[pos++];
                        return m2.Skip(pos).Take(len).ToArray();
                    }
                    if (type == 0x30 || type == 0x20)
                    {
                        if (pos + 2 > m2.Length) return null;
                        int len = BitConverter.ToUInt16(m2, pos); pos += 2;
                        return m2.Skip(pos).Take(len).ToArray();
                    }
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            return null;
        }

        internal static byte[]? ParseRawUser(byte[] m2, int keyId)
        {
            if (m2 == null || m2.Length < 2) return null;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                pos += 4;
                int userKey = kl | (kh << 8);
                if (ns == 0x00 && userKey == keyId && (type == 0x31 || type == 0x30))
                {
                    int len = (type == 0x31) ? m2[pos++] : (int)BitConverter.ToUInt16(m2, (pos += 2) - 2);
                    return m2.Skip(pos).Take(len).ToArray();
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            return null;
        }

        internal static int ParseSysStatus(byte[] m2)
        {
            if (m2 == null || m2.Length < 2) return 0;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                int fullKey = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                if (fullKey == 0xFF0008)
                {
                    if (type == 0x09 && pos < m2.Length) return m2[pos];
                    if (type == 0x08 && pos + 4 <= m2.Length) return (int)BitConverter.ToUInt32(m2, pos);
                    return 0;
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            return 0;
        }

        /// <summary>
        /// Reads the request id (<c>0xFF0006</c>) from an M2 message, or <c>null</c> when the field is absent.
        /// </summary>
        /// <remarks>
        /// RouterOS echoes the request id back on the response, which makes it the correlation key for
        /// dispatching replies to concurrent in-flight requests (verified live — see
        /// <c>Docs/findings-winbox.md §12.1</c>). Note that <c>0xFF0003</c> is <b>not</b> a
        /// correlation field despite looking like one in a single-exchange trace: it stays constant for the
        /// whole session while this value tracks the request (§12.2).
        /// </remarks>
        internal static int? ParseSysReqId(byte[] m2)
        {
            if (m2 == null || m2.Length < 2) return null;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                int fullKey = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                if (fullKey == WinboxM2Protocol.SysKey.RequestId)
                {
                    // u8 in practice (NextReqIdField emits U8Sys); u32 accepted for symmetry with SESSION_ID.
                    if (type == 0x09 && pos < m2.Length) return m2[pos];
                    if (type == 0x08 && pos + 4 <= m2.Length) return (int)BitConverter.ToUInt32(m2, pos);
                    return null;
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            return null;
        }

        // Returns number of bytes to skip for a given TLV type byte (not counting the type byte itself).
        // The 0xA0 str_array case MUST be kept — RouterOS 7.21.4 sends it in mepty responses
        // (e.g. "msg-proxy-7.21.4"); without it the parser walks into the payload and misaligns.
        //
        // The type byte is (ftype << 3) | size-flags (short=1, long=2), and the ftype table is in master*.js:
        // 0 bool, 1 u32, 2 u64, 3 addr6, 4 string, 5 message, 6 raw, and the same +16 for the array forms.
        // EVERY ftype must have a case here even when ParseAllFields has no decoder for it: an unhandled type
        // falls to `default: return 0`, after which the parser reads the value's own bytes as the next key and
        // type, and the rest of the message decodes into garbage keys — silently. That is how the 0xA0
        // str_array bug behaved before it was found, and 0x18 (addr6) was the same trap waiting on any handler
        // with an IPv6 field.
        internal static int SkipTypeBytes(int type, byte[] data, int pos)
        {
            switch (type)
            {
                case 0x00: case 0x01: return 0;
                case 0x09: return 1;
                case 0x08: return 4;
                case 0x10: return 8;
                case 0x18: return 16;                                                   // addr6 (fixed width)
                case 0x80: case 0x81: case 0x82: return CountedArrayBytes(type, data, pos, 1);   // bool[]
                case 0x90: case 0x91: case 0x92: return CountedArrayBytes(type, data, pos, 8);   // u64[]
                case 0x98: case 0x99: case 0x9A: return CountedArrayBytes(type, data, pos, 16);  // addr6[]
                case 0x21: return pos < data.Length ? 1 + data[pos] : 1;
                case 0x20: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) : 2;
                case 0x31: return pos < data.Length ? 1 + data[pos] : 1;
                case 0x30: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) : 2;
                case 0x29: return pos < data.Length ? 1 + data[pos] : 1;             // message short
                case 0x28: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) : 2; // message normal
                case 0x2A: return pos + 3 < data.Length ? 4 + (int)BitConverter.ToUInt32(data, pos) : 4; // message long
                case 0x88: case 0x89: case 0x8A: return CountedArrayBytes(type, data, pos, 4); // u32[]
                // str_array / msg_array / raw_array: count + (len + data) per entry — skip sum of all entry
                // sizes. Widths follow the type's size flags exactly as ReadLen reads them: short (…1) = 1B,
                // normal = 2B, long (…2) = 4B, for both the count and each element length.
                case 0xA0: case 0xA1: case 0xA8: case 0xA9: case 0xA2: case 0xAA:
                case 0xB0: case 0xB1: case 0xB2:
                {
                    int w = LenWidth(type);
                    int p = pos;
                    int cnt = ReadCounter(data, ref p, w);
                    for (int i = 0; i < cnt && p < data.Length; i++)
                    {
                        // Read the element length first: `p += ReadCounter(…, ref p, …)` would capture p
                        // before the call advanced it, silently dropping the length field's own bytes.
                        int elen = ReadCounter(data, ref p, w);
                        p += elen;
                    }
                    return p - pos;
                }
                default: return 0;  // unknown type — stop parsing
            }
        }

        // Byte length of a fixed-element-width array field: a count followed by `count` elements of
        // `elemSize` bytes each — the shape master*.js writes for bool[]/u32[]/u64[]/addr6[].
        private static int CountedArrayBytes(int type, byte[] data, int pos, int elemSize)
        {
            int w = LenWidth(type);
            int p = pos;
            int cnt = ReadCounter(data, ref p, w);
            return (p - pos) + cnt * elemSize;
        }

        // Width in bytes of a length/count field, from the type's size flags — the same rule ReadLen applies
        // when decoding. Kept separate so the skip path and the decode path cannot drift: a width the skipper
        // gets wrong does not fail, it silently walks into the payload and turns the rest of the message into
        // garbage keys (see the SkipTypeBytes comment).
        private static int LenWidth(int type) => IsShort(type) ? 1 : IsLong(type) ? 4 : 2;

        // Reads a little-endian counter of `width` bytes at `pos` and advances past it. A truncated counter
        // yields 0, so a short read ends the walk instead of running off the buffer.
        private static int ReadCounter(byte[] data, ref int pos, int width)
        {
            int v = 0;
            for (int i = 0; i < width; i++)
            {
                if (pos >= data.Length) { pos = data.Length; return 0; }
                v |= data[pos++] << (8 * i);
            }
            return v;
        }
    }
}
