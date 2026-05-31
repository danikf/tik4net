// MacLayerTransport.cs — UDP 20561 MACTelnet 22-byte framing + ACK/PING/PONG
// Shared by MacTelnetClient (client_type=0x0015) and WinboxMacClient (client_type=0x0f90).
// Extracted from MacLayerTest.cs.
//
// Packet format (22-byte header):
//   [0]      version = 1
//   [1]      type    (0=SESSIONSTART, 1=DATA, 2=ACK, 4=PING, 5=PONG, 255=END)
//   [2–7]    source MAC address
//   [8–13]   destination MAC address
//   [14–15]  session_key  (client→server) / client_type (server→client)
//   [16–17]  client_type  (client→server) / session_key (server→client)
//   [18–21]  counter, big-endian uint32
//
// Control packet (inside DATA payload, starts with magic 56 34 12 FF):
//   [0–3]    magic: 0x56, 0x34, 0x12, 0xFF
//   [4]      control type
//   [5–8]    data length, big-endian uint32
//   [9+]     data

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Mndp;

namespace tik4net.tests
{
    internal abstract class MacLayerTransport : IDisposable
    {
        // ── Packet type constants ────────────────────────────────────────────────
        protected const byte PKT_SESSIONSTART = 0;
        protected const byte PKT_DATA         = 1;
        protected const byte PKT_ACK          = 2;
        protected const byte PKT_PING         = 4;
        protected const byte PKT_PONG         = 5;
        protected const byte PKT_END          = 255;

        // ── Control packet type constants ────────────────────────────────────────
        protected const byte CTRL_BEGINAUTH   = 0;
        protected const byte CTRL_PASSSALT    = 1;
        protected const byte CTRL_PASSWORD    = 2;
        protected const byte CTRL_USERNAME    = 3;
        protected const byte CTRL_TERM_TYPE   = 4;
        protected const byte CTRL_TERM_WIDTH  = 5;
        protected const byte CTRL_TERM_HEIGHT = 6;
        protected const byte CTRL_END_AUTH    = 9;

        // ── Transport state ──────────────────────────────────────────────────────
        protected UdpClient  _udp;
        protected IPEndPoint _routerEp;    // unicast/broadcast to router
        protected byte[]     _localMac;
        protected byte[]     _routerMac;
        protected ushort     _sessionKey;
        protected ushort     _clientType;
        protected uint       _outCounter;  // cumulative payload bytes sent in DATA pkts

        // ── AES / HMAC stream keys (derived after EC-SRP5, used by Winbox MAC) ──
        protected byte[] _sendAesKey, _receiveAesKey, _sendHmacKey, _receiveHmacKey;

        // ── Initialise UDP socket and resolve router MAC address ─────────────────
        protected void BaseConnect(string host, ushort clientType)
        {
            _clientType = clientType;
            _localMac   = GetLocalMac();
            _routerMac  = GetRouterMac(host);

            byte[] kb = new byte[2];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);
            _sessionKey = BitConverter.ToUInt16(kb, 0);

            int srcPort = 50000 + new Random().Next(1000, 5000);
            _udp = new UdpClient(srcPort) { EnableBroadcast = true };
            _udp.Client.ReceiveTimeout = 5000;
            Console.WriteLine($"[connect] Bound to srcPort {srcPort}");

            _routerEp   = new IPEndPoint(GetBroadcastAddress(host), 20561);
            Console.WriteLine($"[connect] Sending to broadcast {_routerEp}, " +
                $"srcMac={string.Join(":", _localMac.Select(b => b.ToString("X2")))}, " +
                $"dstMac={string.Join(":", _routerMac.Select(b => b.ToString("X2")))}");
            _outCounter = 0;
        }

        // Derives the broadcast address for the subnet containing host.
        private static IPAddress GetBroadcastAddress(string host)
        {
            IPAddress target = IPAddress.Parse(host);
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] localBytes  = ua.Address.GetAddressBytes();
                    byte[] maskBytes   = ua.IPv4Mask.GetAddressBytes();
                    byte[] targetBytes = target.GetAddressBytes();
                    if ((localBytes[0] & maskBytes[0]) == (targetBytes[0] & maskBytes[0]) &&
                        (localBytes[1] & maskBytes[1]) == (targetBytes[1] & maskBytes[1]) &&
                        (localBytes[2] & maskBytes[2]) == (targetBytes[2] & maskBytes[2]) &&
                        (localBytes[3] & maskBytes[3]) == (targetBytes[3] & maskBytes[3]))
                    {
                        byte[] bcast = new byte[4];
                        for (int i = 0; i < 4; i++) bcast[i] = (byte)(targetBytes[i] | ~maskBytes[i]);
                        return new IPAddress(bcast);
                    }
                }
            }
            // Fallback: /24 broadcast
            var tb = target.GetAddressBytes();
            return new IPAddress(new byte[] { tb[0], tb[1], tb[2], 255 });
        }

        // ── EC-SRP5 authentication ───────────────────────────────────────────────
        protected void Authenticate(string user, string pass)
        {
            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);
            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);

            // SESSIONSTART
            Send(PKT_SESSIONSTART, null);
            Thread.Sleep(80);

            // DATA: BEGINAUTH + PASSSALT (username\0 + xWA[32] + parity[1])
            byte[] psd = Encoding.UTF8.GetBytes(user)
                .Concat(new byte[] { 0 }).Concat(xWA).Concat(new byte[] { (byte)parityA })
                .ToArray();
            Send(PKT_DATA,
                BuildCtrl(CTRL_BEGINAUTH, new byte[0])
                .Concat(BuildCtrl(CTRL_PASSSALT, psd)).ToArray());

            // Wait for server PASSSALT (49B: xWB[32]+parityB[1]+salt[16])
            byte[] xWB = null; int parityB = 0; byte[] salt = null;
            RecvUntil(10000, (type, payload, counter) =>
            {
                if (type == PKT_ACK) return false;
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                SendAck(counter);
                foreach (var (ct, cd) in ParseCtrl(payload))
                {
                    if (ct == CTRL_PASSSALT && cd.Length == 49)
                    {
                        xWB     = cd.Take(32).ToArray();
                        parityB = cd[32];
                        salt    = cd.Skip(33).Take(16).ToArray();
                        Console.WriteLine($"[auth] Got server PASSSALT, xWB[0]={xWB[0]:X2} parity={parityB}");
                        return true;
                    }
                    if (ct == CTRL_PASSSALT && cd.Length == 16)
                        throw new NotSupportedException("Legacy MD5 auth not implemented for MAC layer");
                }
                return false;
            });
            if (xWB == null) throw new InvalidOperationException("No server PASSSALT received (auth failed)");

            // EC-SRP5 confirmation (identical math to WinboxM2Client.EcSrp5Auth)
            byte[] valPriv  = EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v       = EcSrp5.Redp1(xGamma, 1);
            ECPoint wB      = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum     = EcSrp5.ECAdd(wB, v);

            byte[]     j   = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
            var        vh  = (EcSrp5.BEToBI(valPriv) * EcSrp5.BEToBI(j) % EcSrp5.R + EcSrp5.BEToBI(privA)) % EcSrp5.R;
            ECPoint    zPt = EcSrp5.ECScalarMul(vh, sum);
            var (zMont, _) = EcSrp5.ToMontgomery(zPt);
            byte[]     Cc  = EcSrp5.Sha256(j.Concat(zMont).ToArray());

            byte[] secret = EcSrp5.Sha256(zMont);
            WinboxStreamCrypto.DeriveStreamKeys(false, secret,
                out _sendAesKey, out _receiveAesKey, out _sendHmacKey, out _receiveHmacKey);
            Console.WriteLine("[auth] Stream keys derived");

            // DATA: PASSWORD + USERNAME + TERM_TYPE + TERM_WIDTH + TERM_HEIGHT
            Send(PKT_DATA,
                BuildCtrl(CTRL_PASSWORD,     Cc)
                .Concat(BuildCtrl(CTRL_USERNAME,    Encoding.UTF8.GetBytes(user)))
                .Concat(BuildCtrl(CTRL_TERM_TYPE,   Encoding.ASCII.GetBytes("vt102")))
                .Concat(BuildCtrl(CTRL_TERM_WIDTH,  BitConverter.GetBytes((ushort)80)))
                .Concat(BuildCtrl(CTRL_TERM_HEIGHT, BitConverter.GetBytes((ushort)25)))
                .ToArray());

            // Wait for END_AUTH
            RecvUntil(10000, (type, payload, counter) =>
            {
                if (type == PKT_ACK) return false;
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                SendAck(counter);
                foreach (var (ct, _) in ParseCtrl(payload))
                    if (ct == CTRL_END_AUTH) { Console.WriteLine("[auth] END_AUTH received"); return true; }
                return false;
            });
        }

        // ── Send / Receive ───────────────────────────────────────────────────────
        protected void Send(byte type, byte[] payload)
        {
            uint counter = (type == PKT_DATA) ? _outCounter : 0u;
            byte[] pkt = new byte[22 + (payload?.Length ?? 0)];
            pkt[0] = 1; pkt[1] = type;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey & 0xFF); pkt[15] = (byte)(_sessionKey >> 8);
            pkt[16] = (byte)(_clientType & 0xFF); pkt[17] = (byte)(_clientType >> 8);
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)(counter & 0xFF);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
            Console.WriteLine($"[send] type={type} sk=0x{_sessionKey:X4} ct=0x{_clientType:X4} ctr={counter} paylen={payload?.Length ?? 0} to {_routerEp}");
            _udp.Send(pkt, pkt.Length, _routerEp);
            if (type == PKT_DATA && payload != null) _outCounter += (uint)payload.Length;
        }

        protected void SendAck(uint ackCounter)
        {
            byte[] pkt = new byte[22];
            pkt[0] = 1; pkt[1] = PKT_ACK;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey & 0xFF); pkt[15] = (byte)(_sessionKey >> 8);
            pkt[16] = (byte)(_clientType & 0xFF); pkt[17] = (byte)(_clientType >> 8);
            pkt[18] = (byte)(ackCounter >> 24); pkt[19] = (byte)(ackCounter >> 16);
            pkt[20] = (byte)(ackCounter >> 8);  pkt[21] = (byte)(ackCounter & 0xFF);
            _udp.Send(pkt, pkt.Length, _routerEp);
        }

        protected void SendPong(uint counter)
        {
            byte[] pkt = new byte[22];
            pkt[0] = 1; pkt[1] = PKT_PONG;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey & 0xFF); pkt[15] = (byte)(_sessionKey >> 8);
            pkt[16] = (byte)(_clientType & 0xFF); pkt[17] = (byte)(_clientType >> 8);
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)(counter & 0xFF);
            _udp.Send(pkt, pkt.Length, _routerEp);
        }

        // Receives packets, calling handler until it returns true (= done). Throws on timeout.
        protected void RecvUntil(int timeoutMs, Func<byte, byte[], uint, bool> handler)
        {
            var sw = Stopwatch.StartNew();
            bool anyReceived = false;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                _udp.Client.ReceiveTimeout = 500;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    anyReceived = true;
                    if (pkt.Length < 22)
                    {
                        Console.WriteLine($"[recv] short pkt len={pkt.Length} from {ep}");
                        continue;
                    }
                    byte   type    = pkt[1];
                    ushort sk14    = (ushort)(pkt[14] | (pkt[15] << 8));
                    ushort ct16    = (ushort)(pkt[16] | (pkt[17] << 8));
                    uint   counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                                     ((uint)pkt[20] <<  8) |  pkt[21];
                    byte[] payload = pkt.Length > 22 ? pkt.Skip(22).ToArray() : new byte[0];
                    string srcMac  = string.Join(":", pkt.Skip(2).Take(6).Select(b => b.ToString("X2")));
                    string dstMac  = string.Join(":", pkt.Skip(8).Take(6).Select(b => b.ToString("X2")));
                    Console.WriteLine($"[recv] from {ep} type={type} sk=0x{sk14:X4} ct=0x{ct16:X4} ctr={counter} paylen={payload.Length} srcMac={srcMac} dstMac={dstMac}");
                    if (payload.Length > 0 && IsControlPacket(payload))
                    {
                        foreach (var (ct, cd) in ParseCtrl(payload))
                            Console.WriteLine($"  ctrl type={ct} datalen={cd.Length}");
                    }
                    if (handler(type, payload, counter)) return;
                }
                catch (SocketException) { /* poll timeout */ }
            }
            if (!anyReceived) Console.WriteLine("[recv] NO packets received at all during timeout");
            throw new TimeoutException("Timed out waiting for expected MAC-layer packet");
        }

        // ── Control packet helpers ───────────────────────────────────────────────
        protected static byte[] BuildCtrl(byte ctrlType, byte[] data)
        {
            uint len = (uint)(data?.Length ?? 0);
            byte[] pkt = new byte[9 + len];
            pkt[0] = 0x56; pkt[1] = 0x34; pkt[2] = 0x12; pkt[3] = 0xFF;
            pkt[4] = ctrlType;
            pkt[5] = (byte)(len >> 24); pkt[6] = (byte)(len >> 16);
            pkt[7] = (byte)(len >>  8); pkt[8] = (byte)(len & 0xFF);
            if (data != null && data.Length > 0) Buffer.BlockCopy(data, 0, pkt, 9, data.Length);
            return pkt;
        }

        protected static (byte ctrlType, byte[] data)[] ParseCtrl(byte[] payload)
        {
            if (payload == null || payload.Length < 9 ||
                payload[0] != 0x56 || payload[1] != 0x34 ||
                payload[2] != 0x12 || payload[3] != 0xFF)
                return new (byte, byte[])[0];
            var result = new List<(byte, byte[])>();
            int pos = 0;
            while (pos + 9 <= payload.Length && payload[pos] == 0x56)
            {
                byte ct  = payload[pos + 4];
                uint len = ((uint)payload[pos+5] << 24) | ((uint)payload[pos+6] << 16) |
                           ((uint)payload[pos+7] <<  8) |  payload[pos+8];
                pos += 9;
                byte[] d = (len > 0 && pos + (int)len <= payload.Length)
                           ? payload.Skip(pos).Take((int)len).ToArray() : new byte[0];
                result.Add((ct, d));
                pos += (int)len;
            }
            return result.ToArray();
        }

        protected static bool IsControlPacket(byte[] payload)
            => payload != null && payload.Length >= 4
            && payload[0] == 0x56 && payload[1] == 0x34
            && payload[2] == 0x12 && payload[3] == 0xFF;

        // ── Network helpers ──────────────────────────────────────────────────────
        private static byte[] GetLocalMac()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)             continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)  continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)    continue;
                var mac = ni.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length == 6 && mac.Any(b => b != 0)) return mac;
            }
            byte[] rand = new byte[6];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(rand);
            rand[0] = (byte)((rand[0] & 0xFE) | 0x02);
            return rand;
        }

        private static byte[] GetRouterMac(string host)
        {
            // 1. App.config override — fastest path:
            //    <add key="routerMac" value="AA:BB:CC:DD:EE:FF"/>
            string macStr = ConfigurationManager.AppSettings["routerMac"];
            if (!string.IsNullOrEmpty(macStr))
            {
                try { return macStr.Split(':').Select(s => Convert.ToByte(s, 16)).ToArray(); }
                catch { /* malformed — fall through to MNDP */ }
            }

            // 2. MNDP discovery (waits up to 5s, filters by IP)
            Console.WriteLine($"[MNDP] Discovering MAC address for router at {host}…");
            var routers = MndpHelper.Discover(TimeSpan.FromSeconds(5),
                              Encoding.GetEncoding("iso-8859-1"), stopWhenFirstFound: false);
            TikInstanceDescriptor? routerFound = routers
                .Cast<TikInstanceDescriptor?>()
                .FirstOrDefault(r => r.Value.IPv4?.ToString() == host);
            if (routerFound.HasValue && !string.IsNullOrEmpty(routerFound.Value.Mac))
            {
                Console.WriteLine($"[MNDP] Found: {routerFound.Value.Identity} at {routerFound.Value.Mac}");
                return routerFound.Value.Mac.Split(':').Select(s => Convert.ToByte(s, 16)).ToArray();
            }

            throw new InvalidOperationException(
                $"Cannot determine MAC address for router {host}. " +
                "Add <add key='routerMac' value='AA:BB:CC:DD:EE:FF'/> to App.config, " +
                "or verify that MNDP (UDP 5678) is enabled on the router.");
        }

        public void Dispose() => _udp?.Dispose();
    }
}
