using System;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// The MikroTik MAC-layer packet: what MAC-Telnet, WinBox CLI over MAC and WinBox native over MAC all ride on
    /// (<c>Docs/mactelnet-protocol.md</c>). Builds and parses the 22-byte header, so no sender or receiver writes or
    /// reads it by hand.
    /// </summary>
    /// <remarks>
    /// <code>
    /// [0]      version = 1
    /// [1]      type    (0=SESSIONSTART, 1=DATA, 2=ACK, 4=PING, 5=PONG, 255=END)
    /// [2–7]    source MAC
    /// [8–13]   destination MAC
    /// [14–15]  a request's session key, a reply's client type   (big-endian)
    /// [16–17]  a request's client type, a reply's session key   (big-endian)
    /// [18–21]  counter, big-endian uint32
    /// [22…]    payload
    /// </code>
    /// The two 16-bit fields swap places by direction, which is why there is one builder (what we send) and one
    /// parser (what the router sends), each with its own order.
    /// </remarks>
    internal static class MacLayerPacket
    {
        /// <summary>
        /// The UDP port of the router's MAC server, for every MAC-layer session whatever it carries. The three public
        /// <c>DefaultPort</c> constants of the MAC transports are this value.
        /// </summary>
        internal const int Port = 20561;

        /// <summary>The header length; the payload starts here.</summary>
        internal const int HeaderLength = 22;

        /// <summary>
        /// Builds one packet as the client sends it: session key at [14], client type at [16].
        /// </summary>
        internal static byte[] Build(byte type, byte[] srcMac, byte[] dstMac, ushort sessionKey, ushort clientType,
            uint counter, byte[]? payload = null)
        {
            byte[] pkt = new byte[HeaderLength + (payload?.Length ?? 0)];
            pkt[0] = 1; pkt[1] = type;
            Buffer.BlockCopy(srcMac, 0, pkt, 2, 6);
            Buffer.BlockCopy(dstMac, 0, pkt, 8, 6);
            WriteUInt16BE(pkt, 14, sessionKey);
            WriteUInt16BE(pkt, 16, clientType);
            WriteUInt32BE(pkt, 18, counter);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, pkt, HeaderLength, payload.Length);
            return pkt;
        }

        /// <summary>
        /// Parses a packet as the router sends it: session key at [16]. False for a datagram shorter than the header.
        /// </summary>
        internal static bool TryParseReply(byte[]? pkt, out MacLayerReply reply)
        {
            if (pkt == null || pkt.Length < HeaderLength)
            {
                reply = default;
                return false;
            }
            byte[] payload = pkt.Length > HeaderLength ? new byte[pkt.Length - HeaderLength] : new byte[0];
            if (payload.Length > 0) Buffer.BlockCopy(pkt, HeaderLength, payload, 0, payload.Length);
            byte[] srcMac = new byte[6];
            Buffer.BlockCopy(pkt, 2, srcMac, 0, 6);
            reply = new MacLayerReply(pkt[1], ReadUInt32BE(pkt, 18), srcMac,
                (ushort)((pkt[16] << 8) | pkt[17]), payload);
            return true;
        }

        private static uint ReadUInt32BE(byte[] b, int at)
            => ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];

        private static void WriteUInt32BE(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }

        private static void WriteUInt16BE(byte[] b, int at, ushort v)
        {
            b[at] = (byte)(v >> 8); b[at + 1] = (byte)v;
        }
    }

    /// <summary>One packet from the router, as <see cref="MacLayerPacket.TryParseReply"/> read it.</summary>
    internal readonly struct MacLayerReply
    {
        internal MacLayerReply(byte type, uint counter, byte[] srcMac, ushort sessionKey, byte[] payload)
        {
            Type = type;
            Counter = counter;
            SrcMac = srcMac;
            SessionKey = sessionKey;
            Payload = payload;
        }

        internal byte Type { get; }
        internal uint Counter { get; }
        internal byte[] SrcMac { get; }
        /// <summary>The session key the reply carries — at [16], the reverse of a request.</summary>
        internal ushort SessionKey { get; }
        /// <summary>The bytes after the header; empty, never null.</summary>
        internal byte[] Payload { get; }
    }
}
