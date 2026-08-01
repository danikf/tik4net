using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Crypto;
using tik4net.Diagnostics;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.Winbox
{
    /// <summary>
    /// Shared WinBox M2 protocol layer over TCP port 8291: EC-SRP5 (with legacy MD5 fallback)
    /// authentication and AES-128-CBC MAC-then-Encrypt frame I/O. This is the transport-and-crypto
    /// layer only — it knows nothing about terminals or native handlers.
    /// </summary>
    /// <remarks>
    /// Shared by both WinBox modes over TCP: the CLI/terminal mode (<c>tik4net.WinboxCli</c>, which
    /// builds mepty messages on top of <see cref="Send"/>/<see cref="Receive"/>) and the future native
    /// M2 mode. Keep it free of mode-specific message building so both can reuse it.
    /// </remarks>
    internal sealed class WinboxM2Session : IWinboxM2Channel
    {
        private const byte EncryptedTag = 0x06;
        private const byte RawTag        = 0x01;

        /// <summary>Floor under the wait for the EC-SRP5 challenge — see <see cref="Authenticate"/>.</summary>
        private const int MinEcSrp5ProbeMs = 3000;

        /// <summary>Size of the server-confirmation payload: one SHA-256 digest.</summary>
        private const int ConfirmationLength = 32;

        private readonly WinboxTcpTransport _transport = new WinboxTcpTransport();
        private byte[] _sendAesKey, _sendHmacKey, _receiveAesKey, _receiveHmacKey;
        private bool _encrypted;
        private int _reqId;
        private bool _readerLoopTimeoutSet;

        /// <summary>True once an encrypted (EC-SRP5) channel is established. False for legacy MD5 auth.</summary>
        public bool IsEncrypted => _encrypted;

        public bool DataAvailable => _transport.DataAvailable;

        // TCP: a waiting byte is a real buffered M2 frame, so the stale-frame drain is safe and effective.
        public bool SupportsStaleDrain => true;

        // TCP has no per-message acknowledgement to notice the absence of: the kernel either delivers the
        // bytes or tears the connection down, and a router that drops the session sends a FIN/RST that
        // surfaces as an IOException from the read. There is nothing to report here that the socket has not
        // already reported louder.
        public bool SendAbandoned => false;

        // ── Connect + authenticate ────────────────────────────────────────────

        /// <inheritdoc/>
        public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs)
        {
            _transport.Connect(host, port, connectTimeoutMs, ioTimeoutMs);
            Authenticate(host, port, connectTimeoutMs, ioTimeoutMs, user, pass: password);
        }

        /// <summary>
        /// EC-SRP5 first, falling back to the pre-6.43 MD5 handshake only when the router shows no sign
        /// of understanding EC-SRP5 at all.
        /// </summary>
        /// <remarks>
        /// The fallback is decided on <see cref="WinboxEcSrp5UnsupportedException"/> and nothing else.
        /// It used to be selected by testing whether the message text mentioned "EC-SRP5", which made
        /// every future wording change a control-flow change.
        /// <para>The probe window matters more than it looks. Silence is the only evidence a router
        /// offers for "too old to speak EC-SRP5", so a window short enough for a busy router to miss
        /// turns a slow answer into a wrong conclusion — and the wrong conclusion is expensive, because
        /// legacy auth then fails on a modern router and the failure surfaces as "wrong username or
        /// password" (P2.41). Hence <see cref="MinEcSrp5ProbeMs"/> as a floor under the caller's
        /// connect timeout rather than the fixed 3 s this used to wait, and hence the wrapping below:
        /// if both mechanisms fail, the report names the EC-SRP5 exchange that actually broke instead
        /// of blaming the credentials.</para>
        /// </remarks>
        private void Authenticate(string host, int port, int connectTimeoutMs, int ioTimeoutMs, string user, string pass)
        {
            try
            {
                EcSrp5Auth(user, pass, Math.Max(MinEcSrp5ProbeMs, connectTimeoutMs));
                _encrypted = true;
            }
            catch (WinboxEcSrp5UnsupportedException unsupported)
            {
                // Old RouterOS — reconnect and try legacy MD5 auth.
                _transport.Dispose();
                _reqId = 0;
                _transport.Connect(host, port, connectTimeoutMs, ioTimeoutMs);
                try
                {
                    LegacyMd5Auth(user, pass);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InvalidOperationException(
                        $"{ex.Message} — but note that this router was only asked for legacy MD5 auth " +
                        $"because {unsupported.Reason}, so the credentials may be fine and the EC-SRP5 " +
                        "handshake the real failure.", unsupported);
                }
                _encrypted = false;
            }
        }

        // ── Generic M2 message I/O (mode-agnostic) ────────────────────────────

        /// <summary>Sends one M2 message and reads one response, honouring the channel mode.</summary>
        public byte[] SendReceive(byte[] m2, int timeoutMs)
        {
            if (_encrypted) { EncryptAndSend(m2); return RecvAndDecrypt(timeoutMs); }
            return SendRecvRaw(m2, timeoutMs);
        }

        /// <summary>Sends one M2 message without waiting for a response (fire-and-forget).</summary>
        public void Send(byte[] m2)
        {
            if (_encrypted) EncryptAndSend(m2);
            else _transport.SendRaw(m2);
        }

        /// <summary>Receives and decodes one M2 frame already arriving on the wire.</summary>
        public byte[] Receive(int timeoutMs)
        {
            if (_encrypted) return RecvAndDecrypt(timeoutMs);

            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] assembled = _transport.RecvChunked(RawTag);
                return assembled.Length >= 2 ? assembled.Skip(2).ToArray() : assembled;
            }
            finally { _transport.SetReceiveTimeout(old); }
        }

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
        public bool SupportsReaderLoop => true;

        /// <inheritdoc/>
        public byte[] ReceiveNextFrame()
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
                if (_encrypted)
                    return WinboxStreamCrypto.Decrypt(_transport.RecvChunked(EncryptedTag), _receiveAesKey);

                byte[] assembled = _transport.RecvChunked(RawTag);
                return assembled.Length >= 2 ? assembled.Skip(2).ToArray() : assembled;
            }
            catch (IOException)          { return null; }   // socket closed under us — normal shutdown
            catch (ObjectDisposedException) { return null; }
        }

        // ── Encrypted / raw frame primitives ──────────────────────────────────

        private void EncryptAndSend(byte[] msg)
        {
            byte[] full = WinboxStreamCrypto.Encrypt(msg, _sendAesKey, _sendHmacKey);
            _transport.SendChunked(full, EncryptedTag);
        }

        private byte[] RecvAndDecrypt(int timeoutMs)
        {
            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] assembled = _transport.RecvChunked(EncryptedTag);
                return WinboxStreamCrypto.Decrypt(assembled, _receiveAesKey);
            }
            finally { _transport.SetReceiveTimeout(old); }
        }

        private byte[] SendRecvRaw(byte[] m2, int timeoutMs)
        {
            _transport.SendRaw(m2);
            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] assembled = _transport.RecvChunked(RawTag);
                return assembled.Length >= 2 ? assembled.Skip(2).ToArray() : assembled;
            }
            finally { _transport.SetReceiveTimeout(old); }
        }

        // ── EC-SRP5 authentication ────────────────────────────────────────────

        private void EcSrp5Auth(string user, string pass, int probeTimeoutMs)
        {
            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);

            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);
            byte[] userBytes = Encoding.UTF8.GetBytes(user);
            byte[] payload = userBytes.Concat(new byte[] { 0 }).Concat(xWA)
                .Concat(new byte[] { (byte)parityA }).ToArray();
            SendHandshake(payload, "client public key");

            // A server without EC-SRP5 support simply stays silent here, so this window is the whole
            // basis for the legacy fallback — see the remarks on Authenticate for why it is not 3 s flat.
            int ioTimeout = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(probeTimeoutMs);
            byte respLen, respTag;
            try
            {
                byte[] hdr = _transport.ReadExact(2);
                respLen = hdr[0];
                respTag = hdr[1];
            }
            catch (IOException ex)
            {
                throw new WinboxEcSrp5UnsupportedException(
                    $"the router sent nothing within {probeTimeoutMs} ms of the client public key", ex);
            }
            finally { _transport.SetReceiveTimeout(ioTimeout); }

            if (respTag != EncryptedTag)
                throw new WinboxEcSrp5UnsupportedException(
                    $"the challenge frame carried tag 0x{respTag:x2}, not 0x{EncryptedTag:x2}");
            // Not an "unsupported" verdict: a router that answers on the right tag with the wrong size
            // does speak EC-SRP5 and something else went wrong, so retrying with MD5 would only hide it.
            if (respLen != 49)
                throw new InvalidOperationException(
                    $"WinBox EC-SRP5 handshake: challenge frame is {respLen} B, expected 49.");

            byte[] challenge = _transport.ReadExact(respLen);
            TraceHandshake(TikWireDir.Recv, challenge, "challenge");
            byte[] xWB   = challenge.Take(32).ToArray();
            int parityB  = challenge[32];
            byte[] salt  = challenge.Skip(33).Take(16).ToArray();

            byte[] valPriv  = EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v   = EcSrp5.Redp1(xGamma, 1);
            ECPoint wB  = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum = EcSrp5.ECAdd(wB, v);

            byte[] j   = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
            var iInt   = EcSrp5.BEToBI(valPriv);
            var jInt   = EcSrp5.BEToBI(j);
            var aInt   = EcSrp5.BEToBI(privA);
            var vh     = (iInt * jInt % EcSrp5.R + aInt) % EcSrp5.R;

            ECPoint zPt = EcSrp5.ECScalarMul(vh, sum);
            var (zMont, _) = EcSrp5.ToMontgomery(zPt);
            byte[] secret = EcSrp5.Sha256(zMont);

            byte[] clientCc = EcSrp5.Sha256(j.Concat(zMont).ToArray());
            SendHandshake(clientCc, "client confirmation");

            byte[] srvHdr = _transport.ReadExact(2);
            if (srvHdr[1] != EncryptedTag)
                throw new InvalidOperationException(
                    $"WinBox EC-SRP5 handshake: server confirmation carried tag 0x{srvHdr[1]:x2}, " +
                    $"not 0x{EncryptedTag:x2}. The handshake did not complete.");

            byte[] serverCc = _transport.ReadExact(srvHdr[0]);
            TraceHandshake(TikWireDir.Recv, serverCc, "server confirmation");
            // Where the confirmation digest belongs, the router may instead put a refusal in plain words.
            // Reading it beats inferring one: "invalid user name or password (6)" is the router's own
            // account of what happened, and it is not always true — see WinboxLoginRefusedException.
            WinboxHandshakeReply.ThrowIfRouterMessage(serverCc, ConfirmationLength);

            byte[] expectedCc = EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray());
            if (!serverCc.SequenceEqual(expectedCc))
                throw new UnauthorizedAccessException("Wrong username or password");

            WinboxStreamCrypto.DeriveStreamKeys(false, secret,
                out _sendAesKey, out _receiveAesKey,
                out _sendHmacKey, out _receiveHmacKey);
        }

        private void SendHandshake(byte[] payload, string what)
        {
            byte[] frame = new byte[] { (byte)payload.Length, EncryptedTag }.Concat(payload).ToArray();
            TraceHandshake(TikWireDir.Send, payload, what);
            _transport.Stream.Write(frame, 0, frame.Length);
        }

        // The handshake writes to the stream and reads through ReadExact, so none of it passes the
        // SendChunked/RecvChunked emit points — leaving the one exchange that is hardest to reason about
        // as the only part of the transport a wire trace could not see (P2.41).
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
            byte[] salt = M2Message.ParseRawUser(saltResp, WinboxM2Protocol.LegacyAuth.Key.Salt);
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
            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] buf = new byte[4096];
                while (_transport.DataAvailable) _transport.Stream.Read(buf, 0, buf.Length);
            }
            catch (IOException) { }
            finally { _transport.SetReceiveTimeout(old); }
        }

        public void Dispose() => _transport.Dispose();
    }
}
