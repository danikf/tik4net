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
        // All five are assigned in BaseConnect (not the constructor) — subclasses must call it before use.
        protected UdpClient  _udp = null!;
        protected IPEndPoint _routerEp = null!;           // subnet broadcast — used for SESSIONSTART
        // The router's own IP:20561, when there is one to send to — DATA and ACK go here. Null means there
        // is not, and then everything goes to _routerEp (subnet broadcast) instead: a session addressed by
        // MAC alone starts that way, and stays that way, because the router never reveals an address to
        // latch onto (see LatchRouterUnicast). Broadcast for a whole session is correct, just noisier.
        protected IPEndPoint? _routerUnicastEp;
        protected byte[]     _localMac = null!;
        protected byte[]     _routerMac = null!;
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
        //
        // A QUEUE, not one slot (P2.42). While every caller was lockstep, one outstanding DATA packet at a
        // time was all there could be, so holding only the most recent one was enough. A multiplexed channel
        // has several requests in flight, and the counter is a CUMULATIVE byte offset: if the first packet is
        // lost and the second arrives, the router can acknowledge neither — the stream has a hole — and the
        // one packet that has to be resent is precisely the one a single slot has already overwritten. That
        // is a permanent stall, not a slow round trip. Retransmission therefore walks the oldest unacked
        // packet, which is the hole by construction.
        private readonly List<Unacked> _unacked = new List<Unacked>();   // send order; guarded by SendGate
        private uint   _highestAck;                // highest ACK counter the router has returned
        private bool   _haveAck;                   // distinguishes "no ACK yet" from a genuine ack of 0
        private int      _retransmits;             // resends spent on the packet currently at the head
        private DateTime _lastRetransmitUtc;       // rate limit, so a 20 ms poll loop cannot flood
        private bool     _budgetNoted;             // the "we have given up on this packet" note is emitted once
        private const int MaxRetransmits = 8;
        private const int MinRetransmitIntervalMs = 400;   // 8 tries ~= 3.2 s, well inside a 30 s read

        // Bounds the queue if the router stops acknowledging altogether: without a cap a caller that keeps
        // writing into a dead session would grow it without limit. Far above any real concurrency (the
        // multiplexer's request ids top out at 255, and outstanding count is normally <10).
        private const int MaxUnackedTracked = 256;

        // One sent DATA datagram held verbatim, so a resend is byte-identical and the router simply sees a
        // duplicate if the original did arrive.
        private struct Unacked
        {
            public byte[]   Packet;
            public uint     End;      // stream offset past this packet — what the router's ACK must reach
            public DateTime SentUtc;  // when it went out — a packet still in normal flight must not be resent
        }

        /// <summary>
        /// Serialises everything that writes to the socket and to the outbound stream state. MAC-Telnet
        /// runs a background receive pump (see <c>MacTelnetUdpClient</c>) that ACKs router-initiated
        /// output and answers VT100 probes, so sends genuinely come from two threads: the pump and the
        /// caller issuing a command. Transports without a pump simply never contend on it.
        /// </summary>
        protected readonly object SendGate = new object();

        // ── AES / HMAC stream keys (derived after EC-SRP5, used by WinBox MAC) ──
        // Assigned in FinishAuth (post-construction, post-BaseConnect), not the constructor.
        protected byte[] _sendAesKey = null!, _receiveAesKey = null!, _sendHmacKey = null!, _receiveHmacKey = null!;

        // ── Optional router MAC override ──────────────────────────────────────────

        /// <summary>
        /// Optional: router MAC address as "AA:BB:CC:DD:EE:FF" to bypass MNDP discovery
        /// (MNDP takes up to 5 s). Set before calling <see cref="BaseConnect"/>.
        /// </summary>
        protected string? RouterMacOverride { get; set; }

        /// <summary>
        /// Wire-trace channel id for this transport. MAC-Telnet and WinBox-MAC share this base and both
        /// run on UDP 20561, but they are separate sessions with independent counter spaces — emitting
        /// them under one channel id makes every per-session reading of a trace wrong (a stream offset
        /// from one session read as a gap or an idle period in the other). They must stay distinguishable.
        /// </summary>
        protected virtual string WireTraceChannel => "macudp";

        // ── Initialise UDP socket and resolve router MAC address ─────────────────

        /// <summary>
        /// Initialises the UDP socket and resolves the MACs. Must be called by subclass login methods
        /// before authentication, which starts the session itself (<see cref="StartSession"/>).
        /// </summary>
        protected void BaseConnect(string? host, ushort clientType)
        {
            _clientType = clientType;
            _routerMac  = GetRouterMacAddress(host);

            byte[] kb = new byte[2];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);
            _sessionKey = BitConverter.ToUInt16(kb, 0);

            // Two ways in, and they differ only in how the local NIC is chosen.
            //
            // WITH a host, the router's IP names the NIC: exactly one candidate, the one whose subnet
            // contains it, and the session is bound there for good.
            //
            // WITHOUT one — the case the MAC transports exist for, a router with no IP at all — nothing
            // points at a NIC, so every eligible NIC becomes a candidate and StartSession asks them all at
            // once, adopting whichever the router answers. It cannot be left to the host's broadcast route
            // (bind to IPAddress.Any): that route can point at a disconnected adapter holding a stale on-link
            // '<subnet>.255/32', and then every SESSIONSTART leaves by a dead NIC and vanishes while every
            // other transport still works — see the bind below.
            _candidates = string.IsNullOrEmpty(host)
                ? EnumerateCandidateNics()
                : new[] { SelectLocalNic(host!) };
            if (_candidates.Length == 0)
                throw new InvalidOperationException(
                    "No usable local network interface found for a MAC-layer connection. " +
                    "At least one non-loopback, non-tunnel interface that is Up and has an IPv4 address is required.");

            _bindHost = host;
            BindToCandidate(_candidates[0], host);
        }

        // The NICs this session may use: a single entry when the router's IP named one, the whole eligible
        // list when nothing did — in which case StartSession probes them and adopts the one that answers.
        private LocalNic[] _candidates = new LocalNic[0];
        private string? _bindHost;

        /// <summary>
        /// Whether the session is addressed by MAC alone, with no router IP anywhere in the picture — the
        /// case these transports exist for. It is the only mode in which the local NIC is not already
        /// determined and <see cref="StartSession"/> has to find it.
        /// </summary>
        protected bool IsMacOnly => string.IsNullOrEmpty(_bindHost);

        /// <summary>
        /// Finds the local NIC the router is reachable on by asking all of them <b>at once</b>: one socket
        /// per candidate, each sending its own SESSIONSTART, and the first one the router acknowledges wins.
        /// The winner's socket, source MAC, broadcast address and session key become the session's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asking them one at a time does not work, and the way it fails is instructive. Each candidate got
        /// one 300 ms round and was then rebound — which <em>closes the socket the answer is coming back
        /// to</em>. Measured inside a full suite run: the correct NIC's ACK took longer than 300 ms because
        /// the router was busy, so it arrived at a socket that no longer existed; the probe then alternated
        /// between two NICs six times and gave up. The session key was the same throughout, so the router
        /// also saw one session arriving from two different source MACs. Nothing about that is specific to
        /// slow routers: a sequential probe races the answer against its own timer and can always lose.
        /// </para>
        /// <para>
        /// Probing in parallel also costs one round instead of N, and each probe carries its own session
        /// key, so an ACK says without ambiguity which NIC it answers.
        /// </para>
        /// </remarks>
        /// <param name="timeoutMs">How long to wait for any candidate to be acknowledged.</param>
        /// <returns><c>true</c> when a candidate was adopted.</returns>
        private bool ProbeCandidatesInParallel(int timeoutMs)
        {
            var probes = new List<CandidateProbe>();
            try
            {
                foreach (var nic in _candidates)
                {
                    var probe = TryCreateProbe(nic);
                    if (probe != null) probes.Add(probe);
                }
                if (probes.Count == 0) return false;

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var resendAt = DateTime.UtcNow;

                while (DateTime.UtcNow < deadline)
                {
                    if (DateTime.UtcNow >= resendAt)
                    {
                        foreach (var probe in probes) probe.SendSessionStart(_routerMac, _clientType);
                        resendAt = DateTime.UtcNow.AddMilliseconds(SessionStartRetryMs);
                    }

                    foreach (var probe in probes)
                    {
                        if (!probe.TryReadAck(_routerMac, out uint ackCounter)) continue;

                        Adopt(probe, probes);
                        NoteAck(ackCounter);
                        return true;
                    }

                    Thread.Sleep(5);
                }

                return false;
            }
            finally
            {
                // Everything not adopted is disposed; Adopt takes the winner out of the list first.
                foreach (var probe in probes) probe.Dispose();
            }
        }

        /// <summary>Makes the winning probe's socket and identity this session's own.</summary>
        private void Adopt(CandidateProbe winner, List<CandidateProbe> probes)
        {
            probes.Remove(winner);          // so the caller's finally does not dispose the socket we keep

            _udp?.Dispose();
            _udp        = winner.Udp;
            _localMac   = winner.LocalMac;
            _routerEp   = winner.Broadcast;
            _sessionKey = winner.SessionKey;
            _outCounter = 0;
            _inCounter  = 0;

            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                    "NIC ADOPTED local=" + _udp.Client.LocalEndPoint
                    + " srcMac=" + BitConverter.ToString(_localMac)
                    + " bcast=" + _routerEp.Address + TraceTag);
        }

        // A NIC being tried: its own socket, source MAC, broadcast address and session key. Deliberately
        // NOT sharing the session's fields — several of these are alive at once, and the whole point is
        // that they cannot be confused for one another.
        private sealed class CandidateProbe : IDisposable
        {
            internal UdpClient Udp = null!;
            internal byte[] LocalMac = null!;
            internal IPEndPoint Broadcast = null!;
            internal ushort SessionKey;
            internal Action<string>? Trace;

            internal void SendSessionStart(byte[] routerMac, ushort clientType)
            {
                byte[] pkt = new byte[22];
                pkt[0] = 1; pkt[1] = PKT_SESSIONSTART;
                Buffer.BlockCopy(LocalMac, 0, pkt, 2, 6);
                Buffer.BlockCopy(routerMac, 0, pkt, 8, 6);
                pkt[14] = (byte)(SessionKey >> 8); pkt[15] = (byte)(SessionKey & 0xFF);
                pkt[16] = (byte)(clientType >> 8); pkt[17] = (byte)(clientType & 0xFF);
                // A NIC that refuses to send is simply not the one; the others are still being tried.
                try
                {
                    Udp.Send(pkt, pkt.Length, Broadcast);
                    Trace?.Invoke("PROBE SEND sessionstart local=" + Udp.Client.LocalEndPoint
                        + " dst=" + Broadcast + " srcMac=" + BitConverter.ToString(LocalMac)
                        + " key=" + SessionKey.ToString("x4"));
                }
                catch (SocketException ex)
                {
                    Trace?.Invoke("PROBE SEND FAILED local=" + Udp.Client.LocalEndPoint + " " + ex.SocketErrorCode);
                }
            }

            /// <summary>Whether the router has acknowledged this probe. Never blocks.</summary>
            internal bool TryReadAck(byte[] routerMac, out uint counter)
            {
                counter = 0;
                try
                {
                    while (Udp.Available > 0)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] pkt = Udp.Receive(ref ep);
                        Trace?.Invoke("PROBE RECV local=" + Udp.Client.LocalEndPoint + " from=" + ep
                            + " len=" + pkt.Length
                            + (pkt.Length >= 22
                                ? " type=0x" + pkt[1].ToString("x2")
                                  + " key=" + (((uint)pkt[16] << 8) | pkt[17]).ToString("x4")
                                  + " srcMac=" + BitConverter.ToString(pkt, 2, 6)
                                : ""));

                        if (pkt.Length < 22 || pkt[1] != PKT_ACK) continue;

                        // The session key rules out an ACK meant for another probe, and the source MAC
                        // rules out another MikroTik on the segment answering our broadcast.
                        //
                        // THE KEY IS AT [16], NOT AT [14]: the header swaps session key and client type by
                        // direction (Docs/mactelnet-protocol.md, "Packet header structure"), so a reply
                        // carries the client type where our own send carries the key. Reading it at [14]
                        // matches 0x0015 — the client type — against the key, rejects every ACK the router
                        // sends, and the probe then loses to its own timeout with the router answering
                        // correctly the whole time.
                        if (pkt[16] != (byte)(SessionKey >> 8) || pkt[17] != (byte)(SessionKey & 0xFF)) continue;
                        if (!IsFrom(pkt, routerMac)) continue;

                        counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) | ((uint)pkt[20] << 8) | pkt[21];
                        return true;
                    }
                }
                catch (SocketException) { /* this NIC is out of the running */ }

                return false;
            }

            private static bool IsFrom(byte[] pkt, byte[] routerMac)
            {
                for (int i = 0; i < 6; i++)
                    if (pkt[2 + i] != routerMac[i]) return false;
                return true;
            }

            public void Dispose()
            {
                try { Udp?.Dispose(); } catch (SocketException) { /* nothing left to do about it */ }
            }
        }

        private CandidateProbe? TryCreateProbe(LocalNic nic)
        {
            try
            {
                var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(nic.LocalIp ?? IPAddress.Any, 0));

                byte[] kb = new byte[2];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);

                return new CandidateProbe
                {
                    Udp        = udp,
                    LocalMac   = nic.Mac ?? GetLocalMac(null),
                    Broadcast  = new IPEndPoint(nic.Broadcast ?? IPAddress.Broadcast, 20561),
                    SessionKey = BitConverter.ToUInt16(kb, 0),
                    Trace      = Diagnostics.TikWireTrace.Enabled
                        ? new Action<string>(line => Diagnostics.TikWireTrace.Emit(
                            WireTraceChannel, Diagnostics.TikWireDir.Note, line))
                        : null,
                };
            }
            catch (SocketException)
            {
                return null;   // a NIC that cannot be bound is simply not a candidate
            }
        }

        /// <summary>
        /// Every NIC that could plausibly carry a MAC-layer session, in the order they will be tried:
        /// up, not loopback, not a tunnel, with a real MAC and an IPv4 address to bind to.
        /// </summary>
        /// <remarks>
        /// Physical adapters come first. On a developer machine the list is dominated by Hyper-V, WSL and
        /// VPN adapters, and trying those first spends the connect budget before reaching the NIC the
        /// router is actually on — each candidate costs a SESSIONSTART round.
        /// </remarks>
        private static LocalNic[] EnumerateCandidateNics()
        {
            var physical = new List<LocalNic>();
            var virt     = new List<LocalNic>();

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

                    IPAddress? bcast = null;
                    if (ua.IPv4Mask != null)
                    {
                        byte[] lb = ua.Address.GetAddressBytes();
                        byte[] mb = ua.IPv4Mask.GetAddressBytes();
                        byte[] b  = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(lb[i] | ~mb[i]);
                        bcast = new IPAddress(b);
                    }

                    var nic = new LocalNic { Mac = mac, LocalIp = ua.Address, Broadcast = bcast };
                    (IsProbablyVirtual(ni) ? virt : physical).Add(nic);
                    break;   // one address per NIC is enough to bind and broadcast from
                }
            }

            physical.AddRange(virt);
            return physical.ToArray();
        }

        // Best-effort only, and it must stay that way: it decides the ORDER candidates are tried in,
        // never whether one is tried at all. A router reached through a Hyper-V switch is a normal setup.
        private static bool IsProbablyVirtual(NetworkInterface ni)
        {
            string d = ni.Description ?? string.Empty;
            return d.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("hyper-v", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("vmware",  StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("vbox",    StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("tap",     StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("loopback", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Binds the socket to one candidate NIC and points the session's addressing at it: source MAC,
        /// bind address and subnet broadcast all come from that ONE interface. Called again per candidate
        /// while <see cref="StartSession"/> is still looking for the one the router answers on.
        /// </summary>
        private void BindToCandidate(LocalNic nic, string? host)
        {
            // Source MAC, local IPv4 and subnet broadcast must all come from the SAME NIC — see below for
            // why picking them independently is not enough.
            _localMac = nic.Mac ?? GetLocalMac(host);

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
            _udp?.Dispose();
            _udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(nic.LocalIp ?? IPAddress.Any, 0));

            // SESSIONSTART always goes to broadcast. Everything else goes to the router's own address when
            // we have one, and to broadcast when we do not.
            //
            // A known host IS that address, so it is used directly — measured on RouterOS 7.23.2, waiting
            // for the router to reveal it instead would wait forever: the mac-server answers from
            // 0.0.0.0:20561 even on a router that HAS an IP address, so LatchRouterUnicast has nothing to
            // latch onto and a host-addressed session would spend its whole life broadcasting the terminal
            // to every host on the subnet. (Docs/mactelnet-protocol.md describes the latch as the reference
            // client's behaviour; what the router actually sends is what is written here.)
            IPAddress broadcastAddr = nic.Broadcast
                ?? (string.IsNullOrEmpty(host) ? IPAddress.Broadcast : GetBroadcastAddress(host!));
            _routerEp        = new IPEndPoint(broadcastAddr, 20561);

            // A host name that is not an IP literal leaves it null: there is nothing to put in an
            // IPEndPoint, and broadcast is the correct fallback rather than a reason to fail the open.
            _routerUnicastEp = !string.IsNullOrEmpty(host) && IPAddress.TryParse(host, out var routerIp)
                ? new IPEndPoint(routerIp, 20561)
                : null;
            _outCounter = 0;
            _inCounter  = 0;

            // The session's whole identity in one line, so a trace of a full suite run can be asked which
            // sessions were alive at once and whether any two shared a key. The router distinguishes
            // concurrent sessions from one host by this 16-bit key alone, and it is drawn at random per
            // open — with hundreds of sessions in a suite run a birthday collision is not exotic (P2.55).
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                    "SESSION OPEN key=0x" + _sessionKey.ToString("x4")
                    + " local=" + _udp.Client.LocalEndPoint
                    + " srcMac=" + BitConverter.ToString(_localMac)
                    + " clientType=0x" + _clientType.ToString("x4"));
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
                // Traced because this is the inbound half of a stall and it is otherwise invisible: we drop
                // the packet, hold the ACK where it was, and depend entirely on the ROUTER resending the
                // missing bytes. If it does not, every read from here on times out with nothing to show for
                // it, and the trace looks merely quiet rather than blocked (P2.49).
                if (Diagnostics.TikWireTrace.Enabled)
                    Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                        "HOLE counter=" + counter + " expected=" + _inCounter
                        + " missing=" + (counter - _inCounter) + TraceTag);
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
        /// Records an ACK from the router. Its counter is the stream offset the router has consumed up to,
        /// so it both retires every queued packet that now falls below it and gives
        /// <see cref="RetransmitIfUnacked"/> the mark to compare the rest against.
        /// </summary>
        /// <remarks>
        /// Under <see cref="SendGate"/> because it mutates the same outbound state the send path owns. It is
        /// called from the receive side, which on a multiplexed channel is a different thread from the one
        /// sending.
        /// </remarks>
        protected void NoteAck(uint counter)
        {
            lock (SendGate)
            {
                if (!_haveAck || counter > _highestAck)
                {
                    _highestAck = counter;
                    _haveAck    = true;
                }

                // Retire what the router has taken. The ACK is cumulative and the queue is in send order,
                // so this can clear several at once. A plain loop rather than FindIndex: this runs once per
                // received DATA packet — thousands of times in a terminal session — and a lambda there is a
                // per-packet allocation for nothing.
                int retired = 0;
                while (retired < _unacked.Count && _unacked[retired].End <= counter)
                    retired++;
                if (retired > 0)
                {
                    _unacked.RemoveRange(0, retired);
                    _retransmits = 0;   // the budget belongs to the packet at the head, which just changed
                    _budgetNoted = false;
                }
            }
        }

        /// <summary>
        /// Resends the <em>oldest</em> unacknowledged DATA packet. Called from the read loops when nothing
        /// is arriving - that idle moment is precisely the "my command never landed" case. The resend is
        /// byte-identical (same counter), so a packet that did arrive is simply seen as a duplicate and
        /// re-ACKed.
        /// <para>
        /// Oldest rather than newest because the ACK is cumulative: the router cannot acknowledge past a
        /// gap, so the packet blocking the stream is always the one at the head of the queue. With a single
        /// request in flight the two are the same packet.
        /// </para>
        /// <para>
        /// Rate-limited internally rather than by the caller, because the read loops poll at very
        /// different cadences (500 ms in the terminal loops, 20 ms in <see cref="RecvUntil"/>) and a
        /// caller-paced resend would flood the router from the fast one.
        /// </para>
        /// <para>
        /// The head packet must also be <see cref="MinRetransmitIntervalMs"/> OLD, not merely followed by
        /// that much idle time. The rate limit alone does not say that: <c>_lastRetransmitUtc</c> starts at
        /// <see cref="DateTime.MinValue"/>, so the first call after a send always passed it, and the first
        /// ACK of a session — which arrives while the packet behind it is still in flight — fired a resend
        /// of a packet a few milliseconds old. Measured across five traced suite runs: <em>every</em>
        /// MAC-layer session spent one of its eight retransmissions that way, on a packet that was
        /// acknowledged moments later.
        /// </para>
        /// </summary>
        /// <returns><c>true</c> if a retransmission was sent.</returns>
        protected bool RetransmitIfUnacked()
        {
            Unacked head;
            lock (SendGate)
            {
                if (_unacked.Count == 0 || !_haveAck)
                    return false;
                if (_retransmits >= MaxRetransmits)
                {
                    NoteBudgetSpentOnce();
                    return false;
                }
                if ((DateTime.UtcNow - _lastRetransmitUtc).TotalMilliseconds < MinRetransmitIntervalMs)
                    return false;
                if ((DateTime.UtcNow - _unacked[0].SentUtc).TotalMilliseconds < MinRetransmitIntervalMs)
                    return false;

                head = _unacked[0];
                _lastRetransmitUtc = DateTime.UtcNow;
                _retransmits++;
                _udp.Send(head.Packet, head.Packet.Length, Dst);
            }

            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                    "RETRANSMIT #" + _retransmits + " end=" + head.End + " highestAck=" + _highestAck
                    + " queued=" + _unacked.Count + TraceTag);
            return true;
        }

        /// <summary>
        /// Session marker appended to every traced line. The wire-trace channel is per transport, not per
        /// connection, so a trace taken while several MAC sessions are alive interleaves them and cannot be
        /// asked what ONE session was doing — which is exactly the question a wedge poses.
        /// </summary>
        /// <remarks>
        /// It must be on <em>every</em> line, including the receive paths that do their own parsing. Leaving
        /// it off there does not merely lose detail, it produces confident nonsense: replaying a green suite
        /// run through a per-session reconstruction, the untagged inbound lines were attributed to whichever
        /// session opened last and yielded 311 stream holes that never happened.
        /// </remarks>
        protected string TraceTag => " key=" + _sessionKey.ToString("x4");

        /// <summary>
        /// Emits the one state this layer cannot recover from: the head packet has been resent to
        /// exhaustion and the router has still not taken it, so the byte stream is blocked for good and
        /// every caller from here on will fail on its own deadline, far from the cause. Once per packet —
        /// the read loops ask on every poll.
        /// </summary>
        private void NoteBudgetSpentOnce()
        {
            if (_budgetNoted || !Diagnostics.TikWireTrace.Enabled)
                return;
            _budgetNoted = true;
            Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                "RETRANSMIT BUDGET SPENT end=" + _unacked[0].End + " highestAck=" + _highestAck
                + " queued=" + _unacked.Count + TraceTag);
        }

        /// <summary>
        /// <c>true</c> once an unacknowledged DATA packet has been retransmitted to exhaustion without the
        /// router ever taking it. The MAC layer acknowledges the byte stream, so this is the one signal
        /// that says the router did <em>not</em> take our bytes — as opposed to taking them and being slow
        /// to answer. That distinction is what makes a retry safe: an unacknowledged command cannot have
        /// reached the console, so it cannot have half-executed.
        /// </summary>
        protected bool LastSendAbandoned
        {
            get
            {
                lock (SendGate)
                    return _unacked.Count > 0 && _haveAck && _retransmits >= MaxRetransmits;
            }
        }

        /// <summary>
        /// <c>true</c> while the head of the unacknowledged queue has already been retransmitted at least
        /// once without being taken — i.e. the stream has been blocked for at least
        /// <see cref="MinRetransmitIntervalMs"/>. The signal a caller needs to stop <em>adding</em> to a
        /// queue that cannot drain, and the earlier, softer sibling of <see cref="LastSendAbandoned"/>
        /// (which only becomes true once the whole budget is spent).
        /// </summary>
        /// <remarks>
        /// This layer has retransmission but no send window: nothing here stops a caller from queueing more
        /// DATA behind a packet the router is not acknowledging. Because the ACK is cumulative the router
        /// cannot take any of it either, so every packet added past the hole is bytes on the wire that
        /// provably cannot be processed — a WinBox-CLI-MAC terminal piled up ~2.4 KB in 24 packets that way
        /// before the retransmit budget ran out. One retransmission rather than none is deliberate:
        /// a packet still in normal flight is acknowledged in milliseconds, so an ordinary send must never
        /// trip this.
        /// </remarks>
        protected bool LastSendStalled
        {
            get
            {
                lock (SendGate)
                    return _unacked.Count > 0 && _haveAck && _retransmits > 0;
            }
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
            public byte[]? Mac;
            public IPAddress? LocalIp;
            public IPAddress? Broadcast;
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

            StartSession();

            byte[] psd = Encoding.UTF8.GetBytes(user)
                .Concat(new byte[] { 0 }).Concat(xWA).Concat(new byte[] { (byte)parityA })
                .ToArray();
            Send(PKT_DATA,
                BuildCtrl(CTRL_BEGINAUTH, new byte[0])
                .Concat(BuildCtrl(CTRL_PASSSALT, psd)).ToArray());

            byte[]? xWB = null; int parityB = 0; byte[]? salt = null;
            var seen = new HashSet<byte>();
            RecvUntilDuringLogin(10000, seen, (type, payload, counter) =>
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

            // salt is always assigned together with xWB in the CTRL_PASSSALT branch above, so it is
            // non-null here too even though the compiler cannot see that relationship.
            FinishAuth(user, pass, privA, xWA, xWB, parityB, salt!);
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

        /// <summary>
        /// The budget a subclass gives <see cref="StartSession"/>: its own connect timeout, floored at the
        /// old fixed 2 s so a caller who asked for a very short one does not get a worse session-start than
        /// before. Set by the login path before it starts the session.
        /// </summary>
        protected int SessionStartBudgetMs { get; set; } = DefaultSessionStartMs;

        private const int DefaultSessionStartMs = 2000;

        /// <summary>
        /// Opens the session on the router: sends SESSIONSTART and waits for the router to acknowledge it,
        /// resending on silence. Returns once acknowledged, or when the budget runs out — the caller
        /// proceeds either way, because authentication will fail on its own deadline anyway and a second
        /// error here would only hide the first.
        /// </summary>
        /// <param name="timeoutMs">
        /// How long to keep trying. Zero, the normal case, means <see cref="SessionStartBudgetMs"/> — the
        /// connection's own connect timeout.
        /// </param>
        /// <remarks>
        /// SESSIONSTART is the one packet this layer cannot resend through <see cref="RetransmitIfUnacked"/>:
        /// it is not DATA, so it never enters the unacknowledged queue — and that queue is inert until the
        /// first ACK arrives (<c>_haveAck</c>), which is the very thing SESSIONSTART is waiting for. It is
        /// also the one packet that is <em>always</em> sent to broadcast, whatever the session has since
        /// learned about where the router lives. Before this, a single lost SESSIONSTART cost the full
        /// authentication timeout with no retry at all; the previous code waited a blind 80 ms and sent the
        /// auth request into a session the router might never have created.
        /// <para>
        /// Measured on 7.23.2 across five traced suite runs: 76 of 76 SESSIONSTARTs were acknowledged, all
        /// of them before the old blind sleep was over — so on a healthy link this waits less, not more.
        /// </para>
        /// <para>
        /// It is also where a MAC-only session works out which local NIC to use: with nothing naming one,
        /// every candidate is asked at once and the one the router answers is adopted — see
        /// <see cref="ProbeCandidatesInParallel"/>.
        /// </para>
        /// </remarks>
        protected void StartSession(int timeoutMs = 0)
        {
            // Spend the CONNECT budget here rather than a fixed 2 s. What the fixed budget did, measured
            // inside a full suite run: RouterOS went quiet for about four seconds — it had just been given
            // three deliberately wrong logins by the preceding test, on top of a run's worth of accumulated
            // load — so SESSIONSTART went unacknowledged, and the code below proceeded to AUTHENTICATE into
            // a session the router had never created. That burned the 10 s auth deadline and then reported
            // a login failure, which is both slower and a worse description of what happened than simply
            // waiting a little longer for the session to start. The caller asked for a connect timeout;
            // this is what it is for.
            if (timeoutMs <= 0)
                timeoutMs = Math.Max(SessionStartBudgetMs, DefaultSessionStartMs);

            // A MAC-only session does not know which NIC the router is on. Every candidate is asked at
            // once and the one the router answers is adopted — see ProbeCandidatesInParallel for why this
            // is not a sequence of tries. On failure it falls through to the ordinary loop below, which
            // keeps the currently bound NIC: the probe may have been unlucky, and the loop's own retries
            // are still worth spending before authentication is attempted.
            if (IsMacOnly && _candidates.Length > 1 && ProbeCandidatesInParallel(timeoutMs))
                return;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var resendAt = DateTime.UtcNow;

            while (DateTime.UtcNow < deadline)
            {
                if (DateTime.UtcNow >= resendAt)
                {
                    Send(PKT_SESSIONSTART, null);
                    resendAt = DateTime.UtcNow.AddMilliseconds(SessionStartRetryMs);
                }

                bool acked = false;
                while (_udp.Available > 0 && !acked)
                    acked = ReceiveOne((type, payload, counter) =>
                    {
                        if (type != PKT_ACK) return false;
                        NoteAck(counter);
                        return true;
                    });
                if (acked)
                    return;

                Thread.Sleep(5);
            }

            // Still unacknowledged after the whole connect budget. The caller proceeds — authentication
            // fails on its own deadline and a second error here would only hide the first — but the trace
            // has to carry it, because from the exception alone a router that never opened the session is
            // indistinguishable from one that rejected the credentials.
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                    "SESSIONSTART UNACKED after " + timeoutMs + " ms"
                    + (IsMacOnly ? " (" + _candidates.Length + " NIC(s) probed, none answered)" : "") + TraceTag);
        }

        private const int SessionStartRetryMs = 300;

        // ── Send / Receive ───────────────────────────────────────────────────────

        /// <summary>
        /// Where everything except SESSIONSTART is sent: the router's own address once its first reply has
        /// told us what it is, and subnet broadcast until then — or forever, when the router has no IP.
        /// </summary>
        protected IPEndPoint Dst => _routerUnicastEp ?? _routerEp;

        /// <summary>
        /// Latches onto the address the router answers from, so the rest of the session is unicast rather
        /// than broadcast. Called for every received packet; only the first one that carries a usable
        /// address does anything.
        /// </summary>
        /// <remarks>
        /// <b>Measured on RouterOS 7.23.2, this never fires:</b> the mac-server answers from
        /// <c>0.0.0.0:20561</c> even on a router that has an IP address, so there is never an address to
        /// latch onto and a session that was not given a host broadcasts for its whole life — which is
        /// correct, and is what the reference implementation ends up doing too. It is kept because the
        /// protocol prescribes the latch and a router that does reveal its address costs nothing to
        /// honour; it is not a path to rely on. A host-addressed session does not depend on it at all —
        /// <c>BindToCandidate</c> seeds the address from the host directly.
        /// </remarks>
        private void LatchRouterUnicast(IPEndPoint from)
        {
            if (_routerUnicastEp != null) return;
            if (from == null || from.Address == null) return;
            if (from.Address.Equals(IPAddress.Any) || from.Address.Equals(IPAddress.Broadcast)) return;

            _routerUnicastEp = new IPEndPoint(from.Address, 20561);
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Note,
                    "UNICAST LATCH " + _routerUnicastEp + TraceTag);
        }


        protected void Send(byte type, byte[]? payload)
        {
            lock (SendGate)
                SendCore(type, payload);
        }

        private void SendCore(byte type, byte[]? payload)
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
            _udp.Send(pkt, pkt.Length, (type == PKT_SESSIONSTART) ? _routerEp : Dst);

            // The counter belongs in the note: it is the stream offset this packet claims, and the
            // router's ACK counter is what it must be compared against. Without it the trace cannot
            // answer "did the router acknowledge everything we sent" (P2.19).
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Send,
                    payload, 0, payload?.Length ?? 0,
                    "type=0x" + type.ToString("x2") + " counter=" + counter + TraceTag);

            if (type == PKT_DATA && payload != null && payload.Length > 0)
            {
                _outCounter += (uint)payload.Length;
                // Hold it for retransmission until the router's ACK reaches past it (P2.19). Appended, not
                // replaced, so a concurrent sender cannot drop someone else's still-unacked packet (P2.42).
                if (_unacked.Count == 0)
                    _retransmits = 0;   // fresh head; earlier entries keep the budget already spent on them
                if (_unacked.Count < MaxUnackedTracked)
                    _unacked.Add(new Unacked
                    {
                        Packet  = pkt,
                        End     = counter + (uint)payload.Length,
                        SentUtc = DateTime.UtcNow,
                    });
            }
        }

        protected void SendAck(uint ackCounter)
        {
            lock (SendGate)
                SendAckCore(ackCounter);
        }

        private void SendAckCore(uint ackCounter)
        {
            byte[] pkt = new byte[22];
            pkt[0] = 1; pkt[1] = PKT_ACK;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey >> 8);  pkt[15] = (byte)(_sessionKey & 0xFF);
            pkt[16] = (byte)(_clientType >> 8);  pkt[17] = (byte)(_clientType & 0xFF);
            pkt[18] = (byte)(ackCounter >> 24); pkt[19] = (byte)(ackCounter >> 16);
            pkt[20] = (byte)(ackCounter >> 8);  pkt[21] = (byte)(ackCounter & 0xFF);
            _udp.Send(pkt, pkt.Length, Dst);

            // Traced because a regressing ACK cannot be seen from the receive side alone - the router's
            // reaction (a retransmit burst) reads as router misbehaviour until you can see what we told
            // it (P2.19).
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Send,
                    "type=0x02 ack=" + ackCounter + TraceTag);
        }

        protected void SendPong(uint counter)
        {
            lock (SendGate)
                SendPongCore(counter);
        }

        private void SendPongCore(uint counter)
        {
            byte[] pkt = new byte[22];
            pkt[0] = 1; pkt[1] = PKT_PONG;
            Buffer.BlockCopy(_localMac,  0, pkt, 2, 6);
            Buffer.BlockCopy(_routerMac, 0, pkt, 8, 6);
            pkt[14] = (byte)(_sessionKey >> 8);  pkt[15] = (byte)(_sessionKey & 0xFF);
            pkt[16] = (byte)(_clientType >> 8);  pkt[17] = (byte)(_clientType & 0xFF);
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)(counter & 0xFF);
            _udp.Send(pkt, pkt.Length, Dst);
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
                    if (ReceiveOne(handler)) return;
                }
                else
                {
                    RetransmitIfUnacked();   // self-rate-limited; safe at this 20 ms cadence
                    // Sleeping rather than waiting on the socket costs up to 20 ms per received frame,
                    // which is a real drag on terminal traffic — but replacing it with
                    // Socket.Poll(20 ms, SelectRead) only recovered ~15% of a WinboxCliMac run, so the
                    // bulk of the cost is elsewhere. Measured while diagnosing P2.41; left alone
                    // deliberately, see P2.43.
                    Thread.Sleep(20);
                }
            }
            throw new TimeoutException("Timed out waiting for expected MAC-layer packet");
        }

        /// <summary>
        /// <see cref="RecvUntil"/> for a step of the LOGIN handshake: a timeout becomes a
        /// <see cref="TikConnectionLoginNoAnswerException"/> describing what the session had done, but only
        /// once the router has acknowledged the session — before that the silence is about reachability and
        /// the bare timeout is the truer answer.
        /// </summary>
        /// <remarks>
        /// "Timed out waiting for expected MAC-layer packet" cannot tell a router that never took our bytes
        /// from one that took them and said nothing, and those are different faults: the first is a lost
        /// datagram (which this layer resends up to <see cref="MaxRetransmits"/> times), the second is the
        /// router declining to finish a handshake it opened, which clears on a fresh attempt. Seen live: an
        /// otherwise green suite run failing once on <c>MacTransportsWillConnectWithoutAnyHostAddress</c>
        /// with nothing in the message to say which.
        /// </remarks>
        private void RecvUntilDuringLogin(int timeoutMs, HashSet<byte> seen,
            Func<byte, byte[], uint, bool> handler)
        {
            try
            {
                RecvUntil(timeoutMs, (type, payload, counter) =>
                {
                    seen.Add(type);
                    return handler(type, payload, counter);
                });
            }
            catch (TimeoutException ex)
            {
                bool sessionOpen;
                string state;
                lock (SendGate)
                {
                    sessionOpen = _haveAck;
                    state = "waited " + timeoutMs + " ms; session "
                          + (_haveAck ? "acknowledged up to " + _highestAck : "never acknowledged")
                          + "; " + _unacked.Count + " packet(s) still unacknowledged after "
                          + _retransmits + " resend(s); packet types seen: "
                          + (seen.Count == 0 ? "none" : string.Join(",", seen.Select(t => "0x" + t.ToString("X2"))));
                }
                if (!sessionOpen) throw;
                throw new TikConnectionLoginNoAnswerException("MAC-Telnet", state, ex);
            }
        }

        /// <summary>
        /// Processes every datagram <em>already</em> in the socket buffer and returns whether
        /// <paramref name="handler"/> was satisfied by one of them. Never waits and never sleeps: an empty
        /// socket is an immediate <c>false</c>, not a timeout.
        /// </summary>
        /// <remarks>
        /// The polling counterpart of <see cref="RecvUntil"/>, for a caller that wants to know whether
        /// anything useful has arrived rather than to wait for it. The distinction matters because most of
        /// what arrives on this socket is control traffic — ACK, PING and the router's retransmits — which
        /// <see cref="RecvUntil"/> handles and then keeps waiting on, correctly for a caller that is
        /// waiting, but at the cost of the caller's whole timeout for one that is polling.
        /// <para>Control handling is identical either way: the handler sees the same packets, so ACKs are
        /// noted, PINGs are ponged and duplicates re-ACKed regardless of which loop consumed them.</para>
        /// </remarks>
        protected bool RecvAvailable(Func<byte, byte[], uint, bool> handler)
        {
            while (_udp != null && _udp.Available > 0)
                if (ReceiveOne(handler)) return true;

            // An empty socket is exactly the "my command never landed" moment, and it means the same thing
            // whether the caller is waiting or polling — so the poll drives retransmission too. Without
            // this, moving a read loop from RecvUntil to RecvAvailable silently takes the only recovery
            // from a lost outbound datagram away from it: the caller then sits out its whole deadline and
            // reports "nothing was received", which is precisely the shape of a wedge that is not one.
            // Self-rate-limited (MinRetransmitIntervalMs), so a fast poll cannot flood the router.
            RetransmitIfUnacked();
            return false;
        }

        /// <summary>
        /// Receives one datagram (the caller has established there is one), parses it, traces it and hands
        /// it to <paramref name="handler"/>. Returns the handler's verdict; malformed packets and our own
        /// broadcast echo are <c>false</c>.
        /// </summary>
        private bool ReceiveOne(Func<byte, byte[], uint, bool> handler)
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            byte[] pkt = _udp.Receive(ref ep);
            var parsed = ParsePacket(pkt);
            if (parsed == null) return false;
            var (type, counter, payload, srcMac) = parsed.Value;
            if (srcMac.SequenceEqual(_localMac)) return false;  // skip own echo

            // Latch only on OUR router's own packets. The socket is bound for broadcast, so on a segment
            // with several MikroTiks it can see somebody else's session traffic; that must not become the
            // address we send the rest of this session to. The check gates the latch only — which packets
            // are ACCEPTED is left exactly as it was, since the handler below is what knows the session key.
            if (srcMac.SequenceEqual(_routerMac))
                LatchRouterUnicast(ep);

            // Counterpart of the Send emit above. Without it the trace shows only our half of
            // the conversation, so an exchange that goes wrong (a missing reply, a packet type
            // arriving where another was expected) is indistinguishable from one that never
            // happened — which is precisely what made the WinBox handshake failures of P2.41
            // undiagnosable.
            if (Diagnostics.TikWireTrace.Enabled)
                Diagnostics.TikWireTrace.Emit(WireTraceChannel, Diagnostics.TikWireDir.Recv,
                    payload, 0, payload?.Length ?? 0,
                    "type=0x" + type.ToString("x2") + " counter=" + counter
                    + " from=" + ep + " srcMac=" + BitConverter.ToString(srcMac) + TraceTag);

            // payload is a non-nullable tuple element of ParsePacket's result; nullable state analysis loses
            // that through the Nullable<ValueTuple> deconstruction above, but it is never actually null here.
            return handler(type, payload!, counter);
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

        private static byte[] GetLocalMac(string? host)
        {
            IPAddress? target = null;
            if (!string.IsNullOrEmpty(host))
                try { target = IPAddress.Parse(host!); } catch { }

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

        private byte[] GetRouterMacAddress(string? host)
        {
            // RouterMacOverride is a MAC string "AA:BB:CC:DD:EE:FF" — parse directly (no MNDP).
            if (!string.IsNullOrEmpty(RouterMacOverride))
            {
                // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
                if (TikRouterAddress.TryParseMac(RouterMacOverride!, out byte[]? parsed))
                    return parsed!;

                throw new InvalidOperationException(
                    $"Router MAC address '{RouterMacOverride}' is not a MAC address. " +
                    "The expected form is \"AA:BB:CC:DD:EE:FF\".");
            }

            // Without a MAC there has to be a host to look one up BY: MNDP is asked which of the routers
            // announcing themselves has that IP. A MAC-only session has no such question to ask, and
            // falling back to "any MikroTik on the segment" would be a different router, not a default.
            if (string.IsNullOrEmpty(host))
                throw new InvalidOperationException(
                    "A MAC-layer connection opened without a host address must be told which router to "
                    + "talk to: set RouterMac, or create the setup with TikRouterAddress.FromMac(\"AA:BB:CC:DD:EE:FF\").");

            // MNDP discovery via the public core helper (waits up to 5 s).
            byte[]? found = MndpHelper.FindMacByHost(host!);
            if (found != null) return found;

            throw new InvalidOperationException(
                $"Cannot determine MAC address for router {host}. " +
                "Set MacTelnetConnection.RouterMac = \"AA:BB:CC:DD:EE:FF\", " +
                "or verify that MNDP (UDP 5678) is enabled on the router.");
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            OnDisposing();
            _udp?.Dispose();
        }

        /// <summary>
        /// Called before the socket is disposed. Subclasses that run a background thread over the socket
        /// stop it here, so it cannot race a disposed <see cref="_udp"/>.
        /// </summary>
        protected virtual void OnDisposing() { }
#pragma warning restore CS1591
    }
}
