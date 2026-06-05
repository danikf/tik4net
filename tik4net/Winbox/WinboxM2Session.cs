using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Crypto;
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
    internal sealed class WinboxM2Session : IDisposable
    {
        private const byte EncryptedTag = 0x06;
        private const byte RawTag        = 0x01;

        private readonly WinboxTcpTransport _transport = new WinboxTcpTransport();
        private byte[] _sendAesKey, _sendHmacKey, _receiveAesKey, _receiveHmacKey;
        private bool _encrypted;
        private int _reqId;

        /// <summary>True once an encrypted (EC-SRP5) channel is established. False for legacy MD5 auth.</summary>
        internal bool IsEncrypted => _encrypted;

        internal bool DataAvailable => _transport.DataAvailable;

        // ── Connect + authenticate ────────────────────────────────────────────

        internal void Connect(string host, int port, int timeoutMs)
            => _transport.Connect(host, port, timeoutMs);

        internal void Authenticate(string host, int port, int timeoutMs, string user, string pass)
        {
            try
            {
                EcSrp5Auth(user, pass);
                _encrypted = true;
            }
            catch (Exception ex) when (ex.Message.IndexOf("EC-SRP5", StringComparison.Ordinal) >= 0)
            {
                // Old RouterOS — reconnect and try legacy MD5 auth.
                _transport.Dispose();
                _reqId = 0;
                _transport.Connect(host, port, timeoutMs);
                LegacyMd5Auth(user, pass);
                _encrypted = false;
            }
        }

        // ── Generic M2 message I/O (mode-agnostic) ────────────────────────────

        /// <summary>Sends one M2 message and reads one response, honouring the channel mode.</summary>
        internal byte[] SendReceive(byte[] m2, int timeoutMs)
        {
            if (_encrypted) { EncryptAndSend(m2); return RecvAndDecrypt(timeoutMs); }
            return SendRecvRaw(m2, timeoutMs);
        }

        /// <summary>Sends one M2 message without waiting for a response (fire-and-forget).</summary>
        internal void Send(byte[] m2)
        {
            if (_encrypted) EncryptAndSend(m2);
            else _transport.SendRaw(m2);
        }

        /// <summary>Receives and decodes one M2 frame already arriving on the wire.</summary>
        internal byte[] Receive(int timeoutMs)
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
        internal byte[] NextReqIdField() => M2Message.U8Sys(0xFF0006, (byte)(++_reqId));

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

        private void EcSrp5Auth(string user, string pass)
        {
            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);

            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);
            byte[] userBytes = Encoding.UTF8.GetBytes(user);
            byte[] payload = userBytes.Concat(new byte[] { 0 }).Concat(xWA)
                .Concat(new byte[] { (byte)parityA }).ToArray();
            SendHandshake(payload);

            _transport.SetReceiveTimeout(3000);
            byte respLen, respTag;
            try
            {
                byte[] hdr = _transport.ReadExact(2);
                respLen = hdr[0];
                respTag = hdr[1];
            }
            catch (IOException)
            {
                throw new InvalidOperationException("EC-SRP5 not supported by server");
            }
            _transport.SetReceiveTimeout(10000);

            if (respTag != 0x06)
                throw new InvalidOperationException(
                    $"EC-SRP5 not supported by server (tag=0x{respTag:x2})");
            if (respLen != 49)
                throw new InvalidOperationException(
                    $"Unexpected challenge size {respLen}, expected 49");

            byte[] challenge = _transport.ReadExact(respLen);
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
            SendHandshake(clientCc);

            byte[] srvHdr   = _transport.ReadExact(2);
            byte[] serverCc = _transport.ReadExact(srvHdr[0]);
            byte[] expectedCc = EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray());
            if (!serverCc.SequenceEqual(expectedCc))
                throw new UnauthorizedAccessException("Wrong username or password");

            WinboxStreamCrypto.DeriveStreamKeys(false, secret,
                out _sendAesKey, out _receiveAesKey,
                out _sendHmacKey, out _receiveHmacKey);
        }

        private void SendHandshake(byte[] payload)
        {
            byte[] frame = new byte[] { (byte)payload.Length, 0x06 }.Concat(payload).ToArray();
            _transport.Stream.Write(frame, 0, frame.Length);
        }

        // ── Legacy MD5 authentication (pre-6.43 RouterOS) ─────────────────────

        private void LegacyMd5Auth(string user, string pass)
        {
            byte[] listMsg = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 7),
                M2Message.BoolSys(0xFF0005, true), NextReqIdField(), M2Message.StringUser(1, "list"));
            byte[] listResp = SendRecvRaw(listMsg, 5000);
            int sessionId = M2Message.ParseSessionId(listResp);

            byte[] challengeSetup = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 5),
                M2Message.BoolSys(0xFF0005, true), NextReqIdField(), M2Message.SessionIdField(sessionId));
            _transport.SendRaw(challengeSetup);
            Thread.Sleep(200);
            DrainSocket(500);

            byte[] saltMsg = M2Message.BuildM2(
                M2Message.SysToArr(13, 4), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 4),
                M2Message.BoolSys(0xFF0005, true), NextReqIdField(), M2Message.SessionIdField(sessionId));
            byte[] saltResp = SendRecvRaw(saltMsg, 5000);
            byte[] salt = M2Message.ParseRawUser(saltResp, 9);
            if (salt == null)
                throw new InvalidOperationException("No salt in challenge response");

            byte[] hashInput = new byte[] { 0 }
                .Concat(Encoding.UTF8.GetBytes(pass)).Concat(salt).ToArray();
            byte[] hash = new byte[] { 0 }
                .Concat(MD5.Create().ComputeHash(hashInput)).ToArray();

            byte[] loginMsg = M2Message.BuildM2(
                M2Message.SysToArr(13, 4), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 1),
                M2Message.BoolSys(0xFF0005, true), NextReqIdField(), M2Message.SessionIdField(sessionId),
                M2Message.StringUser(1, user), M2Message.RawUser(9, salt), M2Message.RawUser(10, hash));
            byte[] loginResp = SendRecvRaw(loginMsg, 5000);

            int status = M2Message.ParseSysStatus(loginResp);
            if (status != 0)
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
