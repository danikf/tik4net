using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Crypto;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// Shared WinBox M2 protocol layer over TCP port 8291: EC-SRP5 (with legacy MD5 fallback)
    /// authentication and AES-128-CBC MAC-then-Encrypt frame I/O. This is the transport-and-crypto
    /// layer only — it knows nothing about terminals or native handlers.
    /// </summary>
    /// <remarks>
    /// Shared by both WinBox modes over TCP: the CLI/terminal mode (<c>tik4net.WinboxCli</c>, which
    /// builds mepty messages on top of <see cref="Send"/>/<see cref="Receive"/>) and the native M2 mode
    /// (<c>tik4net.WinboxNative</c>). It builds no message of either mode; the only M2 messages it builds are the
    /// legacy MD5 login's (<see cref="LegacyMd5Auth"/>), which is part of authenticating.
    /// </remarks>
    internal sealed class WinboxM2Session : IWinboxM2Channel
    {
        /// <summary>Floor under the wait for the EC-SRP5 challenge — see <see cref="Authenticate"/>.</summary>
        private const int MinEcSrp5ProbeMs = 3000;

        /// <summary>Size of the server-confirmation payload: one SHA-256 digest.</summary>
        private const int ConfirmationLength = 32;

        private readonly WinboxTcpTransport _transport = new WinboxTcpTransport();
        // Set by EcSrp5Auth; null on the legacy MD5 path, which is unencrypted.
        private WinboxStreamKeys? _keys;
        private int _reqId;
        private bool _readerLoopTimeoutSet;

        /// <summary>True once an encrypted (EC-SRP5) channel is established. False for legacy MD5 auth.</summary>
        public bool IsEncrypted => _keys != null;

        private WinboxStreamKeys Keys
            => _keys ?? throw new InvalidOperationException("The WinBox session is not encrypted (legacy MD5 login).");

        public bool DataAvailable => _transport.DataAvailable;

        /// <inheritdoc/>
        public long BytesReceived => _transport.BytesRead;

        // TCP: a waiting byte is a real buffered M2 frame, so the stale-frame drain is safe and effective.
        public bool SupportsStaleDrain => true;

        // TCP has no per-message acknowledgement to notice the absence of: the kernel either delivers the
        // bytes or tears the connection down, and a router that drops the session sends a FIN/RST that
        // surfaces as an IOException from the read. There is nothing to report here that the socket has not
        // already reported louder.
        public bool SendAbandoned => false;

        // Same reason: there is no per-message acknowledgement to be waiting for, and TCP's own window
        // already throttles a sender the kernel cannot drain.
        public bool SendStalled => false;

        // Nothing to do: TCP acknowledges the byte stream in the kernel, so router-initiated console output
        // is acknowledged whether or not anyone is reading. It simply waits in the receive buffer.
        public void StartIdleServicing() { }

        // ── Connect + authenticate ────────────────────────────────────────────

        /// <inheritdoc/>
        public void Open(string host, int port, string user, string password, int connectTimeoutMs,
            int ioTimeoutMs, int sendTimeoutMs = 0)
        {
            _transport.Connect(host, port, connectTimeoutMs, ioTimeoutMs, sendTimeoutMs);
            Authenticate(host, port, connectTimeoutMs, ioTimeoutMs, user, pass: password);
        }

        /// <summary>
        /// EC-SRP5 first, falling back to the pre-6.43 MD5 handshake only when the router shows no sign
        /// of understanding EC-SRP5 at all.
        /// </summary>
        /// <remarks>
        /// The fallback is decided on <see cref="WinboxEcSrp5UnsupportedException"/> and nothing else.
        /// Selecting it by testing whether the message text mentions "EC-SRP5" would make every future
        /// wording change a control-flow change.
        /// <para>The probe window matters more than it looks. Silence is the only evidence a router
        /// offers for "too old to speak EC-SRP5", so a window short enough for a busy router to miss
        /// turns a slow answer into a wrong conclusion — and the wrong conclusion is expensive, because
        /// legacy auth then fails on a modern router and the failure surfaces as "wrong username or
        /// password". Hence <see cref="MinEcSrp5ProbeMs"/> as a floor under the caller's
        /// connect timeout rather than a fixed short wait, and hence the wrapping below:
        /// if both mechanisms fail, the report names the EC-SRP5 exchange that actually broke instead
        /// of blaming the credentials.</para>
        /// </remarks>
        private void Authenticate(string host, int port, int connectTimeoutMs, int ioTimeoutMs, string user, string pass)
        {
            try
            {
                EcSrp5Auth(user, pass, Math.Max(MinEcSrp5ProbeMs, TikTimeouts.HandshakeBoundMs(connectTimeoutMs, ioTimeoutMs)));
            }
            catch (WinboxEcSrp5UnsupportedException unsupported)
            {
                // Old RouterOS — reconnect and try legacy MD5 auth.
                _transport.Dispose();
                _reqId = 0;
                _transport.Connect(host, port, connectTimeoutMs, ioTimeoutMs);
                try
                {
                    // The account name alone: login options ("+c") are measured on EC-SRP5 routers only.
                    LegacyMd5Auth(EcSrp5.AccountName(user), pass);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InvalidOperationException(
                        $"{ex.Message} — but note that this router was only asked for legacy MD5 auth " +
                        $"because {unsupported.Reason}, so the credentials may be fine and the EC-SRP5 " +
                        "handshake the real failure.", unsupported);
                }
                _keys = null;
            }
        }

        // ── Generic M2 message I/O (mode-agnostic) ────────────────────────────

        /// <inheritdoc cref="IWinboxM2Channel.SendReceive"/>
        public byte[]? SendReceive(byte[] m2, int timeoutMs)
        {
            Send(m2);
            return Receive(timeoutMs);
        }

        /// <summary>Sends one M2 message without waiting for a response (fire-and-forget).</summary>
        public void Send(byte[] m2)
        {
            if (IsEncrypted) EncryptAndSend(m2);
            else _transport.SendRaw(m2);
        }

        /// <summary>Receives and decodes one M2 frame, or <c>null</c> when none began within the timeout.</summary>
        /// <remarks>
        /// The socket's receive timeout (SO_RCVTIMEO) is the only deadline TCP has, and where it fires decides what it
        /// means. Before the first byte of a frame the stream is still on a frame boundary, so the timeout is "nothing
        /// arrived" and the channel is as usable as before. Once part of a frame has been read it is not: the next read
        /// would start inside that frame, so the timeout keeps throwing, as does a frame that does not decrypt
        /// (<see cref="DecryptOrThrow"/>) — review D9.
        /// </remarks>
        public byte[]? Receive(int timeoutMs)
        {
            long before = _transport.BytesRead;
            try
            {
                if (IsEncrypted)
                    return _transport.WithReceiveTimeout(timeoutMs,
                        () => DecryptOrThrow(_transport.RecvChunked(WinboxChunkFraming.EncryptedTag), Keys));

                return _transport.WithReceiveTimeout(timeoutMs,
                    () => WinboxChunkFraming.StripRawLength(_transport.RecvChunked(WinboxChunkFraming.RawTag)));
            }
            catch (IOException ex) when (_transport.BytesRead == before && IsReceiveTimeout(ex))
            {
                return null;
            }
        }

        private static bool IsReceiveTimeout(IOException ex)
            => ex.InnerException is System.Net.Sockets.SocketException se
            && se.SocketErrorCode == System.Net.Sockets.SocketError.TimedOut;

        /// <summary>Builds the next request-id system field (key 0xFF0006), incrementing the counter.</summary>
        /// <remarks>
        /// <see cref="Interlocked"/> because a multiplexed connection has concurrent senders
        /// (design §4.1). The 8-bit truncation is the wire format, not a choice — id allocation that has to
        /// avoid colliding with a still-pending id lives in <see cref="WinboxM2Multiplexer"/>, which is why
        /// the native path uses <see cref="WinboxM2Multiplexer.NextReqId"/> rather than this method.
        /// </remarks>
        public byte[] NextReqIdField()
            => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)Interlocked.Increment(ref _reqId));

        /// <inheritdoc/>
        public byte[]? ReceiveNextFrame()
        {
            // The reader loop owns the socket, so the receive timeout goes to infinite once and stays there:
            // a socket-level deadline can fire between two chunks of one frame and leave the stream
            // unrecoverably desynchronized. Per-request deadlines live in the multiplexer's registrations.
            if (!_readerLoopTimeoutSet)
            {
                _transport.SetReceiveTimeout(0);   // Socket: 0 == infinite
                _readerLoopTimeoutSet = true;
            }

            try
            {
                // Null is the interface's "channel closed". A frame that does not decrypt must NOT take that
                // path — see DecryptOrThrow.
                if (_keys != null)
                    return DecryptOrThrow(_transport.RecvChunked(WinboxChunkFraming.EncryptedTag), _keys);

                byte[] assembled = _transport.RecvChunked(WinboxChunkFraming.RawTag);
                return WinboxChunkFraming.StripRawLength(assembled);
            }
            catch (IOException)          { return null; }   // socket closed under us — normal shutdown
            catch (ObjectDisposedException) { return null; }
        }

        // ── Encrypted / raw frame primitives ──────────────────────────────────

        private void EncryptAndSend(byte[] msg)
        {
            byte[] full = Keys.Encrypt(msg);
            _transport.SendChunked(full, WinboxChunkFraming.EncryptedTag);
        }

        /// <summary>
        /// Decrypts one assembled frame, or throws naming what happened.
        /// </summary>
        /// <remarks>
        /// <see cref="WinboxStreamKeys.Decrypt"/> answers <c>null</c> for a wrong key, a bad pad, an HMAC that does not
        /// match and a truncated frame alike, and <c>null</c> is also this channel's word for "nothing arrived" — so
        /// returning it made a corrupt frame arrive at the caller as an orderly shutdown, with the reader
        /// loop failing every waiter with "the WinBox M2 channel was closed". It is a protocol failure and is
        /// reported as one, the same way <c>RecvChunked</c> reports a frame with the wrong tag: the session
        /// is finished either way, but only one of the two messages sends the reader to the right place.
        /// </remarks>
        /// <param name="assembled">The reassembled ciphertext frame.</param>
        /// <param name="keys">The session's stream keys (the receive-side AES and HMAC keys are used).</param>
        internal static byte[] DecryptOrThrow(byte[] assembled, WinboxStreamKeys keys)
        {
            byte[]? plain = keys.Decrypt(assembled);
            if (plain == null)
                throw new InvalidOperationException(
                    $"A {assembled?.Length ?? 0}-byte encrypted WinBox M2 frame could not be decrypted "
                    + "(wrong key, bad padding, a truncated frame, or an HMAC that does not match). The stream cannot be "
                    + "resynchronized, so this session is finished — open a new connection.");
            return plain;
        }

        // The legacy login's round trip: a reply that does not come within the timeout is a failed login step.
        private byte[] SendRecvRaw(byte[] m2, int timeoutMs)
        {
            _transport.SendRaw(m2);
            return _transport.WithReceiveTimeout(timeoutMs,
                () => WinboxChunkFraming.StripRawLength(_transport.RecvChunked(WinboxChunkFraming.RawTag)));
        }

        // ── EC-SRP5 authentication ────────────────────────────────────────────

        /// <summary>
        /// Test seam for the EC-SRP5 known-answer vectors: a fixed 32-byte client private key. Null — always,
        /// outside the unit tests — draws a random one per handshake.
        /// </summary>
        internal byte[]? EcSrp5PrivateKeyOverride { get; set; }

        private void EcSrp5Auth(string user, string pass, int probeTimeoutMs)
        {
            var client = new EcSrp5Client(user, EcSrp5PrivateKeyOverride);
            SendHandshake(client.Hello, "client public key");

            // A server without EC-SRP5 support simply stays silent here, so this window is the whole
            // basis for the legacy fallback — see the remarks on Authenticate for why it is not 3 s flat.
            byte[] hdr;
            try { hdr = _transport.WithReceiveTimeout(probeTimeoutMs, () => _transport.ReadExact(2)); }
            catch (IOException ex)
            {
                throw new WinboxEcSrp5UnsupportedException(
                    $"the router sent nothing within {probeTimeoutMs} ms of the client public key", ex);
            }
            byte respLen = hdr[0], respTag = hdr[1];

            if (respTag != WinboxChunkFraming.EncryptedTag)
                throw new WinboxEcSrp5UnsupportedException(
                    $"the challenge frame carried tag 0x{respTag:x2}, not 0x{WinboxChunkFraming.EncryptedTag:x2}");
            // Not an "unsupported" verdict: a router that answers on the right tag with the wrong size
            // does speak EC-SRP5 and something else went wrong, so retrying with MD5 would only hide it.
            if (respLen != EcSrp5Client.ChallengeLength)
                throw new InvalidOperationException(
                    $"WinBox EC-SRP5 handshake: challenge frame is {respLen} B, expected {EcSrp5Client.ChallengeLength}.");

            byte[] challenge = _transport.ReadExact(respLen);
            TraceHandshake(TikWireDir.Recv, challenge, "challenge");

            // RouterOS hashes only the account name into the validator: login options after '+' (WinBox's "user+r" to a
            // RoMON agent) travel in the name sent above but not here (Docs/findings-romon.md, "Agent login").
            EcSrp5Proof proof = client.Answer(challenge, pass, EcSrp5.AccountName(user));
            SendHandshake(proof.ClientConfirmation, "client confirmation");

            byte[] srvHdr = _transport.ReadExact(2);
            if (srvHdr[1] != WinboxChunkFraming.EncryptedTag)
                throw new InvalidOperationException(
                    $"WinBox EC-SRP5 handshake: server confirmation carried tag 0x{srvHdr[1]:x2}, " +
                    $"not 0x{WinboxChunkFraming.EncryptedTag:x2}. The handshake did not complete.");

            byte[] serverCc = _transport.ReadExact(srvHdr[0]);
            TraceHandshake(TikWireDir.Recv, serverCc, "server confirmation");
            // Where the confirmation digest belongs, the router may instead put a refusal in plain words.
            // Reading it beats inferring one: "invalid user name or password (6)" is the router's own
            // account of what happened, and it is not always true — see TikConnectionLoginRefusedException.
            proof.Verify(serverCc, ConfirmationLength);

            _keys = WinboxStreamKeys.Derive(proof.Secret);
        }

        private void SendHandshake(byte[] payload, string what)
        {
            byte[] frame = new byte[] { (byte)payload.Length, WinboxChunkFraming.EncryptedTag }.Concat(payload).ToArray();
            TraceHandshake(TikWireDir.Send, payload, what);
            _transport.Stream.Write(frame, 0, frame.Length);
        }

        // The handshake writes to the stream and reads through ReadExact, so none of it passes the
        // SendChunked/RecvChunked emit points — leaving the one exchange that is hardest to reason about
        // as the only part of the transport a wire trace could not see.
        private static void TraceHandshake(TikWireDir dir, byte[] payload, string what)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", dir, payload, 0, payload?.Length ?? 0,
                    "ecsrp5 " + what);
        }

        // ── Legacy MD5 authentication (pre-6.43 RouterOS) ─────────────────────

        private void LegacyMd5Auth(string user, string pass)
        {
            byte[] listMsg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mproxy.Handler), M2Message.SysFrom(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mproxy.OpenStatic),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(),
                M2Message.StringUser(WinboxM2Protocol.Mproxy.Key.FileName, "list"));
            byte[] listResp = SendRecvRaw(listMsg, 5000);
            int sessionId = M2Message.ParseSessionId(listResp);

            byte[] challengeSetup = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mproxy.Handler), M2Message.SysFrom(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mproxy.Setup),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(), M2Message.SessionIdField(sessionId));
            _transport.SendRaw(challengeSetup);
            Thread.Sleep(200);
            DrainSocket(500);

            byte[] saltMsg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.SysInfo.Handler), M2Message.SysFrom(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.LegacyAuth.GetSalt),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(), M2Message.SessionIdField(sessionId));
            byte[] saltResp = SendRecvRaw(saltMsg, 5000);
            byte[]? salt = M2Message.ParseUserBytes(saltResp, WinboxM2Protocol.LegacyAuth.Key.Salt);
            if (salt == null)
                throw new InvalidOperationException("No salt in challenge response");

            byte[] hashInput = new byte[] { 0 }
                .Concat(Encoding.UTF8.GetBytes(pass)).Concat(salt).ToArray();
            byte[] hash = new byte[] { 0 }
                .Concat(MD5.Create().ComputeHash(hashInput)).ToArray();

            byte[] loginMsg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.SysInfo.Handler), M2Message.SysFrom(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.LegacyAuth.Login),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), NextReqIdField(), M2Message.SessionIdField(sessionId),
                M2Message.StringUser(WinboxM2Protocol.LegacyAuth.Key.User, user),
                M2Message.RawUser(WinboxM2Protocol.LegacyAuth.Key.Salt, salt),
                M2Message.RawUser(WinboxM2Protocol.LegacyAuth.Key.Hash, hash));
            byte[] loginResp = SendRecvRaw(loginMsg, 5000);

            int status = M2Message.ParseSysStatus(loginResp);
            if (status != WinboxM2Protocol.Error.None)
                throw new UnauthorizedAccessException("Wrong username or password (legacy auth)");
        }

        private void DrainSocket(int timeoutMs)
        {
            try
            {
                _transport.WithReceiveTimeout(timeoutMs, () =>
                {
                    byte[] buf = new byte[4096];
                    // The bytes are discarded, so how many one read returns does not matter; only end-of-stream stops it.
                    while (_transport.DataAvailable && _transport.Stream.Read(buf, 0, buf.Length) > 0) { }
                    return 0;
                });
            }
            catch (IOException) { }
        }

        public void Dispose() => _transport.Dispose();
    }
}
