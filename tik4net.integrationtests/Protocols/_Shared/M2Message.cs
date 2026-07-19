// M2Message.cs — Winbox M2 TLV builder, parser, and SkipTypeBytes
// Shared by WinboxM2Client (TCP) and WinboxMacClient (MAC).
// Extracted from WinboxM2CatalogTest.cs and MacLayerTest.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Builds and parses Winbox M2 TLV messages.
    /// Wire format: b'M2' + concatenated TLV fields.
    /// </summary>
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

        // SESSION_ID: key=(0x01,0x00,0xFE). u8 for id<=255, else u32 (handles >255, e.g. 265).
        internal static byte[] SessionIdField(int id)
            => id >= 0 && id <= 255
                ? new byte[] { 0x01, 0x00, 0xFE, 0x09, (byte)id }
                : SessionIdFieldU32(id);

        // SESSION_ID u32: key=(0x01,0x00,0xFE) — use when id > 255
        internal static byte[] SessionIdFieldU32(int id)
        {
            var b = new List<byte> { 0x01, 0x00, 0xFE, 0x08 };
            b.AddRange(BitConverter.GetBytes((uint)id));
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

        // ── TLV parsing ───────────────────────────────────────────────────────

        // ── M2 type-byte decomposition (matches webfig master.js) ─────────────
        // The 4th header byte = (ftype<<3) | sizeFlags, where sizeFlags = FS_SHORT(0x01) | FS_LONG(0x02).
        // ftype categories (from webfig): 0=bool 1=u32 2=u64 3=addr6 4=string 5=message 6=raw
        //   16=bool[] 17=u32[] 18=u64[] 19=addr6[] 20=string[] 21=message[] 22=raw[]
        // Length/count encoding (webfig readLen): always 1 byte; +1 if NOT short; +2 more if long.
        //   → normal=2B, short=1B, long=4B.
        internal static int FType(int type) => type >> 3;
        private static bool IsShort(int type) => (type & 0x01) != 0;
        private static bool IsLong(int type)  => (type & 0x02) != 0;

        // Read a length/count using the type's size flags; advances pos.
        private static int ReadLen(int type, byte[] d, ref int pos)
        {
            int len = pos < d.Length ? d[pos++] : 0;
            if (!IsShort(type) && pos < d.Length) len |= d[pos++] << 8;
            if (IsLong(type)) { if (pos < d.Length) len |= d[pos++] << 16; if (pos < d.Length) len |= d[pos++] << 24; }
            return len;
        }

        // Parse a message-array field (ftype 21) into a list of record field-dicts.
        // Each element on the wire is a full submessage ('M2' + TLVs). Returns
        // the records found under fullKey, or empty list if not present / wrong type.
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
        internal static Dictionary<int, Tuple<string, object>> ParseAllFields(byte[] m2)
        {
            var result = new Dictionary<int, Tuple<string, object>>();
            if (m2 == null || m2.Length < 2 || m2[0] != 'M' || m2[1] != '2') return result;
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                int fullKey = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                object val = null;
                string typeName = "?";
                switch (type)
                {
                    case 0x00: typeName = "bool"; val = false; break;
                    case 0x01: typeName = "bool"; val = true;  break;
                    case 0x09:
                        typeName = "u8"; val = pos < m2.Length ? (object)m2[pos] : null; pos += 1; break;
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
                    case 0x21:
                        typeName = "str";
                        if (pos < m2.Length) { int l = m2[pos++]; val = Encoding.UTF8.GetString(m2, pos, Math.Min(l, m2.Length-pos)); pos += l; }
                        break;
                    case 0x20:
                        typeName = "str_l";
                        if (pos + 2 <= m2.Length) { int l = BitConverter.ToUInt16(m2, pos); pos += 2; val = Encoding.UTF8.GetString(m2, pos, Math.Min(l, m2.Length-pos)); pos += l; }
                        break;
                    case 0x31:
                        typeName = "raw";
                        if (pos < m2.Length) { int l = m2[pos++]; val = BitConverter.ToString(m2, pos, Math.Min(l, 32)).Replace("-",""); pos += l; }
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
                    result[fullKey] = Tuple.Create(typeName, val);
            }
            return result;
        }

        internal static int ParseSessionId(byte[] m2)
        {
            if (m2 == null || m2.Length < 4 || m2[0] != 'M' || m2[1] != '2')
                throw new InvalidOperationException("Not a valid M2 response");
            int pos = 2;
            while (pos + 4 <= m2.Length)
            {
                int kl = m2[pos], kh = m2[pos+1], ns = m2[pos+2], type = m2[pos+3];
                int fullKey = (ns << 16) | (kh << 8) | kl;
                pos += 4;
                if (fullKey == 0xFE0001)
                {
                    if (type == 0x09 && pos < m2.Length)
                        return m2[pos];
                    if (type == 0x08 && pos + 4 <= m2.Length)
                        return (int)BitConverter.ToUInt32(m2, pos);
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            throw new InvalidOperationException("No SESSION_ID in M2 response");
        }

        // Like ParseRawUser but also handles string_s/string_l encodings.
        internal static byte[] ParseUserBytes(byte[] m2, int keyId)
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

        internal static byte[] ParseRawUser(byte[] m2, int keyId)
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

        // Returns number of bytes to skip for a given TLV type byte (not counting the type byte itself).
        // Handles the 0xA0 str_array trap — must be kept or parsing misaligns.
        internal static int SkipTypeBytes(int type, byte[] data, int pos)
        {
            switch (type)
            {
                case 0x00: case 0x01: return 0;
                case 0x09: return 1;
                case 0x08: return 4;
                case 0x10: return 8;
                case 0x21: return pos < data.Length ? 1 + data[pos] : 1;
                case 0x20: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) : 2;
                case 0x31: return pos < data.Length ? 1 + data[pos] : 1;
                case 0x30: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) : 2;
                case 0x29: return pos < data.Length ? 1 + data[pos] : 1;
                case 0x28: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) : 2;
                case 0x2A: return pos + 3 < data.Length ? 4 + (int)BitConverter.ToUInt32(data, pos) : 4; // message long
                case 0x88: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) * 4 : 2;
                // str_array / msg_array: count + (len + data) per entry — skip sum of all entry sizes.
                // Normal form (no flags): 2B count, 2B per-element len. Long form (0xA2/0xAA): 4B/4B.
                case 0xA0: case 0xA8: case 0xA2: case 0xAA:
                {
                    bool lng = (type & 0x02) != 0;
                    int p = pos;
                    int cnt = lng ? (p + 3 < data.Length ? (int)BitConverter.ToUInt32(data, p) : 0)
                                  : (p + 1 < data.Length ? BitConverter.ToUInt16(data, p) : 0);
                    p += lng ? 4 : 2;
                    for (int i = 0; i < cnt && p < data.Length; i++)
                    {
                        int elen = lng ? (p + 3 < data.Length ? (int)BitConverter.ToUInt32(data, p) : 0)
                                       : (p + 1 < data.Length ? BitConverter.ToUInt16(data, p) : 0);
                        p += (lng ? 4 : 2) + elen;
                    }
                    return p - pos;
                }
                default: return 0;  // unknown type — stop parsing
            }
        }
    }
}
