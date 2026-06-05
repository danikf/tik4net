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

        // SESSION_ID: key=(0x01,0x00,0xFE). RouterOS returns the mepty session id as a u32 when it
        // exceeds 255 (e.g. 265), so encode the same way: u8 (0x09) for small ids, u32 (0x08) otherwise.
        // Sending it back truncated to a single byte addresses the wrong session ("No SESSION_ID" /
        // dead terminal).
        internal static byte[] SessionIdField(int id)
        {
            if (id >= 0 && id <= 255)
                return new byte[] { 0x01, 0x00, 0xFE, 0x09, (byte)id };
            var b = new List<byte> { 0x01, 0x00, 0xFE, 0x08 };
            b.AddRange(BitConverter.GetBytes((uint)id));
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

        // ── TLV parsing ───────────────────────────────────────────────────────

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
                    // mepty returns the session id as u8 (0x09) for small ids and u32 (0x08) for ids > 255.
                    if (type == 0x09 && pos < m2.Length) return m2[pos];
                    if (type == 0x08 && pos + 4 <= m2.Length) return (int)BitConverter.ToUInt32(m2, pos);
                }
                pos += SkipTypeBytes(type, m2, pos);
            }
            throw new InvalidOperationException("No SESSION_ID in M2 response");
        }

        // Parses the bytes of a user-namespace field; handles raw_s/raw_l and string_s/string_l.
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
        // The 0xA0 str_array case MUST be kept — RouterOS 7.21.4 sends it in mepty responses
        // (e.g. "msg-proxy-7.21.4"); without it the parser walks into the payload and misaligns.
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
                case 0x88: return pos + 1 < data.Length ? 2 + BitConverter.ToUInt16(data, pos) * 4 : 2;
                // str_array: 2B count + (2B len + data) per entry — skip sum of all entry sizes
                case 0xA0:
                    if (pos + 1 >= data.Length) return 2;
                    int cnt = BitConverter.ToUInt16(data, pos); int skip = 2;
                    for (int i = 0; i < cnt && pos + skip + 1 < data.Length; i++)
                    {
                        int slen = BitConverter.ToUInt16(data, pos + skip); skip += 2 + slen;
                    }
                    return skip;
                default: return 0;  // unknown type — stop parsing
            }
        }
    }
}
