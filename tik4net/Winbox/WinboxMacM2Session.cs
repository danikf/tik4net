using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Connection;
using tik4net.Crypto;
using tik4net.MacTelnet;

namespace tik4net.Winbox
{
    /// <summary>
    /// WinBox M2 channel over the MAC layer (UDP 20561, <c>client_type=0x0f90</c>). Uses
    /// <see cref="MacLayerTransport"/> for the MAC-layer packets, ACK/PING handling and retransmission, runs the
    /// WinBox EC-SRP5 handshake over it, and carries each encrypted M2 message in the same chunked framing as the
    /// TCP carrier, inside <c>PKT_DATA</c> payloads.
    /// </summary>
    /// <remarks>
    /// Shared between the CLI/terminal MAC mode (<c>WinboxCliMac</c>) and the native MAC mode
    /// (<c>WinboxNativeMac</c>). Implements <see cref="IWinboxM2Channel"/> so it drops straight into the shared
    /// <c>WinboxCliClient</c> and <c>WinboxNativeM2Operations</c> in place of the TCP <see cref="WinboxM2Session"/>.
    /// </remarks>
    internal sealed class WinboxMacM2Session : MacLayerTransport, IWinboxM2Channel
    {
        private const ushort ClientType = 0x0f90;  // WinBox-over-MAC

        /// <inheritdoc/>
        protected override string WireTraceChannel => "wbxmac.udp";

        private int _reqId;
        // Buffer of received DATA payload bytes not yet consumed into a complete chunked frame.
        private readonly List<byte> _rxBuf = new List<byte>();

        /// <summary>
        /// Serialises everything that <em>receives</em>: the reassembly buffer, the pending frame, and the
        /// ACK/PONG bookkeeping that receiving performs. Until the idle pump (<see cref="StartIdleServicing"/>)
        /// there was only ever one reader and none of it needed guarding.
        /// </summary>
        private readonly object _rxGate = new object();

        internal WinboxMacM2Session(string? routerMac)
        {
            RouterMacOverride = routerMac;
        }

        // ── IWinboxM2Channel ──────────────────────────────────────────────────

        public bool IsEncrypted => _keys != null;

        // Set by the handshake; null until it completes.
        private WinboxStreamKeys? _keys;

        private WinboxStreamKeys Keys
            => _keys ?? throw new InvalidOperationException("The MAC-WinBox session has not completed its handshake.");

        /// <summary>
        /// True when a complete M2 frame is ready to be taken by <see cref="Receive"/> — <b>not</b> merely
        /// when a datagram has arrived. Answering it drains whatever the socket already holds: ACKs are
        /// noted, PINGs ponged, duplicates re-ACKed and real payload buffered for reassembly.
        /// </summary>
        /// <remarks>
        /// A raw <c>_udp.Available &gt; 0</c> would be a false positive on most polls, because the great
        /// majority of packets on this socket are control traffic. That matters far more than it looks:
        /// <c>WinboxCliClient</c> uses this property to gate a <em>blocking</em> read, so every "available"
        /// that turns out to be an ACK or a router retransmit costs that read's whole frame timeout —
        /// <b>5 s, measured, per command and again per connection open</b>. The property has to be honest
        /// precisely because its consumer treats it as permission to block.
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
                lock (_rxGate)
                {
                    if (_pendingFrame != null) return true;
                    DrainSocket();
                    _pendingFrame = TryExtractFrame();
                    return _pendingFrame != null;
                }
            }
        }

        /// <summary>
        /// Consumes everything the socket already holds: ACKs are noted, PINGs ponged, duplicates re-ACKed
        /// and real payload buffered for reassembly. The handler never stops the drain early, so one pass
        /// cannot leave a second frame behind to be found only on the next one.
        /// </summary>
        /// <remarks>Caller holds <see cref="_rxGate"/>.</remarks>
        private void DrainSocket()
        {
            RecvAvailable((type, payload, counter) =>
            {
                if (type == PKT_END) { _routerEnded = true; return false; }
                if (!TryAcceptData(type, payload, counter)) return false;
                if (IsControlPacket(payload)) return false;
                _rxBuf.AddRange(payload);
                System.Threading.Interlocked.Add(ref _bytesReceived, payload.Length);
                return false;
            });
        }

        private long _bytesReceived;

        /// <inheritdoc/>
        /// <remarks>
        /// Counts the DATA payload this channel accepted for reassembly — not every byte the socket saw.
        /// ACKs, PINGs and duplicate retransmits are deliberately excluded: they are the carrier keeping
        /// itself alive and would make a silent router look like one that is answering slowly, which is the
        /// exact distinction the counter exists to draw.
        /// </remarks>
        public long BytesReceived => System.Threading.Interlocked.Read(ref _bytesReceived);

        // Set by DataAvailable when its drain completes a frame, handed straight out by the next RecvFrame.
        // Without it the poll would have to either discard the frame it just assembled or leave it in a
        // state the blocking path cannot tell from a partial one.
        private byte[]? _pendingFrame;

        // MAC/UDP: a stale-frame drain loop would thrash on control noise instead of discarding one stale
        // DATA frame. Disable it here — the request-id correlation guard in WinboxNativeM2Operations still
        // covers stray frames.
        public bool SupportsStaleDrain => false;

        // The MAC layer acknowledges the byte stream, so it is the one carrier here that can tell a dropped
        // session from a slow one. See MacLayerTransport.LastSendAbandoned for why an unacknowledged command
        // is also known not to have run.
        public bool SendAbandoned => LastSendAbandoned;

        // Same bookkeeping, read earlier: the stream is blocked but the head may still land. Nothing below
        // this layer throttles the sender, so it is the caller that has to stop adding.
        public bool SendStalled => LastSendStalled;

        /// <summary>
        /// Connects over the MAC layer and authenticates (EC-SRP5). <paramref name="port"/> is ignored
        /// (MAC always uses UDP 20561), as is <paramref name="ioTimeoutMs"/> — UDP has no socket-level
        /// receive timeout here, every read carries its own explicit deadline.
        /// <paramref name="connectTimeoutMs"/> bounds each wait for a handshake frame.
        /// </summary>
        /// <remarks>
        /// <paramref name="sendTimeoutMs"/> is accepted and ignored, and that is the honest answer rather
        /// than an omission: this carrier writes UDP datagrams, which are handed to the network stack and
        /// do not block waiting for the far end. There is nothing here for a send deadline to bound.
        /// </remarks>
        public void Open(string host, int port, string user, string password, int connectTimeoutMs,
            int ioTimeoutMs, int sendTimeoutMs = 0)
        {
            // Opening the session is part of opening the connection, so it gets the connect budget; with no connect
            // budget of its own (<= 0), the receive timeout (TikTimeouts.HandshakeBoundMs).
            int handshakeMs = TikTimeouts.HandshakeBoundMs(connectTimeoutMs, ioTimeoutMs);
            SessionStartBudgetMs = handshakeMs;
            BaseConnect(host, ClientType);
            // MAC-WinBox carries the SAME WinBox EC-SRP5 handshake as TCP (length-prefixed [len][0x06]
            // frames), tunnelled inside MAC-layer DATA packets — NOT the MAC-Telnet control-packet auth.
            MacAuthEcSrp5(user, password, handshakeMs);
        }

        // ── WinBox EC-SRP5 handshake over the MAC layer ───────────────────────

        private void MacAuthEcSrp5(string user, string pass, int timeoutMs)
        {
            StartSession();

            var client = new EcSrp5Client(user, EcSrp5PrivateKeyOverride);
            SendHandshakeFrame(client.Hello);

            // Challenge frame: [len=49][0x06][32B xWB][1B parityB][16B salt] — same as WinBox TCP.
            byte[]? challenge = RecvFrame(timeoutMs);
            if (challenge == null || challenge.Length != EcSrp5Client.ChallengeLength)
                throw new InvalidOperationException(
                    $"MAC-WinBox: bad challenge ({(challenge == null ? "none" : challenge.Length + "B")})");

            // RouterOS hashes only the account name into the validator: login options after '+' (WinBox's "user+r" to a
            // RoMON agent) travel in the name sent above but not here (Docs/findings-romon.md, "Agent login").
            EcSrp5Proof proof = client.Answer(challenge, pass, EcSrp5.AccountName(user));
            SendHandshakeFrame(proof.ClientConfirmation);

            // Establish what actually arrived before reading anything into it. Folding "no frame" into
            // the digest comparison made a lost or late UDP reply indistinguishable from a rejected
            // password — and over the MAC layer a lost reply is by far the likelier of the two.
            byte[]? serverCc = RecvFrame(timeoutMs);
            if (serverCc == null)
                throw new InvalidOperationException(
                    $"MAC-WinBox: no server confirmation within {timeoutMs} ms. The handshake did not " +
                    "complete; this says nothing about the credentials.");
            // Same reply shapes as TCP — the MAC layer carries the identical WinBox handshake, refusal
            // text included.
            proof.Verify(serverCc, 32);

            _keys = WinboxStreamKeys.Derive(proof.Secret);
        }

        private void SendHandshakeFrame(byte[] payload)
            => Send(PKT_DATA, WinboxChunkFraming.Chunk(payload, WinboxChunkFraming.EncryptedTag));

        // Receives the next DATA packet payload (acking, ponging; skipping control packets), or null on timeout. The
        // router ending the session (PKT_END with our key) throws: no frame follows it.
        private byte[]? RecvDataPayload(int timeoutMs)
        {
            ThrowIfRouterEnded();
            byte[]? result = null;
            try
            {
                RecvUntil(timeoutMs, (type, payload, counter) =>
                {
                    if (type == PKT_END) { _routerEnded = true; return true; }
                    if (!TryAcceptData(type, payload, counter)) return false;
                    if (IsControlPacket(payload)) return false;
                    result = payload;
                    return true;
                });
            }
            catch (TimeoutException) { return null; }
            ThrowIfRouterEnded();
            return result;
        }

        // Set when the router sends PKT_END for this session: it has ended it, and nothing sent into it is answered.
        // The carrier used not to look at PKT_END at all, so a read waited out its whole deadline on a session that
        // was already gone. Reported as TCP reports the router's FIN, an IOException: the stream is broken, and whether
        // a command in flight ran is not known (so not TikConnectionSessionClosedException, which says it did not).
        private volatile bool _routerEnded;

        private void ThrowIfRouterEnded()
        {
            if (_routerEnded)
                throw new System.IO.IOException(
                    "WinBox over MAC: the router ended this session (PKT_END). Open a new connection to continue.");
        }

        public byte[] NextReqIdField()
            => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)Interlocked.Increment(ref _reqId));

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
        /// <remarks>
        /// A reader loop over this carrier rests on two things. <see cref="MacLayerTransport"/> sends ACK/PONG from
        /// inside its receive path, so a background reader has the transport writing from two threads
        /// (design §4.5): every write goes through <c>SendGate</c>, which makes that safe. And the outbound
        /// retransmit state is a QUEUE rather than a single slot — the MAC counter is a cumulative byte
        /// offset, and a lost first packet of two is exactly what a single-slot buffer cannot resend.
        /// </remarks>
        public byte[]? ReceiveNextFrame()
        {
            // One-time handover from the open phase (the MAC counterpart of the TCP channel's one-time
            // switch to an infinite socket timeout). Anything still in the reassembly buffer was left by an
            // init exchange that has already completed, and the multiplexer restarts request ids from 1 — so
            // a leftover reply echoing, say, id 3 would be handed to a *new* request that later gets id 3.
            // Only this thread ever fills the buffer, and it has not read anything yet, so nothing live can
            // be discarded here. The connection's own DrainBufferedFrames cannot do this job: it discards
            // whole frames, and what has to go is the partial reassembly state underneath them
            // (SupportsStaleDrain is false for that reason).
            if (!_readerLoopHandover)
            {
                lock (_rxGate)
                {
                    _rxBuf.Clear();
                    _pendingFrame = null;
                }
                _readerLoopHandover = true;
            }

            while (!_closed)
            {
                byte[]? frame;
                try
                {
                    frame = RecvFrame(ReaderPollSliceMs);
                }
                // Null is the interface's "channel closed".
                catch (ObjectDisposedException) { return null; }   // socket closed under us — normal shutdown
                catch (System.Net.Sockets.SocketException) { return null; }
                catch (System.IO.IOException) { return null; }   // the router ended the session (PKT_END)

                // A slice that expires is not an error and must not surface as one: partial chunks stay in
                // the receive buffer and the next slice resumes reassembly where this one stopped.
                if (frame == null) continue;

                // Not a clean M2 frame — drop it and keep reading, as Receive does. Traced because a
                // dropped frame is invisible by construction: the caller sees a read that produced
                // nothing, which is also what a silent router looks like, and this is the layer
                // the MAC-side wedge lives on.
                //
                // BOTH failure shapes land here. Decrypt answers null for a wrong key, a bad pad or a
                // truncated frame and throws only for a cryptographic fault, and null is this method's word
                // for "the channel closed" — so returning it reported a corrupt frame to every waiter as an
                // orderly shutdown (X-2). Frames are independently keyed here (each carries its own IV), so
                // unlike the TCP session one bad frame does not desynchronize what follows it.
                byte[]? plain = DecryptOrDrop(frame);
                if (plain != null) return plain;
            }
            return null;
        }

        // ── Idle servicing ────────────────────────────────────────────────────

        // How often the pump looks at the socket while nothing else is. The router retransmits an
        // unacknowledged packet roughly every second, so this is comfortably inside its patience while
        // costing one non-blocking syscall per tick.
        private const int IdlePumpIntervalMs = 200;

        private readonly TikBackgroundLoop _idlePump = new TikBackgroundLoop();

        /// <inheritdoc/>
        /// <remarks>
        /// The terminal consumer (<c>WinboxCliClient</c>) touches this channel only while a command is in
        /// flight. Between two commands nobody receives, so anything RouterOS writes to the console on its
        /// own initiative goes unacknowledged, the router retransmits it, and when it runs out of patience it
        /// stops serving the session altogether — silently, with nothing in <c>/log</c>.
        /// <para>Measured in a suite run: the only two idle stretches on a live session in a whole
        /// 340-test run, 19.7 s and 56.1 s, were the only two sessions that died. The mechanism was already
        /// written down for the sibling transport — see the class remarks on <c>MacTelnetUdpClient</c> —
        /// where the pump has existed from the start for exactly this reason.</para>
        /// <para>What it does is narrow on purpose: it acknowledges what arrives and answers PINGs, and it
        /// never sends anything unprompted. It is not a keepalive and cannot become one — that was measured
        /// four different ways on a MAC console and every one of them shortened the session's life. Nor does
        /// it extend the router's ~30 s idle window; past that the session is gone, and the reopen-and-retry
        /// path handles it.</para>
        /// </remarks>
        public void StartIdleServicing()
        {
            if (_idlePump.IsRunning) return;
            _idlePump.Start("tik4net WinBox-MAC idle ACK pump", IdlePumpLoop);
        }

        private void IdlePumpLoop(Func<bool> stopping)
        {
            while (!stopping() && !_closed)
            {
                Thread.Sleep(IdlePumpIntervalMs);
                if (stopping() || _closed) return;

                // TryEnter, never a blocking wait: a reader holding the gate is already servicing the socket,
                // and queueing behind it would only park this thread inside a 30 s command read.
                if (!Monitor.TryEnter(_rxGate)) continue;
                try
                {
                    if (_udp != null && !_closed) DrainSocket();
                }
                catch (ObjectDisposedException) { return; }   // socket closed under us — normal shutdown
                catch (System.Net.Sockets.SocketException) { return; }
                catch (Exception ex)
                {
                    // A malformed packet must not take the pump down with it — but a pump that keeps
                    // swallowing the same failure looks exactly like an idle router.
                    if (tik4net.Diagnostics.TikWireTrace.Enabled)
                        tik4net.Diagnostics.TikWireTrace.Emit("wbxclimac.session", tik4net.Diagnostics.TikWireDir.Note,
                            $"idle pump swallowed {ex.GetType().Name}: {ex.Message}");
                }
                finally { Monitor.Exit(_rxGate); }
            }
        }

        private void StopIdlePump() => _idlePump.Stop(IdlePumpIntervalMs * 5);

        // WinBox over MAC uses the SAME chunked framing as TCP ([chunkLen][tag][data]…), carried inside
        // MAC DATA packets — NOT a bare encrypted blob. The encrypted frame is chunk-wrapped on send and
        // reassembled on receive before AES decryption.
        public void Send(byte[] m2)
            => Send(PKT_DATA, WinboxChunkFraming.Chunk(Keys.Encrypt(m2),
                WinboxChunkFraming.EncryptedTag));

        public byte[]? SendReceive(byte[] m2, int timeoutMs)
        {
            Send(m2);
            return Receive(timeoutMs);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A frame that does not decrypt is dropped and the wait goes on to the deadline (the contract of
        /// <see cref="IWinboxM2Channel.Receive"/>): each frame here carries its own IV, so the next one is readable.
        /// </remarks>
        public byte[]? Receive(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                byte[]? frame = RecvFrame(Math.Max(0, remaining));   // null: nothing within the timeout
                if (frame == null) return null;
                byte[]? plain = DecryptOrDrop(frame);
                if (plain != null) return plain;
                if (remaining <= 0) return null;
            }
        }

        // The frame's plaintext, or null — traced — when it is not a clean M2 frame. Decrypt answers null for a wrong
        // key, a bad pad or a truncated frame and throws only for a cryptographic fault.
        private byte[]? DecryptOrDrop(byte[] frame)
        {
            string dropped;
            try
            {
                byte[]? plain = Keys.Decrypt(frame);
                if (plain != null) return plain;
                dropped = "Decrypt returned null (wrong key, bad padding, a truncated frame, or an HMAC that does not match)";
            }
            catch (Exception ex)
            {
                dropped = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (tik4net.Diagnostics.TikWireTrace.Enabled)
                tik4net.Diagnostics.TikWireTrace.Emit("wbxclimac.session", tik4net.Diagnostics.TikWireDir.Note,
                    $"undecryptable {frame.Length}B frame dropped: {dropped}");
            return null;
        }

        // ── Chunk framing (WinboxChunkFraming, as on TCP, carried in DATA payloads) ──

        // Reassembles one complete chunked frame ([chunkLen][tag][data], chunkLen=0xFF = continuation),
        // reading further DATA packets as needed. Returns the concatenated chunk data, or null on timeout.
        private byte[]? RecvFrame(int timeoutMs)
        {
            // Held for the whole read, not per packet: a frame is reassembled across several DATA packets,
            // and letting the idle pump take a packet out of the middle of one would lose the boundary. The
            // pump asks with TryEnter and simply skips while this is held — a read is servicing the socket
            // anyway, which is the only thing the pump exists to guarantee.
            lock (_rxGate)
            {
                if (_pendingFrame != null)
                {
                    byte[] ready = _pendingFrame;
                    _pendingFrame = null;
                    return ready;
                }

                byte[]? frame = TryExtractFrame();
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (frame == null)
                {
                    int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0) return null;
                    byte[]? data = RecvDataPayload(remaining);
                    if (data == null) return null;
                    _rxBuf.AddRange(data);
                    System.Threading.Interlocked.Add(ref _bytesReceived, data.Length);
                    frame = TryExtractFrame();
                }
                return frame;
            }
        }

        // Tries to pull one complete frame out of _rxBuf. Returns null (and leaves the buffer intact) if
        // the buffer does not yet hold a full frame. Once the stream keys exist every frame is encrypted and
        // ends at its declared length — the router sends no short final chunk after a 3570-byte frame, and
        // waiting for one merged it with the next frame and stalled the terminal (see
        // WinboxChunkFraming.ReadChunkedFrame). The handshake frames before that declare no length.
        private byte[]? TryExtractFrame()
            => WinboxChunkFraming.TryTakeChunkedFrame(_rxBuf, encrypted: _keys != null);

        /// <summary>
        /// Tells the router the session is over before the socket goes away, by sending the MAC-layer
        /// <c>PKT_END</c> — the same courtesy <c>MacTelnetUdpClient.TryCloseSession</c> pays.
        /// </summary>
        /// <remarks>
        /// Closing a UDP socket signals nothing: there is no FIN, so a router that is not told keeps the
        /// login open until its own timeout. Measured on 7.23.2: six WinBox-native-over-MAC
        /// connections opened and disposed left six <c>winbox</c> rows in <c>/user/active</c> that were
        /// still there 15 s later and only expired after roughly a minute and a half — while the TCP
        /// sibling left none, because there the FIN does the telling. This matters to any caller that opens
        /// short-lived connections: one connection per operation leaves the router holding a rolling ~90 s
        /// worth of dead sessions.
        /// <para>Best-effort by design: this runs on the disposal path, where a channel that never
        /// finished connecting has no MACs or socket to send with, and a router that has already dropped
        /// the session has nothing to hear it.</para>
        /// </remarks>
        protected override void OnDisposing()
        {
            _closed = true;   // stops the reader loop before the socket it polls is disposed
            StopIdlePump();   // and the idle pump, for the same reason
            try { Send(PKT_END, null); } catch { /* ignore — see remarks */ }
        }
    }
}
