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
    internal sealed class WinboxM2Session : IWinboxM2Channel
    {
        private const byte EncryptedTag = 0x06;
        private const byte RawTag        = 0x01;

        private readonly WinboxTcpTransport _transport = new WinboxTcpTransport();
        private byte[] _sendAesKey, _sendHmacKey, _receiveAesKey, _receiveHmacKey;
        private bool _encrypted;
        private int _reqId;

        /// <summary>True once an encrypted (EC-SRP5) channel is established. False for legacy MD5 auth.</summary>
        public bool IsEncrypted => _encrypted;

        public bool DataAvailable => _transport.DataAvailable;

        // TCP: a waiting byte is a real buffered M2 frame, so the stale-frame drain is safe and effective.
        public bool SupportsStaleDrain => true;

        // ── Connect + authenticate ────────────────────────────────────────────

        /// <summary>Connects to the router (TCP 8291) and authenticates (EC-SRP5, legacy MD5 fallback).</summary>
        public void Open(string host, int port, string user, string password, int timeoutMs)
        {
            Connect(host, port, timeoutMs);
            Authenticate(host, port, timeoutMs, user, password);
        }

        private void Connect(string host, int port, int timeoutMs)
            => _transport.Connect(host, port, timeoutMs);

        private void Authenticate(string host, int port, int timeoutMs, string user, string pass)
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
        public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)(++_reqId));

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
