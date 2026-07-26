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
        // Low-level wire-protocol plumbing (packet/control constants, transport state, framing helpers) is
        // documented inline rather than with per-member XML docs — suppress the missing-doc warning for them.
#pragma warning disable CS1591
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
        protected uint       _inCounter;          // cumulative DATA payload bytes received (for ACK + dedup)

        // Outbound reliability (P2.19). The MAC layer is UDP with no delivery guarantee, and RouterOS
        // answers a DATA packet with an ACK whose counter is the offset PAST it (send 148+7 -> ack 155).
        // We used to discard every ACK and never resend, so a lost outbound datagram was unrecoverable:
        // the router waits for bytes that never arrive, we wait for a reply that cannot come.
        //
        // NOTE, so this is not mis-read as a cure: retransmission does NOT fix the P2.19 suite wedge.
        // Measured - at the wedge the router re-ACKs the offset BEFORE our command and then ignores 8
        // byte-identical resends, and the failure is deterministic across runs (same test, same point),
        // which loss is not. Whatever stops the router consuming our stream there is still unidentified.
        // This stays because the ACK is a real signal we were throwing away and a genuinely lost packet
        // must be recoverable; it is not the answer to the wedge.
        private byte[] _lastDataPacket;            // the exact datagram, so a resend is byte-identical
        private uint   _lastDataEnd;               // offset past it - what the router's ACK must reach
        private uint   _highestAck;                // highest ACK counter the router has returned
        private bool   _haveAck;                   // distinguishes "no ACK yet" from a genuine ack of 0
        private int      _retransmits;             // resends spent on _lastDataPacket
        private DateTime _lastRetransmitUtc;       // rate limit, so a 20 ms poll loop cannot flood
        private const int MaxRetransmits = 8;
        private const int MinRetransmitIntervalMs = 400;   // 8 tries ~= 3.2 s, well inside a 30 s read

        // ── AES / HMAC stream keys (derived after EC-SRP5, used by WinBox MAC) ──
        protected byte[] _sendAesKey, _receiveAesKey, _sendHmacKey, _receiveHmacKey;

        // ── Optional router MAC override ──────────────────────────────────────────

        /// <summary>
        /// Optional: router MAC address as "AA:BB:CC:DD:EE:FF" to bypass MNDP discovery
        /// (MNDP takes up to 5 s). Set before calling <see cref="BaseConnect"/>.
        /// </summary>
        protected string RouterMacOverride { get; set; }

        /// <summary>
        /// Wire-trace channel id for this transport. MAC-Telnet and WinBox-MAC share this base and both
        /// run on UDP 20561, but they are separate sessions with independent counter spaces — emitting
        /// them under one channel id makes every per-session reading of a trace wrong (a stream offset
        /// from one session read as a gap or an idle period in the other). They must stay distinguishable.
        /// </summary>
        protected virtual string WireTraceChannel => "macudp";

        // ── Initialise UDP socket and resolve router MAC address ─────────────────

        /// <summary>
        /// Initialises the UDP socket, discovers MACs, and sends the SESSIONSTART broadcast.
        /// Must be called by subclass login methods before authentication.
        /// </summary>
        protected void BaseConnect(string host, ushort clientType)
        {
            _clientType = clientType;

            // Source MAC, local IPv4 and subnet broadcast must all come from the SAME NIC — see the
            // bind below for why picking them independently is not enough.
            var nic = SelectLocalNic(host);
            _localMac   = nic.Mac ?? GetLocalMac(host);
            _routerMac  = GetRouterMacAddress(host);

            byte[] kb = new byte[2];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);
            _sessionKey = BitConverter.ToUInt16(kb, 0);

            // Bind to the local address of the NIC whose MAC and broadcast address are in the packet,
            // NOT to IPAddress.Any. An unbound socket lets the HOST's broadcast route decide which
            // interface the datagram leaves by, and that route need not be the NIC we just described in
            // the packet. Measured failure mode: a DISCONNECTED adapter holding a stale DHCP lease in the
            // router's subnet keeps its '<subnet>.255/32' on-link route installed, and wins broadcast
            // routing on interface metric — while its address is 'Deprecated' and so is correctly skipped
            // for UNICAST source selection. Result: every IP transport works, MNDP still shows the router
            // (the router broadcasts to us — our send path is not involved), and every SESSIONSTART leaves
            // via the dead NIC and vanishes. Binding is exactly the A/B difference: same packet, same
            // source MAC, bound = ACK, unbound = no reply.
            // IPAddress.Any is kept as the fallback when no NIC sits in the router's subnet (a router
            // reached through a gateway), so that case does not regress.
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(nic.LocalIp ?? IPAddress.Any, 0));

            // SESSIONSTART goes to subnet broadcast; DATA and ACK go to known unicast IP.
            IPAddress broadcastAddr = nic.Broadcast ?? GetBroadcastAddress(host);
            _routerEp        = new IPEndPoint(broadcastAddr, 20561);
            _routerUnicastEp = new IPEndPoint(IPAddress.Parse(host), 20561);
            _outCounter = 0;
            _inCounter  = 0;
        }

        /// <summary>
        /// Acknowledges a received DATA packet and reports whether it carries new data.
        /// <para>
        /// The MAC-layer counter is the cumulative byte offset of the stream, so the ACK must
        /// reference the offset <em>past</em> this packet (<c>counter + payloadLen</c>) — ACKing the
        /// packet's own start offset makes RouterOS believe the packet was lost and retransmit it
        /// indefinitely, which derails the cursor-probe terminal negotiation. Retransmissions
        /// (<c>counter &lt; _inCounter</c>) are still ACKed but must not be reprocessed (doing so would
        /// corrupt the VT100 cursor state and the accumulated output buffer).
        /// </para>
        /// </summary>
        /// <returns><c>true</c> if the packet is new and should be processed; <c>false</c> for a duplicate.</returns>
        protected bool AckData(uint counter, int payloadLen)
        {
            // Deliver strictly in order. The counter is a stream offset, so a packet starting PAST
            // _inCounter means the datagrams in between were lost. ACKing it (as this used to) tells the
            // router those bytes arrived and it never resends them - silent data loss. Dropping it
            // unacked leaves our ACK on the last contiguous byte, which is the signal that makes
            // RouterOS retransmit the hole.
            if (counter > _inCounter)
            {
                SendAck(_inCounter);
                return false;
            }

            // A duplicate/retransmit: re-ACK the HIGH-WATER MARK, never the duplicate's own end offset.
            // The latter regresses the ACK below what we already hold, and the router answers a
            // regressed ACK by retransmitting - the 7-in-a-row burst the P2.19 trace caught sitting on
            // top of one of the two wedges.
            if (counter < _inCounter)
            {
                SendAck(_inCounter);
                return false;
            }

            _inCounter = counter + (uint)payloadLen;
            SendAck(_inCounter);
            return true;
        }

        /// <summary>
        /// Records an ACK from the router. Its counter is the stream offset the router has consumed up
        /// to, so it is what <see cref="RetransmitIfUnacked"/> compares the last sent packet against.
        /// </summary>
        protected void NoteAck(uint counter)
        {
            if (!_haveAck || counter > _highestAck)
            {
                _highestAck = counter;
                _haveAck    = true;
            }
        }

        /// <summary>
        /// Resends the last DATA packet if the router has not acknowledged it. Called from the read loops
        /// when nothing is arriving - that idle moment is precisely the "my command never landed" case.
        /// The resend is byte-identical (same counter), so a packet that did arrive is simply seen as a
        /// duplicate and re-ACKed.
        /// <para>
        /// Rate-limited internally rather than by the caller, because the read loops poll at very
        /// different cadences (500 ms in the terminal loops, 20 ms in <see cref="RecvUntil"/>) and a
        /// caller-paced resend would flood the router from the fast one.
        /// </para>
        /// </summary>
        /// <returns><c>true</c> if a retransmission was sent.</returns>
        protected bool RetransmitIfUnacked()
        {
            if (_lastDataPacket == null || !_haveAck || _highestAck >= _lastDataEnd)
                return false;
            if (_retransmits >= MaxRetransmits)
                return false;
            if ((DateTime.UtcNow - _lastRetransmitUtc).TotalMilliseconds < MinRetransmitIntervalMs)
                return false;

            _lastRetransmitUtc = DateTime.UtcNow;
            _retransmits++;
            _udp.Send(_lastDataPacket, _lastDataPacket.Length, _routerUnicastEp);

            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                    "RETRANSMIT #" + _retransmits + " end=" + _lastDataEnd + " highestAck=" + _highestAck);
            return true;
        }

        /// <summary>
        /// The one NIC used for the whole MAC-layer exchange: its MAC goes in the packet header, its
        /// address is what the socket binds to, and its subnet broadcast is where SESSIONSTART is sent.
        /// Resolving all three together is the point — see the bind in <see cref="BaseConnect"/>.
        /// Every field is <c>null</c> when no live NIC sits in the router's subnet (router behind a
        /// gateway), and the caller then falls back to the individual lookups plus an unbound socket.
        /// </summary>
        private struct LocalNic
        {
            public byte[] Mac;
            public IPAddress LocalIp;
            public IPAddress Broadcast;
        }

        private static LocalNic SelectLocalNic(string host)
        {
            var result = new LocalNic();

            IPAddress target;
            try { target = IPAddress.Parse(host); } catch { return result; }
            byte[] tb = target.GetAddressBytes();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)             continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)   continue;

                byte[] mac = ni.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length != 6 || !mac.Any(b => b != 0)) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (ua.IPv4Mask == null) continue;

                    byte[] lb = ua.Address.GetAddressBytes();
                    byte[] mb = ua.IPv4Mask.GetAddressBytes();

                    bool same = true;
                    for (int i = 0; i < 4; i++)
                        if ((lb[i] & mb[i]) != (tb[i] & mb[i])) { same = false; break; }
                    if (!same) continue;

                    byte[] bcast = new byte[4];
                    for (int i = 0; i < 4; i++) bcast[i] = (byte)(tb[i] | ~mb[i]);

                    result.Mac       = mac;
                    result.LocalIp   = ua.Address;
                    result.Broadcast = new IPAddress(bcast);
                    return result;
                }
            }

            return result;
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
                if (type == PKT_ACK) { NoteAck(counter); return false; }
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                if (!AckData(counter, payload.Length)) return false;   // duplicate — ignore
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
                if (type == PKT_ACK) { NoteAck(counter); return false; }
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                if (!AckData(counter, payload.Length)) return false;   // duplicate — ignore
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

            // The counter belongs in the note: it is the stream offset this packet claims, and the
            // router's ACK counter is what it must be compared against. Without it the trace cannot
            // answer "did the router acknowledge everything we sent" (P2.19).
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Send,
                    payload, 0, payload?.Length ?? 0,
                    "type=0x" + type.ToString("x2") + " counter=" + counter);

            if (type == PKT_DATA && payload != null && payload.Length > 0)
            {
                _outCounter += (uint)payload.Length;
                // Hold it for retransmission until the router's ACK reaches past it (P2.19).
                _lastDataPacket = pkt;
                _lastDataEnd    = counter + (uint)payload.Length;
                _retransmits    = 0;
            }
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

            // Traced because a regressing ACK cannot be seen from the receive side alone - the router's
            // reaction (a retransmit burst) reads as router misbehaviour until you can see what we told
            // it (P2.19).
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Send,
                    "type=0x02 ack=" + ackCounter);
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
                    RetransmitIfUnacked();   // self-rate-limited; safe at this 20 ms cadence
                    Thread.Sleep(20);
                }
            }
            throw new TimeoutException("Timed out waiting for expected MAC-layer packet");
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
#pragma warning restore CS1591
    }
}
