// MacLayerTransport.cs — UDP 20561 MAC-Telnet/WinBox 22-byte framing + EC-SRP5 auth
//
// Packet format (22-byte header):
//   [0]      version = 1
//   [1]      type    (0=SESSIONSTART, 1=DATA, 2=ACK, 4=PING, 5=PONG, 255=END)
//   [2–7]    source MAC address
//   [8–13]   destination MAC address
//   [14–15]  session_key  big-endian
//   [16–17]  client_type  big-endian
//   [18–21]  counter, big-endian uint32
//
// Control packet (inside DATA payload, starts with magic 56 34 12 FF):
//   [0–3]    magic: 0x56, 0x34, 0x12, 0xFF
//   [4]      control type
//   [5–8]    data length, big-endian uint32
//   [9+]     data

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Crypto;
using tik4net.Mndp;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// Abstract base for MikroTik MAC-layer transports (MAC-Telnet and WinBox MAC).
    /// Handles UDP framing, session management, and EC-SRP5 authentication.
    /// Subclasses implement the application-level protocol (terminal or M2 messages).
    /// </summary>
    public abstract class MacLayerTransport : IDisposable
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
        protected IPEndPoint _routerEp;           // subnet broadcast — used for SESSIONSTART
        protected IPEndPoint _routerUnicastEp;    // known unicast IP:20561 — used for DATA/ACK
        protected byte[]     _localMac;
        protected byte[]     _routerMac;
        protected ushort     _sessionKey;
        protected ushort     _clientType;
        protected uint       _outCounter;         // cumulative DATA payload bytes sent

        // ── AES / HMAC stream keys (derived after EC-SRP5, used by WinBox MAC) ──
        protected byte[] _sendAesKey, _receiveAesKey, _sendHmacKey, _receiveHmacKey;

        // ── Optional router MAC override ──────────────────────────────────────────

        /// <summary>
        /// Optional: router MAC address as "AA:BB:CC:DD:EE:FF" to bypass MNDP discovery
        /// (MNDP takes up to 5 s). Set before calling <see cref="BaseConnect"/>.
        /// </summary>
        protected string RouterMacOverride { get; set; }

        // ── Initialise UDP socket and resolve router MAC address ─────────────────

        /// <summary>
        /// Initialises the UDP socket, discovers MACs, and sends the SESSIONSTART broadcast.
        /// Must be called by subclass login methods before authentication.
        /// </summary>
        protected void BaseConnect(string host, ushort clientType)
        {
            _clientType = clientType;
            _localMac   = GetLocalMac(host);
            _routerMac  = GetRouterMacAddress(host);

            byte[] kb = new byte[2];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);
            _sessionKey = BitConverter.ToUInt16(kb, 0);

            // Bind to OS-assigned port (port 0). The router responds to our source port.
            _udp = new UdpClient(0) { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // SESSIONSTART goes to subnet broadcast; DATA and ACK go to known unicast IP.
            IPAddress broadcastAddr = GetBroadcastAddress(host);
            _routerEp        = new IPEndPoint(broadcastAddr, 20561);
            _routerUnicastEp = new IPEndPoint(IPAddress.Parse(host), 20561);
            _outCounter = 0;
        }

        // Derives subnet broadcast for the subnet containing host.
        private static IPAddress GetBroadcastAddress(string host)
        {
            IPAddress target = IPAddress.Parse(host);
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] lb = ua.Address.GetAddressBytes();
                    byte[] mb = ua.IPv4Mask.GetAddressBytes();
                    byte[] tb = target.GetAddressBytes();
                    if ((lb[0] & mb[0]) == (tb[0] & mb[0]) &&
                        (lb[1] & mb[1]) == (tb[1] & mb[1]) &&
                        (lb[2] & mb[2]) == (tb[2] & mb[2]) &&
                        (lb[3] & mb[3]) == (tb[3] & mb[3]))
                    {
                        byte[] bcast = new byte[4];
                        for (int i = 0; i < 4; i++) bcast[i] = (byte)(tb[i] | ~mb[i]);
                        return new IPAddress(bcast);
                    }
                }
            }
            // Fallback: /24 broadcast
            var x = target.GetAddressBytes();
            return new IPAddress(new byte[] { x[0], x[1], x[2], 255 });
        }

        // ── EC-SRP5 authentication ───────────────────────────────────────────────

        /// <summary>
        /// Performs EC-SRP5 authentication over the MAC layer.
        /// Synchronous version (used by WinBox MAC PoC subclasses).
        /// </summary>
        protected void Authenticate(string user, string pass)
        {
            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);
            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);

            Send(PKT_SESSIONSTART, null);
            Thread.Sleep(80);

            byte[] psd = Encoding.UTF8.GetBytes(user)
                .Concat(new byte[] { 0 }).Concat(xWA).Concat(new byte[] { (byte)parityA })
                .ToArray();
            Send(PKT_DATA,
                BuildCtrl(CTRL_BEGINAUTH, new byte[0])
                .Concat(BuildCtrl(CTRL_PASSSALT, psd)).ToArray());

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
                        xWB    = cd.Take(32).ToArray();
                        parityB = cd[32];
                        salt    = cd.Skip(33).Take(16).ToArray();
                        return true;
                    }
                    if (ct == CTRL_PASSSALT && cd.Length == 16)
                        throw new NotSupportedException("Legacy MD5 auth not supported for MAC layer");
                }
                return false;
            });
            if (xWB == null) throw new InvalidOperationException("No server PASSSALT received (auth failed)");

            FinishAuth(user, pass, privA, xWA, xWB, parityB, salt);
        }

        /// <summary>
        /// Performs EC-SRP5 authentication over the MAC layer.
        /// Async version (used by MacTelnetUdpClient).
        /// </summary>
        protected async Task AuthenticateAsync(string user, string pass, CancellationToken ct)
        {
            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);
            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);

            Send(PKT_SESSIONSTART, null);
            await Task.Delay(80, ct).ConfigureAwait(false);

            byte[] psd = Encoding.UTF8.GetBytes(user)
                .Concat(new byte[] { 0 }).Concat(xWA).Concat(new byte[] { (byte)parityA })
                .ToArray();
            Send(PKT_DATA,
                BuildCtrl(CTRL_BEGINAUTH, new byte[0])
                .Concat(BuildCtrl(CTRL_PASSSALT, psd)).ToArray());

            byte[] xWB = null; int parityB = 0; byte[] salt = null;
            await RecvUntilAsync(10000, (type, payload, counter) =>
            {
                if (type == PKT_ACK) return false;
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                SendAck(counter);
                foreach (var (ctype, cd) in ParseCtrl(payload))
                {
                    if (ctype == CTRL_PASSSALT && cd.Length == 49)
                    {
                        xWB     = cd.Take(32).ToArray();
                        parityB = cd[32];
                        salt    = cd.Skip(33).Take(16).ToArray();
                        return true;
                    }
                    if (ctype == CTRL_PASSSALT && cd.Length == 16)
                        throw new NotSupportedException("Legacy MD5 auth not supported for MAC layer");
                }
                return false;
            }, ct).ConfigureAwait(false);

            if (xWB == null) throw new InvalidOperationException("No server PASSSALT received (auth failed)");

            FinishAuth(user, pass, privA, xWA, xWB, parityB, salt);
        }

        // Shared EC-SRP5 completion: compute shared secret, send CTRL_PASSWORD, wait for END_AUTH.
        private void FinishAuth(string user, string pass,
            byte[] privA, byte[] xWA, byte[] xWB, int parityB, byte[] salt)
        {
            byte[] valPriv  = EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v       = EcSrp5.Redp1(xGamma, 1);
            ECPoint wB      = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum     = EcSrp5.ECAdd(wB, v);

            byte[]    j    = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
            var       vh   = (EcSrp5.BEToBI(valPriv) * EcSrp5.BEToBI(j) % EcSrp5.R + EcSrp5.BEToBI(privA)) % EcSrp5.R;
            ECPoint   zPt  = EcSrp5.ECScalarMul(vh, sum);
            var (zMont, _) = EcSrp5.ToMontgomery(zPt);
            byte[]    Cc   = EcSrp5.Sha256(j.Concat(zMont).ToArray());

            byte[] secret = EcSrp5.Sha256(zMont);
            WinboxStreamCrypto.DeriveStreamKeys(false, secret,
                out _sendAesKey, out _receiveAesKey, out _sendHmacKey, out _receiveHmacKey);

            Send(PKT_DATA,
                BuildCtrl(CTRL_PASSWORD,     Cc)
                .Concat(BuildCtrl(CTRL_USERNAME,    Encoding.UTF8.GetBytes(user)))
                .Concat(BuildCtrl(CTRL_TERM_TYPE,   Encoding.ASCII.GetBytes("vt102")))
                .Concat(BuildCtrl(CTRL_TERM_WIDTH,  BitConverter.GetBytes((ushort)80)))
                .Concat(BuildCtrl(CTRL_TERM_HEIGHT, BitConverter.GetBytes((ushort)25)))
                .ToArray());

            RecvUntil(10000, (type, payload, counter) =>
            {
                if (type == PKT_ACK) return false;
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                SendAck(counter);
                foreach (var (ctype, _) in ParseCtrl(payload))
                    if (ctype == CTRL_END_AUTH) return true;
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
            pkt[14] = (byte)(_sessionKey >> 8);  pkt[15] = (byte)(_sessionKey & 0xFF);
            pkt[16] = (byte)(_clientType >> 8);  pkt[17] = (byte)(_clientType & 0xFF);
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)(counter & 0xFF);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
            var dst = (type == PKT_SESSIONSTART) ? _routerEp : _routerUnicastEp;
            _udp.Send(pkt, pkt.Length, dst);
            if (type == PKT_DATA && payload != null) _outCounter += (uint)payload.Length;
        }

        protected void SendAck(uint ackCounter)
        {
            byte[] pkt = new byte[22];
            pkt[0] = 1; pkt[1] = PKT_ACK;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey >> 8);  pkt[15] = (byte)(_sessionKey & 0xFF);
            pkt[16] = (byte)(_clientType >> 8);  pkt[17] = (byte)(_clientType & 0xFF);
            pkt[18] = (byte)(ackCounter >> 24); pkt[19] = (byte)(ackCounter >> 16);
            pkt[20] = (byte)(ackCounter >> 8);  pkt[21] = (byte)(ackCounter & 0xFF);
            _udp.Send(pkt, pkt.Length, _routerUnicastEp);
        }

        protected void SendPong(uint counter)
        {
            byte[] pkt = new byte[22];
            pkt[0] = 1; pkt[1] = PKT_PONG;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey >> 8);  pkt[15] = (byte)(_sessionKey & 0xFF);
            pkt[16] = (byte)(_clientType >> 8);  pkt[17] = (byte)(_clientType & 0xFF);
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)(counter & 0xFF);
            _udp.Send(pkt, pkt.Length, _routerUnicastEp);
        }

        /// <summary>
        /// Receives packets in a polling loop (synchronous), calling <paramref name="handler"/>
        /// until it returns <c>true</c>. Throws <see cref="TimeoutException"/> if not satisfied
        /// within <paramref name="timeoutMs"/> milliseconds.
        /// </summary>
        protected void RecvUntil(int timeoutMs, Func<byte, byte[], uint, bool> handler)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (_udp.Available > 0)
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    var parsed = ParsePacket(pkt);
                    if (parsed == null) continue;
                    var (type, counter, payload, srcMac) = parsed.Value;
                    if (srcMac.SequenceEqual(_localMac)) continue;  // skip own echo
                    if (handler(type, payload, counter)) return;
                }
                else
                {
                    Thread.Sleep(20);
                }
            }
            throw new TimeoutException("Timed out waiting for expected MAC-layer packet");
        }

        /// <summary>
        /// Receives packets in a polling loop (async), calling <paramref name="handler"/>
        /// until it returns <c>true</c>. Throws <see cref="TimeoutException"/> if not satisfied
        /// within <paramref name="timeoutMs"/> milliseconds.
        /// </summary>
        protected async Task RecvUntilAsync(int timeoutMs, Func<byte, byte[], uint, bool> handler,
            CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (_udp.Available > 0)
                {
                    var result = await _udp.ReceiveAsync().ConfigureAwait(false);
                    var parsed = ParsePacket(result.Buffer);
                    if (parsed == null) continue;
                    var (type, counter, payload, srcMac) = parsed.Value;
                    if (srcMac.SequenceEqual(_localMac)) continue;  // skip own echo
                    if (handler(type, payload, counter)) return;
                }
                else
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                }
            }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("Timed out waiting for expected MAC-layer packet");
        }

        /// <summary>
        /// Tries to receive one packet within <paramref name="pollMs"/> milliseconds.
        /// Returns null if nothing arrives in that window.
        /// Skips own-echo packets automatically.
        /// </summary>
        protected async Task<(byte type, uint counter, byte[] payload)?> TryReceivePacketAsync(
            int pollMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(pollMs);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (_udp.Available > 0)
                {
                    var result = await _udp.ReceiveAsync().ConfigureAwait(false);
                    var parsed = ParsePacket(result.Buffer);
                    if (parsed == null) continue;
                    var (type, counter, payload, srcMac) = parsed.Value;
                    if (srcMac.SequenceEqual(_localMac)) continue;  // skip own echo
                    return (type, counter, payload);
                }
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
            return null;
        }

        // Parses a raw UDP datagram. Returns null if too short.
        private static (byte type, uint counter, byte[] payload, byte[] srcMac)? ParsePacket(byte[] pkt)
        {
            if (pkt == null || pkt.Length < 22) return null;
            byte   type    = pkt[1];
            uint   counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                             ((uint)pkt[20] <<  8) |  pkt[21];
            byte[] payload = pkt.Length > 22 ? new byte[pkt.Length - 22] : new byte[0];
            if (payload.Length > 0) Buffer.BlockCopy(pkt, 22, payload, 0, payload.Length);
            byte[] srcMac  = new byte[6]; Buffer.BlockCopy(pkt, 2, srcMac, 0, 6);
            return (type, counter, payload, srcMac);
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

        private static byte[] GetLocalMac(string host)
        {
            IPAddress target = null;
            try { target = IPAddress.Parse(host); } catch { }

            // Prefer NIC on same subnet as the router (avoids Hyper-V/VPN virtual adapters).
            if (target != null)
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)            continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)   continue;
                    var mac = ni.GetPhysicalAddress().GetAddressBytes();
                    if (mac.Length != 6 || !mac.Any(b => b != 0)) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] lb = ua.Address.GetAddressBytes();
                        byte[] mb = ua.IPv4Mask.GetAddressBytes();
                        byte[] tb = target.GetAddressBytes();
                        bool same = true;
                        for (int i = 0; i < 4; i++)
                            if ((lb[i] & mb[i]) != (tb[i] & mb[i])) { same = false; break; }
                        if (same) return mac;
                    }
                }
            }

            // Fallback: first active non-loopback non-tunnel NIC.
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)            continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)   continue;
                var mac = ni.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length == 6 && mac.Any(b => b != 0)) return mac;
            }

            byte[] rand = new byte[6];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(rand);
            rand[0] = (byte)((rand[0] & 0xFE) | 0x02);
            return rand;
        }

        private byte[] GetRouterMacAddress(string host)
        {
            // RouterMacOverride is a MAC string "AA:BB:CC:DD:EE:FF" — parse directly (no MNDP).
            if (!string.IsNullOrEmpty(RouterMacOverride))
            {
                try { return RouterMacOverride.Split(':').Select(s => Convert.ToByte(s, 16)).ToArray(); }
                catch { /* malformed — fall through to MNDP */ }
            }

            // MNDP discovery via the public core helper (waits up to 5 s).
            byte[] found = MndpHelper.FindMacByHost(host);
            if (found != null) return found;

            throw new InvalidOperationException(
                $"Cannot determine MAC address for router {host}. " +
                "Set MacTelnetConnection.RouterMac = \"AA:BB:CC:DD:EE:FF\", " +
                "or verify that MNDP (UDP 5678) is enabled on the router.");
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose() => _udp?.Dispose();
    }
}
