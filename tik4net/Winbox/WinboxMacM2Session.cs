using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Crypto;
using tik4net.MacTelnet;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.Winbox
{
    /// <summary>
    /// WinBox M2 channel over the MAC layer (UDP 20561, <c>client_type=0x0f90</c>). Reuses the proven
    /// <see cref="MacLayerTransport"/> (framing, ACK/PING handling, EC-SRP5 authentication and AES/HMAC
    /// stream-key derivation) and carries each M2 message as a single AES-128-CBC blob inside a
    /// <c>PKT_DATA</c> payload — no TCP-style chunk wrapper.
    /// </summary>
    /// <remarks>
    /// Shared between the CLI/terminal MAC mode (<c>WinboxCliMac</c>) and a future native-M2 MAC mode.
    /// Implements <see cref="IWinboxM2Channel"/> so it drops straight into the shared
    /// <c>WinboxCliClient</c> in place of the TCP <see cref="WinboxM2Session"/>.
    /// </remarks>
    internal sealed class WinboxMacM2Session : MacLayerTransport, IWinboxM2Channel
    {
        private const ushort ClientType = 0x0f90;  // WinBox-over-MAC

        /// <inheritdoc/>
        protected override string WireTraceChannel => "wbxmac.udp";

        private int _reqId;
        // Buffer of received DATA payload bytes not yet consumed into a complete chunked frame.
        private readonly List<byte> _rxBuf = new List<byte>();

        internal WinboxMacM2Session(string routerMac)
        {
            RouterMacOverride = routerMac;
        }

        // ── IWinboxM2Channel ──────────────────────────────────────────────────

        public bool IsEncrypted => _sendAesKey != null;

        /// <summary>
        /// True when a complete M2 frame is ready to be taken by <see cref="Receive"/> — <b>not</b> merely
        /// when a datagram has arrived. Answering it drains whatever the socket already holds: ACKs are
        /// noted, PINGs ponged, duplicates re-ACKed and real payload buffered for reassembly.
        /// </summary>
        /// <remarks>
        /// The raw <c>_udp.Available &gt; 0</c> this used to be was a false positive on most polls, because
        /// the great majority of packets on this socket are control traffic. That mattered far more than it
        /// looks: <c>WinboxCliClient</c> uses this property to gate a <em>blocking</em> read, so every
        /// "available" that turned out to be an ACK or a router retransmit cost that read's whole frame
        /// timeout — <b>5 s, measured, per command and again per connection open</b> (P2.43). The property
        /// has to be honest precisely because its consumer treats it as permission to block.
        /// <para>Doing I/O in a getter is deliberate and safe here: this is the channel's poll operation,
        /// and only the single-threaded terminal loops in <c>WinboxCliClient</c> ever read it. The native
        /// transport runs a reader loop instead (<see cref="ReceiveNextFrame"/>) and never polls — which is
        /// what <see cref="SupportsStaleDrain"/> being <c>false</c> already guarantees.</para>
        /// </remarks>
        public bool DataAvailable
        {
            get
            {
                if (_udp == null) return false;
                if (_pendingFrame != null) return true;

                // Consume everything already buffered — the handler never stops the drain early, so one
                // poll cannot leave a second frame behind to be found only on the next one.
                RecvAvailable((type, payload, counter) =>
                {
                    if (type == PKT_ACK)  { NoteAck(counter); return false; }
                    if (type == PKT_PING) { SendPong(counter); return false; }
                    if (type != PKT_DATA) return false;
                    if (!AckData(counter, payload.Length)) return false;  // duplicate retransmit
                    if (IsControlPacket(payload)) return false;
                    _rxBuf.AddRange(payload);
                    return false;
                });

                _pendingFrame = TryExtractFrame();
                return _pendingFrame != null;
            }
        }

        // Set by DataAvailable when its drain completes a frame, handed straight out by the next RecvFrame.
        // Without it the poll would have to either discard the frame it just assembled or leave it in a
        // state the blocking path cannot tell from a partial one.
        private byte[] _pendingFrame;

        // MAC/UDP: a stale-frame drain loop would thrash on control noise instead of discarding one stale
        // DATA frame. Disable it here — the request-id correlation guard in WinboxNativeM2Operations still
        // covers stray frames.
        public bool SupportsStaleDrain => false;

        /// <summary>
        /// Connects over the MAC layer and authenticates (EC-SRP5). <paramref name="port"/> is ignored
        /// (MAC always uses UDP 20561), as is <paramref name="ioTimeoutMs"/> — UDP has no socket-level
        /// receive timeout here, every read carries its own explicit deadline.
        /// <paramref name="connectTimeoutMs"/> bounds each wait for a handshake frame.
        /// </summary>
        public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs)
        {
            BaseConnect(host, ClientType);
            // MAC-WinBox carries the SAME WinBox EC-SRP5 handshake as TCP (length-prefixed [len][0x06]
            // frames), tunnelled inside MAC-layer DATA packets — NOT the MAC-Telnet control-packet auth.
            MacAuthEcSrp5(user, password, connectTimeoutMs);
        }

        // ── WinBox EC-SRP5 handshake over the MAC layer ───────────────────────

        private void MacAuthEcSrp5(string user, string pass, int timeoutMs)
        {
            Send(PKT_SESSIONSTART, null);
            Thread.Sleep(80);

            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);
            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);

            byte[] payload = Encoding.UTF8.GetBytes(user)
                .Concat(new byte[] { 0 }).Concat(xWA).Concat(new byte[] { (byte)parityA }).ToArray();
            SendHandshakeFrame(payload);

            // Challenge frame: [len=49][0x06][32B xWB][1B parityB][16B salt] — same as WinBox TCP.
            byte[] challenge = RecvHandshakeFrame(timeoutMs);
            if (challenge == null || challenge.Length != 49)
                throw new InvalidOperationException(
                    $"MAC-WinBox: bad challenge ({(challenge == null ? "none" : challenge.Length + "B")})");

            byte[] xWB   = challenge.Take(32).ToArray();
            int parityB  = challenge[32];
            byte[] salt  = challenge.Skip(33).Take(16).ToArray();

            byte[] valPriv  = EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v   = EcSrp5.Redp1(xGamma, 1);
            ECPoint wB  = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum = EcSrp5.ECAdd(wB, v);

            byte[] j   = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
            var vh     = (EcSrp5.BEToBI(valPriv) * EcSrp5.BEToBI(j) % EcSrp5.R + EcSrp5.BEToBI(privA)) % EcSrp5.R;
            ECPoint zPt = EcSrp5.ECScalarMul(vh, sum);
            var (zMont, _) = EcSrp5.ToMontgomery(zPt);
            byte[] secret = EcSrp5.Sha256(zMont);

            byte[] clientCc = EcSrp5.Sha256(j.Concat(zMont).ToArray());
            SendHandshakeFrame(clientCc);

            // Establish what actually arrived before reading anything into it. Folding "no frame" into
            // the digest comparison made a lost or late UDP reply indistinguishable from a rejected
            // password — and over the MAC layer a lost reply is by far the likelier of the two (P2.41).
            byte[] serverCc = RecvHandshakeFrame(timeoutMs);
            if (serverCc == null)
                throw new InvalidOperationException(
                    $"MAC-WinBox: no server confirmation within {timeoutMs} ms. The handshake did not " +
                    "complete; this says nothing about the credentials.");
            // Same reply shapes as TCP — the MAC layer carries the identical WinBox handshake, refusal
            // text included.
            WinboxHandshakeReply.ThrowIfRouterMessage(serverCc, 32);

            byte[] expectedCc = EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray());
            if (!serverCc.SequenceEqual(expectedCc))
                throw new UnauthorizedAccessException("Wrong username or password");

            WinboxStreamCrypto.DeriveStreamKeys(false, secret,
                out _sendAesKey, out _receiveAesKey, out _sendHmacKey, out _receiveHmacKey);
        }

        private void SendHandshakeFrame(byte[] payload)
            => Send(PKT_DATA, ChunkWrap(payload, 0x06));

        // Reads one chunked handshake frame ([len][0x06]…) reassembled from MAC DATA packets.
        private byte[] RecvHandshakeFrame(int timeoutMs) => RecvFrame(timeoutMs);

        // Receives the next DATA packet payload (acking, ponging; skipping control packets), or null on timeout.
        private byte[] RecvDataPayload(int timeoutMs)
        {
            byte[] result = null;
            try
            {
                RecvUntil(timeoutMs, (type, payload, counter) =>
                {
                    if (type == PKT_ACK) { NoteAck(counter); return false; }
                    if (type == PKT_PING) { SendPong(counter); return false; }
                    if (type != PKT_DATA) return false;
                    if (!AckData(counter, payload.Length)) return false;  // duplicate retransmit
                    if (IsControlPacket(payload)) return false;
                    result = payload;
                    return true;
                });
            }
            catch (TimeoutException) { return null; }
            return result;
        }

        public byte[] NextReqIdField()
            => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)Interlocked.Increment(ref _reqId));

        /// <inheritdoc/>
        /// <remarks>
        /// <c>true</c> since P2.42. It was <c>false</c> because <see cref="MacLayerTransport"/> sends
        /// ACK/PONG from inside its receive path, so a background reader would have the transport writing
        /// from two threads (design §4.5). Both halves of that are now covered: every write already went
        /// through <c>SendGate</c>, and the outbound retransmit state — which really did assume one packet
        /// in flight — became a queue in the same change, because the MAC counter is a cumulative byte
        /// offset and a lost first packet of two is exactly what a single-slot buffer cannot resend.
        /// </remarks>
        public bool SupportsReaderLoop => true;

        // How long one ReceiveNextFrame poll waits before looping. The method itself carries no deadline —
        // per-request deadlines belong to the multiplexer's registrations — but the MAC layer has no
        // blocking primitive, and the slice has to end periodically so RetransmitIfUnacked keeps running
        // while the channel is idle. Long enough not to spin, short enough that disposal is noticed promptly.
        private const int ReaderPollSliceMs = 500;

        // Set on the disposal path so the reader loop stops asking rather than waiting for the socket to
        // throw underneath it.
        private volatile bool _closed;

        private bool _readerLoopHandover;

        /// <inheritdoc/>
        public byte[] ReceiveNextFrame()
        {
            // One-time handover from the lockstep path (the MAC counterpart of the TCP channel's one-time
            // switch to an infinite socket timeout). Anything still in the reassembly buffer was left by an
            // init exchange that has already completed, and the multiplexer restarts request ids from 1 — so
            // a leftover reply echoing, say, id 3 would be handed to a *new* request that later gets id 3.
            // Only this thread ever fills the buffer, and it has not read anything yet, so nothing live can
            // be discarded here. The connection's own DrainBufferedFrames cannot do this job: it discards
            // whole frames, and what has to go is the partial reassembly state underneath them
            // (SupportsStaleDrain is false for that reason).
            if (!_readerLoopHandover)
            {
                _rxBuf.Clear();
                _pendingFrame = null;
                _readerLoopHandover = true;
            }

            while (!_closed)
            {
                byte[] frame;
                try
                {
                    frame = RecvFrame(ReaderPollSliceMs);
                }
                catch (ObjectDisposedException) { return null; }   // socket closed under us — normal shutdown
                catch (System.Net.Sockets.SocketException) { return null; }

                // A slice that expires is not an error and must not surface as one: partial chunks stay in
                // the receive buffer and the next slice resumes reassembly where this one stopped.
                if (frame == null) continue;

                try { return WinboxStreamCrypto.Decrypt(frame, _receiveAesKey); }
                catch { /* not a clean M2 frame — drop it and keep reading, as Receive does */ }
            }
            return null;
        }

        // WinBox over MAC uses the SAME chunked framing as TCP ([chunkLen][tag][data]…), carried inside
        // MAC DATA packets — NOT a bare encrypted blob. The encrypted frame is chunk-wrapped on send and
        // reassembled on receive before AES decryption.
        public void Send(byte[] m2)
            => Send(PKT_DATA, ChunkWrap(WinboxStreamCrypto.Encrypt(m2, _sendAesKey, _sendHmacKey), 0x06));

        public byte[] SendReceive(byte[] m2, int timeoutMs)
        {
            Send(m2);
            return Receive(timeoutMs);
        }

        public byte[] Receive(int timeoutMs)
        {
            byte[] frame = RecvFrame(timeoutMs);
            if (frame == null) return null;
            try { return WinboxStreamCrypto.Decrypt(frame, _receiveAesKey); }
            catch { return null; }   // not a clean M2 frame
        }

        // ── Chunk framing (same wire format as WinboxTcpTransport, carried in DATA payloads) ──

        // Reassembles one complete chunked frame ([chunkLen][tag][data], chunkLen=0xFF = continuation),
        // reading further DATA packets as needed. Returns the concatenated chunk data, or null on timeout.
        private byte[] RecvFrame(int timeoutMs)
        {
            if (_pendingFrame != null)
            {
                byte[] ready = _pendingFrame;
                _pendingFrame = null;
                return ready;
            }

            byte[] frame = TryExtractFrame();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (frame == null)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) return null;
                byte[] data = RecvDataPayload(remaining);
                if (data == null) return null;
                _rxBuf.AddRange(data);
                frame = TryExtractFrame();
            }
            return frame;
        }

        // Tries to pull one complete frame out of _rxBuf. Returns null (and leaves the buffer intact) if
        // the buffer does not yet hold a full frame.
        private byte[] TryExtractFrame()
        {
            int pos = 0;
            var frame = new List<byte>();
            while (true)
            {
                if (_rxBuf.Count - pos < 2) return null;            // need chunk header
                int chunkLen = _rxBuf[pos];
                int payloadLen = (chunkLen == 0xFF) ? 0xFF : chunkLen;
                if (_rxBuf.Count - pos - 2 < payloadLen) return null;  // incomplete chunk
                for (int i = 0; i < payloadLen; i++) frame.Add(_rxBuf[pos + 2 + i]);
                pos += 2 + payloadLen;
                if (chunkLen < 0xFF) break;                          // final chunk
            }
            _rxBuf.RemoveRange(0, pos);
            return frame.ToArray();
        }

        private static byte[] ChunkWrap(byte[] data, byte firstTag)
        {
            var outBuf = new List<byte>(data.Length + 4);
            byte tag = firstTag;
            int pos = 0;
            while (true)
            {
                int rem = data.Length - pos;
                if (rem >= 0xFF)
                {
                    outBuf.Add(0xFF); outBuf.Add(tag);
                    outBuf.AddRange(data.Skip(pos).Take(0xFF));
                    pos += 0xFF;
                    tag = 0xFF;
                }
                else
                {
                    outBuf.Add((byte)rem); outBuf.Add(tag);
                    outBuf.AddRange(data.Skip(pos).Take(rem));
                    break;
                }
            }
            return outBuf.ToArray();
        }

        /// <summary>
        /// Tells the router the session is over before the socket goes away, by sending the MAC-layer
        /// <c>PKT_END</c> — the same courtesy <c>MacTelnetUdpClient.TryCloseSession</c> pays.
        /// </summary>
        /// <remarks>
        /// Closing a UDP socket signals nothing: there is no FIN, so a router that is not told keeps the
        /// login open until its own timeout. Measured on 7.23.2 (P2.35): six WinBox-native-over-MAC
        /// connections opened and disposed left six <c>winbox</c> rows in <c>/user/active</c> that were
        /// still there 15 s later and only expired after roughly a minute and a half — while the TCP
        /// sibling left none, because there the FIN does the telling. It mattered most while
        /// <c>WinboxNativeMac</c> was excluded from test-connection reuse — one connection per test meant a
        /// run held a rolling ~90 s worth of dead sessions on the router — and still matters for any caller
        /// that opens short-lived connections. The exclusion itself was retired in P2.42.
        /// <para>Best-effort by design: this runs on the disposal path, where a channel that never
        /// finished connecting has no MACs or socket to send with, and a router that has already dropped
        /// the session has nothing to hear it.</para>
        /// </remarks>
        protected override void OnDisposing()
        {
            _closed = true;   // stops the reader loop before the socket it polls is disposed
            try { Send(PKT_END, null); } catch { /* ignore — see remarks */ }
        }
    }
}
