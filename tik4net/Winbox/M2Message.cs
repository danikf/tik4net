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
    /// (1-based key), 0xFF = system fields, 0xFE = session fields. Shared by every WinBox transport, over
    /// TCP (<see cref="WinboxM2Session"/>) and the MAC layer (<see cref="WinboxMacM2Session"/>) alike.
    /// </remarks>
    internal static class M2Message
    {
        /// <summary>
        /// The M2 type bytes: <c>(ftype &lt;&lt; 3) | size flags</c>, with the ftype table from webfig's
        /// <c>master*.js</c> (0 bool, 1 u32, 2 u64, 3 addr6, 4 string, 5 message, 6 raw, and +16 for the array forms)
        /// and the size flags <see cref="Short"/> (1-byte length or count) and <see cref="Long"/> (4-byte).
        /// </summary>
        internal static class TypeByte
        {
            internal const int Short = 0x01;
            internal const int Long = 0x02;

            internal const int False = 0x00;          // bool: the value is the short flag itself
            internal const int True = 0x01;
            internal const int U32 = 0x08;
            internal const int U8 = 0x09;             // u32, short form: one byte
            internal const int U64 = 0x10;
            internal const int Addr6 = 0x18;          // sixteen bytes, no length
            internal const int String = 0x20;
            internal const int Message = 0x28;
            internal const int Raw = 0x30;
            internal const int BoolArray = 0x80;
            internal const int U32Array = 0x88;
            internal const int U64Array = 0x90;
            internal const int Addr6Array = 0x98;
            internal const int StringArray = 0xA0;
            internal const int MessageArray = 0xA8;
            internal const int RawArray = 0xB0;
        }

        // ── TLV building ──────────────────────────────────────────────────────

        // M2 message = b'M2' + concatenated TLV fields
        internal static byte[] BuildM2(params byte[][] fields)
            => new byte[] { (byte)'M', (byte)'2' }.Concat(fields.SelectMany(f => f)).ToArray();

        // The 4-byte field header every builder starts with: the full key over three little-endian bytes (key low,
        // key high, namespace), then the type byte.
        private static List<byte> Header(int fullKey, int type, int capacity = 8)
            => new List<byte>(capacity) { (byte)fullKey, (byte)(fullKey >> 8), (byte)(fullKey >> 16), (byte)type };

        // String and raw share one length rule: the short form (type | Short, a 1-byte length) up to 255 bytes, the
        // normal form (a 2-byte length) above.
        private static byte[] VarLen(int fullKey, int normalType, byte[] data)
        {
            List<byte> b;
            if (data.Length <= 255)
            {
                b = Header(fullKey, normalType | TypeByte.Short, 5 + data.Length);
                b.Add((byte)data.Length);
            }
            else
            {
                b = Header(fullKey, normalType, 6 + data.Length);
                b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            }
            b.AddRange(data);
            return b.ToArray();
        }

        // The counted arrays (u32[], u64[], addr6[], string[], raw[], message[]): a 2-byte count, then each item as
        // writeItem writes it.
        private static byte[] Counted<T>(int fullKey, int type, IList<T> items, Action<List<byte>, T> writeItem)
        {
            var b = Header(fullKey, type);
            b.AddRange(BitConverter.GetBytes((ushort)items.Count));
            foreach (T item in items) writeItem(b, item);
            return b.ToArray();
        }

        // A 2-byte length followed by the bytes: one element of string[], raw[] and message[].
        private static void LengthPrefixed(List<byte> b, byte[] data)
        {
            b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            b.AddRange(data);
        }

        /// <summary>
        /// A request: the header every request with a reply sends — <c>SYS_TO</c> = <paramref name="handler"/>,
        /// <c>SYS_FROM</c>, reply-expected, the request id, <c>SYS_CMD</c> = <paramref name="command"/> as a u32 —
        /// then the operation's own <paramref name="fields"/> in order. A <c>null</c> field is left out, so an
        /// optional one is written in place (<c>id &gt;= 0 ? SessionIdField(id) : null</c>).
        /// </summary>
        /// <remarks>
        /// The header order is what the native operations, RoMON and the mepty login send. The legacy MD5 login sends
        /// <c>SYS_CMD</c> before reply-expected, and mproxy sends <c>SYS_CMD</c> as a u8, so neither is built here.
        /// </remarks>
        internal static byte[] Request(WinboxHandler handler, int command, byte[] reqIdField, IEnumerable<byte[]?> fields)
        {
            var all = new List<byte[]>
            {
                SysToArr(handler), SysFrom(), BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), reqIdField,
                U32Sys(WinboxM2Protocol.SysKey.Command, command),
            };
            foreach (var f in fields) if (f != null) all.Add(f);
            return BuildM2(all.ToArray());
        }

        /// <inheritdoc cref="Request(WinboxHandler, int, byte[], IEnumerable{byte[]})"/>
        internal static byte[] Request(WinboxHandler handler, int command, byte[] reqIdField, params byte[]?[] fields)
            => Request(handler, command, reqIdField, (IEnumerable<byte[]?>)fields);

        // SYS_TO u32_array: key=(0x01,0x00,0xFF)
        internal static byte[] SysToArr(params int[] ids) => U32ArraySys(WinboxM2Protocol.SysKey.To, ids);

        /// <summary><c>SYS_TO</c> = <paramref name="handler"/>'s path.</summary>
        internal static byte[] SysToArr(WinboxHandler handler) => SysToArr(handler.ToArray());

        // SYS_FROM u32_array [0, srcId]: key=(0x02,0x00,0xFF)
        internal static byte[] SysFrom(int srcId = 8) => U32ArraySys(WinboxM2Protocol.SysKey.From, 0, srcId);

        // Bool field for system keys
        internal static byte[] BoolSys(int fullKey, bool val)
            => Header(fullKey, val ? TypeByte.True : TypeByte.False).ToArray();

        // u8 field for system keys
        internal static byte[] U8Sys(int fullKey, byte val)
        {
            var b = Header(fullKey, TypeByte.U8);
            b.Add(val);
            return b.ToArray();
        }

        // u32 field for system keys (used when value > 255)
        internal static byte[] U32Sys(int fullKey, int val)
        {
            var b = Header(fullKey, TypeByte.U32);
            b.AddRange(BitConverter.GetBytes((uint)val));
            return b.ToArray();
        }

        // u64 field (ftype 2 → type byte 0x10, eight little-endian bytes). NOT interchangeable with the u32
        // form: RouterOS reads the type byte, and a u64 field written as u32 is accepted and IGNORED — the
        // write reports success and the value on the router does not move. That is how /queue/simple's rate
        // fields were unwritable over native while resolving to the right keys.
        internal static byte[] U64Sys(int fullKey, ulong val)
        {
            var b = Header(fullKey, TypeByte.U64, 12);
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

            List<byte> b;
            if (body.Count <= 0xFF) { b = Header(fullKey, TypeByte.Message | TypeByte.Short); b.Add((byte)body.Count); }
            else if (body.Count <= 0xFFFF) { b = Header(fullKey, TypeByte.Message); b.AddRange(BitConverter.GetBytes((ushort)body.Count)); }
            else { b = Header(fullKey, TypeByte.Message | TypeByte.Long); b.AddRange(BitConverter.GetBytes((uint)body.Count)); }
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
            => id <= 255
                ? U8Sys(WinboxM2Protocol.RecordKey.Id, (byte)id)
                : U32Sys(WinboxM2Protocol.RecordKey.Id, unchecked((int)id));

        // The user-namespace builders: the *Sys builders with namespace 0. The key is masked to its 16 bits, so a
        // key with namespace bits still lands in namespace 0.
        internal static byte[] StringUser(int keyId, string value) => StringSys(keyId & 0xFFFF, value);
        internal static byte[] U32User(int keyId, int val) => U32Sys(keyId & 0xFFFF, val);
        internal static byte[] RawUser(int keyId, byte[] data) => RawSys(keyId & 0xFFFF, data);
        internal static byte[] U32ArrayUser(int keyId, params int[] values) => U32ArraySys(keyId & 0xFFFF, values);

        // String field for a full key (any namespace, e.g. 0xFE0009 = comment).
        internal static byte[] StringSys(int fullKey, string value)
            => VarLen(fullKey, TypeByte.String, Encoding.UTF8.GetBytes(value ?? ""));

        // Raw bytes field for a full key (any namespace, e.g. mac/ip encoded as raw bytes).
        internal static byte[] RawSys(int fullKey, byte[] data)
            => VarLen(fullKey, TypeByte.Raw, data ?? new byte[0]);

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
            var b = Header(fullKey, TypeByte.Addr6, 20);
            b.AddRange(addr16);
            return b.ToArray();
        }

        // u32 array field for a system key (namespace 0xFF or 0xFE).
        internal static byte[] U32ArraySys(int fullKey, params int[] values)
            => Counted(fullKey, TypeByte.U32Array, values, (b, v) => b.AddRange(BitConverter.GetBytes((uint)v)));

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
            => Counted(fullKey, TypeByte.U64Array, values, (b, v) => b.AddRange(BitConverter.GetBytes(v)));

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
            => Counted(fullKey, TypeByte.StringArray, values, (b, v) => LengthPrefixed(b, Encoding.UTF8.GetBytes(v ?? "")));

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
            => Counted(fullKey, TypeByte.RawArray, values, (b, v) => LengthPrefixed(b, v ?? new byte[0]));

        /// <summary>
        /// IPv6-array field (webfig ftype 19, <c>FT_ADDR6_ARRAY</c> — type byte <c>0x98</c>): a 16-bit
        /// element count followed by sixteen raw bytes per element, with NO per-element length — the same
        /// fixed-width rule the scalar <see cref="Addr6Sys"/> follows.
        /// </summary>
        internal static byte[] Addr6ArraySys(int fullKey, IList<byte[]> values)
            => Counted(fullKey, TypeByte.Addr6Array, values, (b, v) =>
            {
                if (v == null || v.Length != 16)
                    throw new ArgumentException("An FT_ADDR6 element is exactly 16 bytes.", nameof(values));
                b.AddRange(v);
            });

        /// <summary>
        /// Message-array field (webfig ftype 21, <c>FT_MESSAGE_ARRAY</c> — type byte <c>0xA8</c>): a 16-bit
        /// element count, then each element as a 16-bit byte length followed by a complete submessage
        /// (<c>'M2'</c> + its own TLVs) — the write side of <see cref="ParseRecords"/>.
        /// </summary>
        internal static byte[] MessageArraySys(int fullKey, IList<byte[][]> elements)
            => Counted(fullKey, TypeByte.MessageArray, elements, (b, fields) => LengthPrefixed(b, BuildM2(fields)));

        // ── Diagnostics ───────────────────────────────────────────────────────

        /// <summary>
        /// Renders an M2 message (request or reply) as a single compact, human-readable line for
        /// row-level tracing (<c>OnReadRow</c>/<c>OnWriteRow</c>). Shows the message length and every
        /// top-level field as <c>0x&lt;fullKey&gt;=&lt;type&gt;:&lt;value&gt;</c>; nested submessages and
        /// record arrays are expanded so the raw M2 field keys of each record are visible — which is the
        /// information needed to debug the native field mapping. Non-M2 / empty buffers are reported as
        /// a short hex preview.
        /// </summary>
        internal static string Describe(byte[]? m2)
        {
            if (m2 == null) return "(null)";
            if (m2.Length < 2 || m2[0] != 'M' || m2[1] != '2')
            {
                int n = Math.Min(m2.Length, 16);
                string hex = TikHexText.Format(m2, 0, n);
                return $"({m2.Length}B non-M2{(n < m2.Length ? "…" : "")}: {hex})";
            }

            var sb = new StringBuilder();
            sb.Append("M2[").Append(m2.Length).Append("B]");
            foreach (var kv in ParseAllFields(m2))
                sb.Append(' ').Append(RenderTraceKey(kv.Key)).Append('=')
                  .Append(kv.Value.TraceName).Append(':').Append(RenderTraceValue(kv.Value.Value));
            return sb.ToString();
        }

        // A key the parser had to qualify (two fields, one key — see ParseAllFields) would otherwise print as
        // 0x2000012, which is not a key the router ever sent. Render the wire key with the qualifier as a
        // suffix, so a trace can be matched against the .jg id ('u12'/'U12') by eye.
        private static string RenderTraceKey(int key)
            => WinboxM2Protocol.TypedKey.IsQualified(key)
                ? "0x" + WinboxM2Protocol.TypedKey.WireKeyOf(key).ToString("X")
                    + ((key & WinboxM2Protocol.TypedKey.Array) != 0 ? "~arr"
                       : (key & WinboxM2Protocol.TypedKey.Bool) != 0 ? "~bool" : "~sca")
                : "0x" + key.ToString("X");

        // Compact renderer for a decoded M2 value: nested record dicts → {0xKEY=…,…}, record arrays →
        // [{…},{…}], scalars → ToString(). Depth-capped to keep trace lines bounded.
        private static string RenderTraceValue(object? value, int depth = 0)
        {
            switch (value)
            {
                case null:
                    return "";
                case M2Record rec:
                {
                    if (depth >= 3) return "{…}";
                    var sb = new StringBuilder("{");
                    bool first = true;
                    foreach (var kv in rec)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(RenderTraceKey(kv.Key)).Append('=')
                          .Append(RenderTraceValue(kv.Value.Value, depth + 1));
                    }
                    return sb.Append('}').ToString();
                }
                case List<M2Record> list:
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
                    return TikHexText.Format(bytes);
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
        private static bool IsShort(int type) => (type & TypeByte.Short) != 0;
        private static bool IsLong(int type) => (type & TypeByte.Long) != 0;

        // Read a length/count using the type's size flags; advances pos.
        private static int ReadLen(int type, byte[] d, ref int pos)
        {
            int len = pos < d.Length ? d[pos++] : 0;
            if (!IsShort(type) && pos < d.Length) len |= d[pos++] << 8;
            if (IsLong(type)) { if (pos < d.Length) len |= d[pos++] << 16; if (pos < d.Length) len |= d[pos++] << 24; }
            return len;
        }

        private static string Hex(byte[] d, int pos, int len)
            => len <= 0 ? "" : TikHexText.Format(d, pos, len);

        private static bool IsM2(byte[]? m2) => m2 != null && m2.Length >= 2 && m2[0] == 'M' && m2[1] == '2';

        /// <summary>
        /// One top-level TLV field as <see cref="Walk"/> found it: the full key and type byte from its header, where
        /// the field starts (its header) and where it ends.
        /// </summary>
        /// <remarks>
        /// <see cref="End"/> is where the field's declared length says it ends, so it lies past the end of the
        /// message when the field is truncated — always the last field walked.
        /// </remarks>
        internal readonly struct M2Field
        {
            internal M2Field(int key, int type, int start, int end)
            {
                Key = key;
                Type = type;
                Start = start;
                End = end;
            }

            internal int Key { get; }
            internal int Type { get; }
            internal int Start { get; }
            internal int ValueStart => Start + 4;
            internal int End { get; }
        }

        /// <summary>
        /// The one TLV walk every parser here uses: the top-level fields of <paramref name="m2"/> in wire order, from
        /// offset 2 (after the <c>'M2'</c> magic, which it does not check).
        /// </summary>
        /// <remarks>
        /// Each field is skipped by <see cref="SkipTypeBytes"/>'s rule, so a nested message holding the same key is
        /// never seen. The walk ends at the end of the message, after a truncated field, and at a type byte no
        /// ftype covers: such a field's length is unknown, so the bytes after its header could be its value or the
        /// next field, and reading them as a field would invent a key. Nothing after it is reported.
        /// </remarks>
        internal static IEnumerable<M2Field> Walk(byte[]? m2)
        {
            if (m2 == null) yield break;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int key = (m2[pos + 2] << 16) | (m2[pos + 1] << 8) | m2[pos];
                int type = m2[pos + 3];
                int? size = ValueBytes(type, m2, pos + 4);
                if (size == null) yield break;
                int end = pos + 4 + size.Value;
                yield return new M2Field(key, type, pos, end);
                pos = end;
            }
        }

        /// <summary>The first top-level field with <paramref name="fullKey"/>, by <see cref="Walk"/>.</summary>
        private static bool TryFindField(byte[]? m2, int fullKey, out M2Field field)
        {
            foreach (var f in Walk(m2))
            {
                if (f.Key != fullKey) continue;
                field = f;
                return true;
            }
            field = default;
            return false;
        }

        // A u8 or u32 value (RouterOS sends small numbers in the short form), or null for any other type or a value
        // cut off by the end of the message.
        private static int? ReadU8OrU32(byte[] m2, M2Field f)
        {
            if (f.Type == TypeByte.U8 && f.ValueStart < m2.Length) return m2[f.ValueStart];
            if (f.Type == TypeByte.U32 && f.ValueStart + 4 <= m2.Length) return (int)BitConverter.ToUInt32(m2, f.ValueStart);
            return null;
        }

        // The first top-level field with fullKey read as a u8 or u32; null when it is absent or of another type.
        private static int? ReadU8OrU32(byte[]? m2, int fullKey)
            => TryFindField(m2, fullKey, out var f) ? ReadU8OrU32(m2!, f) : null;

        // Parse a message-array field (ftype 21) into a list of record field-dicts.
        // Each element on the wire is a full submessage ('M2' + TLVs). Returns the records
        // found under <paramref name="fullKey"/>, or an empty list if not present / wrong type.
        // This is the critical "records under 0xFE0002 (Mfe0002)" path used by getall/get-one.
        internal static List<M2Record> ParseRecords(byte[] m2, int fullKey)
        {
            var records = new List<M2Record>();
            if (!IsM2(m2)) return records;
            foreach (var f in Walk(m2))
            {
                if (f.Key != fullKey || M2Types.FromTypeByte(f.Type) != M2Type.MessageArray) continue;
                int pos = f.ValueStart;
                int cnt = ReadLen(f.Type, m2, ref pos);
                for (int i = 0; i < cnt && pos < m2.Length; i++)
                {
                    int elen = ReadLen(f.Type, m2, ref pos);
                    if (pos + elen > m2.Length) break;
                    records.Add(ParseAllFields(m2.Skip(pos).Take(elen).ToArray()));
                    pos += elen;
                }
                return records;
            }
            return records;
        }

        // Parse all TLV fields from an M2 message into a record keyed by full_key; the values by type are listed on
        // M2Value.
        internal static M2Record ParseAllFields(byte[] m2)
        {
            var result = new M2Record();
            if (!IsM2(m2)) return result;
            foreach (var f in Walk(m2))
            {
                int fullKey = f.Key, type = f.Type, pos = f.ValueStart;
                if (!(M2Types.FromTypeByte(type) is M2Type ftype)) continue;   // unreachable: Walk stops at a type byte no ftype covers
                // Left null when a field's declared length runs past the end of a truncated frame.
                object? val = null;
                // Decoded by ftype, with every length and count read under the type's size flags (ReadLen) - the
                // rule Walk (ValueBytes) steps over the field by, so a type the walker knows is a type this reads. A
                // switch on exact type bytes skipped the size variants it did not list (u32[] short/long, string[]
                // short/long, string and raw long, every bool[]) and read a normal-length raw as the text "[nB]".
                switch (ftype)
                {
                    case M2Type.Bool:   // the value is the short flag itself
                        val = type == TypeByte.True; break;
                    case M2Type.U32:   // or u8 in the short form
                        if (type == TypeByte.U8) { if (pos < m2.Length) val = m2[pos]; }
                        else if (pos + 4 <= m2.Length) val = BitConverter.ToUInt32(m2, pos);
                        break;
                    case M2Type.U64:
                        if (pos + 8 <= m2.Length) val = BitConverter.ToUInt64(m2, pos);
                        break;
                    case M2Type.Addr6:
                        // FT_ADDR6: 16 raw bytes, no length prefix (see Addr6Sys).
                        if (pos + 16 <= m2.Length) val = m2.Skip(pos).Take(16).ToArray();
                        break;
                    case M2Type.String:
                    {
                        int len = ReadLen(type, m2, ref pos);
                        if (pos < m2.Length || len == 0) val = Encoding.UTF8.GetString(m2, pos, Math.Max(0, Math.Min(len, m2.Length - pos)));
                        break;
                    }
                    case M2Type.Message:
                    {
                        // length via size flags, body is a submessage.
                        int len = ReadLen(type, m2, ref pos);
                        if (pos + len <= m2.Length)
                            val = ParseAllFields(m2.Skip(pos).Take(len).ToArray());
                        break;
                    }
                    case M2Type.Raw:
                    {
                        // hex text, whatever its length class - a raw past 255 bytes (an SSH public key, a
                        // certificate blob) is a value like any other, not a size.
                        int len = ReadLen(type, m2, ref pos);
                        if (pos < m2.Length || len == 0) val = Hex(m2, pos, Math.Min(len, m2.Length - pos));
                        break;
                    }
                    case M2Type.BoolArray:
                    {
                        // count + one byte per element, rendered as the scalar bool renders.
                        int cnt = ReadLen(type, m2, ref pos);
                        var items = new List<string>();
                        for (int i = 0; i < cnt && pos < m2.Length; i++, pos++)
                            items.Add(m2[pos] != 0 ? "true" : "false");
                        val = "[" + string.Join(",", items) + "]";
                        break;
                    }
                    case M2Type.U32Array:
                    {
                        int cnt = ReadLen(type, m2, ref pos);
                        var nums = new List<uint>();
                        for (int i = 0; i < cnt && pos + 4 <= m2.Length; i++, pos += 4)
                            nums.Add(BitConverter.ToUInt32(m2, pos));
                        val = "[" + string.Join(",", nums) + "]";
                        break;
                    }
                    case M2Type.U64Array:
                    {
                        // the counted-array shape u32[] has, one width wider; rendered as the same "[a,b,...]"
                        // text, so the codec's list formatter reads it without a case of its own.
                        int cnt = ReadLen(type, m2, ref pos);
                        var nums = new List<ulong>();
                        for (int i = 0; i < cnt && pos + 8 <= m2.Length; i++, pos += 8)
                            nums.Add(BitConverter.ToUInt64(m2, pos));
                        val = "[" + string.Join(",", nums) + "]";
                        break;
                    }
                    case M2Type.Addr6Array:
                    {
                        // count + 16 fixed bytes per element; each element hex, so the codec's list
                        // formatter can hand it to the ip6 element formatter.
                        int cnt = ReadLen(type, m2, ref pos);
                        var addrs = new List<string>();
                        for (int i = 0; i < cnt && pos + 16 <= m2.Length; i++, pos += 16)
                            addrs.Add(Hex(m2, pos, 16));
                        val = "[" + string.Join(",", addrs) + "]";
                        break;
                    }
                    case M2Type.StringArray:
                    {
                        // count + (len + bytes) per element.
                        int cnt = ReadLen(type, m2, ref pos);
                        var strs = new List<string>();
                        for (int i = 0; i < cnt && pos < m2.Length; i++)
                        {
                            int elen = ReadLen(type, m2, ref pos);
                            if (pos + elen > m2.Length) break;
                            strs.Add(Encoding.UTF8.GetString(m2, pos, elen));
                            pos += elen;
                        }
                        val = "[" + string.Join(",", strs) + "]";
                        break;
                    }
                    case M2Type.MessageArray:
                    {
                        // count + (len + submessage) per element.
                        int cnt = ReadLen(type, m2, ref pos);
                        var list = new List<M2Record>();
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
                    case M2Type.RawArray:
                    {
                        // count + (len + bytes) per element, each the hex text a scalar raw is.
                        int cnt = ReadLen(type, m2, ref pos);
                        var blobs = new List<string>();
                        for (int i = 0; i < cnt && pos < m2.Length; i++)
                        {
                            int elen = ReadLen(type, m2, ref pos);
                            if (pos + elen > m2.Length) break;
                            blobs.Add(Hex(m2, pos, elen));
                            pos += elen;
                        }
                        val = "[" + string.Join(",", blobs) + "]";
                        break;
                    }
                    default:
                        continue;   // unreachable: Walk stops at a type byte no ftype covers
                }
                var value = new M2Value(ftype, val, IsShort(type));
                if (!result.ContainsKey(fullKey))
                {
                    result[fullKey] = value;
                }
                else if (WinboxM2Protocol.TypedKey.KindOf(result[fullKey].Type) != WinboxM2Protocol.TypedKey.KindOf(ftype))
                {
                    // ONE record, ONE key, TWO fields. The .jg declares both — /ip/dhcp-client's window has
                    // 'Add Default Route' as u12 (a scalar enum) and 'DHCP Options' as U12 (a u32[]) — and the
                    // router sends both, so first-wins here silently dropped whichever came second: the
                    // add-default-route the API prints was never in the record the decoder saw. The array and
                    // the scalar are told apart by nothing but the TLV type, so the loser is filed under its
                    // arrayness-qualified key and the resolver looks it up there (WinboxFieldResolver's typed
                    // registrations). A bool beside a number is the same case: the IPv4 Connections window has the
                    // 'Hw. Offload' flag on b1f and the original packet count on q1f, and one record carries both
                    // (7.24.2). A duplicate of the SAME kind is still first-wins — that is the .jg's
                    // own 'freq'/'CPU Frequency' kind of alias, two names for one value.
                    result[WinboxM2Protocol.TypedKey.Qualify(fullKey, ftype)] = value;
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
            throw new InvalidOperationException("No SESSION_ID in M2 response — " + DescribeSysError(m2));
        }

        /// <summary>
        /// Renders what the router said about a reply the caller cannot use: the system error code
        /// (<c>0xFF0008</c>) and its optional string (<c>0xFF0009</c>), falling back to the whole message.
        /// </summary>
        /// <remarks>
        /// The refusal is evidence available exactly once — the bytes are gone the moment the exception is
        /// built. Reporting only the absence of the field we wanted turns a router-side refusal (a session
        /// cap, "not enough resources") into something indistinguishable from a client-side bug.
        /// </remarks>
        internal static string DescribeSysError(byte[] m2)
        {
            try
            {
                var fields = ParseAllFields(m2);
                string? err = fields.TryGetValue(WinboxM2Protocol.SysKey.ErrorString, out var es)
                    ? es.Value?.ToString() : null;
                int code = ParseSysStatus(m2);
                if (code != 0 || !string.IsNullOrEmpty(err))
                    return "router said error 0x" + code.ToString("X") +
                           (string.IsNullOrEmpty(err) ? " (no message)" : " '" + err + "'");
                return "and no error field either; the reply was " + Describe(m2);
            }
            catch (Exception ex)
            {
                return "and the reply did not parse (" + ex.GetType().Name + "): " + Describe(m2);
            }
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
            if (m2 == null || m2.Length < 4 || !IsM2(m2))
                return false;
            // mepty returns the session id as u8 (0x09) for small ids and u32 (0x08) for ids > 255. Unlike the
            // other u8-or-u32 readers, a .id of another type does not end the search.
            foreach (var f in Walk(m2))
            {
                if (f.Key != WinboxM2Protocol.RecordKey.Id) continue;
                int? id = ReadU8OrU32(m2, f);
                if (id != null) { sessionId = id.Value; return true; }
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
            if (!IsM2(m2) || !TryFindField(m2, fullKey, out var f)) return null;
            // A field whose declared length runs past the end of the frame is truncated: echoing a
            // malformed cursor would make the next request the router's problem to reject. Drop it and
            // let the caller treat the page as the last one.
            if (f.End > m2.Length) return null;
            var slice = new byte[f.End - f.Start];
            Buffer.BlockCopy(m2, f.Start, slice, 0, slice.Length);
            return slice;
        }

        // The bytes of the first user-namespace field with keyId that is a string or a raw, in either size form;
        // null when there is none or its length is cut off.
        internal static byte[]? ParseUserBytes(byte[] m2, int keyId)
        {
            if (m2 == null || m2.Length < 4) return null;
            foreach (var f in Walk(m2))
            {
                if (f.Key >> 16 != 0 || (f.Key & 0xFFFF) != keyId) continue;
                int pos = f.ValueStart;
                if (f.Type == (TypeByte.Raw | TypeByte.Short) || f.Type == (TypeByte.String | TypeByte.Short))
                {
                    if (pos >= m2.Length) return null;
                    int len = m2[pos++];
                    return m2.Skip(pos).Take(len).ToArray();
                }
                if (f.Type == TypeByte.Raw || f.Type == TypeByte.String)
                {
                    if (pos + 2 > m2.Length) return null;
                    int len = BitConverter.ToUInt16(m2, pos); pos += 2;
                    return m2.Skip(pos).Take(len).ToArray();
                }
            }
            return null;
        }

        internal static int ParseSysStatus(byte[] m2) => ReadU8OrU32(m2, WinboxM2Protocol.SysKey.ErrorCode) ?? 0;

        /// <summary>
        /// Reads the request id (<c>0xFF0006</c>) from an M2 message, or <c>null</c> when the field is absent.
        /// </summary>
        /// <remarks>
        /// RouterOS echoes the request id back on the response, which makes it the correlation key for
        /// dispatching replies to concurrent in-flight requests (verified live — see
        /// <c>Docs/findings-winbox.md §12.1</c>). Note that <c>0xFF0003</c> is <b>not</b> a
        /// correlation field despite looking like one in a single-exchange trace: it stays constant for the
        /// whole session while this value tracks the request (§12.2). u8 in practice (NextReqIdField emits
        /// U8Sys); u32 accepted for symmetry with SESSION_ID.
        /// </remarks>
        internal static int? ParseSysReqId(byte[]? m2) => ReadU8OrU32(m2, WinboxM2Protocol.SysKey.RequestId);

        /// <summary>
        /// Reads a top-level u8/u32 system field (such as <c>SYS_CMD</c>, <c>0xFF0007</c>), or <c>null</c> when it is
        /// absent.
        /// </summary>
        internal static int? ParseSysU32(byte[] m2, int fullKey) => ReadU8OrU32(m2, fullKey);

        /// <summary>
        /// Reads a top-level u32-array field (such as <c>SYS_FROM</c>), or <c>null</c> when it is absent.
        /// </summary>
        internal static int[]? ParseU32ArrayField(byte[] m2, int fullKey)
        {
            int[]? found = null;
            RewriteU32ArrayField(m2, fullKey, values => found = values);
            return found;
        }

        /// <summary>
        /// Returns <paramref name="m2"/> with its top-level u32-array field <paramref name="fullKey"/> replaced by
        /// what <paramref name="rewrite"/> makes of it, re-encoded in the normal-width form (type <c>0x88</c>, a
        /// 2-byte count). A message without the field comes back unchanged — the same instance.
        /// </summary>
        /// <remarks>
        /// What the RoMON relay uses to put a link's <c>[2, link]</c> in front of <c>SYS_TO</c> and take it off
        /// <c>SYS_FROM</c> (<c>Docs/findings-romon.md</c> §5). Only the top level is walked (<see cref="Walk"/>), so a
        /// nested message holding the same key is never touched.
        /// </remarks>
        internal static byte[] RewriteU32ArrayField(byte[] m2, int fullKey, Func<int[], int[]> rewrite)
        {
            if (!IsM2(m2)) return m2!;
            foreach (var f in Walk(m2))
            {
                if (f.Key != fullKey || M2Types.FromTypeByte(f.Type) != M2Type.U32Array) continue;
                int p = f.ValueStart;
                int count = ReadCounter(m2, ref p, LenWidth(f.Type));
                var values = new int[count];
                for (int i = 0; i < count; i++) values[i] = (int)BitConverter.ToUInt32(m2, p + 4 * i);
                byte[] field = U32ArraySys(fullKey, rewrite(values));
                var result = new byte[m2.Length - (f.End - f.Start) + field.Length];
                Buffer.BlockCopy(m2, 0, result, 0, f.Start);
                Buffer.BlockCopy(field, 0, result, f.Start, field.Length);
                Buffer.BlockCopy(m2, f.End, result, f.Start + field.Length, m2.Length - f.End);
                return result;
            }
            return m2;
        }

        // Returns number of bytes to skip for a given TLV type byte (not counting the type byte itself), and 0 for
        // a type byte no ftype covers — which Walk does not step over (see ValueBytes).
        internal static int SkipTypeBytes(int type, byte[] data, int pos) => ValueBytes(type, data, pos) ?? 0;

        // The length of a field's value: the one skip rule. The 0xA0 str_array case MUST be kept — RouterOS 7.21.4
        // sends it in mepty responses (e.g. "msg-proxy-7.21.4"); without it the parser walks into the payload and
        // misaligns.
        //
        // The type byte is (ftype << 3) | size-flags (short=1, long=2), and the ftype table is in master*.js:
        // 0 bool, 1 u32, 2 u64, 3 addr6, 4 string, 5 message, 6 raw, and the same +16 for the array forms.
        // EVERY ftype must have a case here even when ParseAllFields has no decoder for it: an unhandled type
        // is null, and Walk stops there — the rest of the message is lost rather than decoded into garbage keys,
        // which is what an unhandled type did while the walkers stepped over it as if it had no value. That is how
        // the 0xA0 str_array bug behaved before it was found, and 0x18 (addr6) was the same trap waiting on any
        // handler with an IPv6 field.
        private static int? ValueBytes(int type, byte[] data, int pos)
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
                // string long / raw long: a 4-byte length, the third size flag ReadLen already reads and the
                // one this switch was missing. Nothing we send uses it and no reply has been seen carrying
                // one, which is precisely why it had to be here: an unhandled type does not fail, it ends the
                // walk and every field after it is lost.
                case 0x22: return pos + 3 < data.Length ? 4 + (int)BitConverter.ToUInt32(data, pos) : 4;
                case 0x32: return pos + 3 < data.Length ? 4 + (int)BitConverter.ToUInt32(data, pos) : 4;
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
                default: return null;  // unknown type — its length is unknown, so the walk stops
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
