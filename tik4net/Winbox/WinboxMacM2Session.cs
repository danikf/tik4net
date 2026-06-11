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

        private int _reqId;
        // Buffer of received DATA payload bytes not yet consumed into a complete chunked frame.
        private readonly List<byte> _rxBuf = new List<byte>();

        internal WinboxMacM2Session(string routerMac)
        {
            RouterMacOverride = routerMac;
        }

        // ── IWinboxM2Channel ──────────────────────────────────────────────────

        public bool IsEncrypted => _sendAesKey != null;

        public bool DataAvailable => _udp != null && _udp.Available > 0;

        /// <summary>
        /// Connects over the MAC layer and authenticates (EC-SRP5). <paramref name="port"/> and
        /// <paramref name="timeoutMs"/> are ignored — MAC always uses UDP 20561 and the base auth
        /// uses its own receive deadlines.
        /// </summary>
        public void Open(string host, int port, string user, string password, int timeoutMs)
        {
            BaseConnect(host, ClientType);
            // MAC-WinBox carries the SAME WinBox EC-SRP5 handshake as TCP (length-prefixed [len][0x06]
            // frames), tunnelled inside MAC-layer DATA packets — NOT the MAC-Telnet control-packet auth.
            MacAuthEcSrp5(user, password);
        }

        // ── WinBox EC-SRP5 handshake over the MAC layer ───────────────────────

        private void MacAuthEcSrp5(string user, string pass)
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
            byte[] challenge = RecvHandshakeFrame(10000);
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

            byte[] serverCc = RecvHandshakeFrame(10000);
            byte[] expectedCc = EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray());
            if (serverCc == null || !serverCc.SequenceEqual(expectedCc))
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
                    if (type == PKT_ACK) return false;
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

        public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)(++_reqId));

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

        // Dispose is inherited from MacLayerTransport (disposes the UDP socket).
    }
}
