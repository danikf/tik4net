#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Crypto;
using tik4net.MacTelnet;
using tik4net.Winbox;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// T1 of the WinBox + MAC layer review: known-answer vectors for the three EC-SRP5 client handshakes and for the
    /// WinBox stream cipher, run through the code that ships.
    /// </summary>
    /// <remarks>
    /// The three client bodies — <c>WinboxM2Session.EcSrp5Auth</c> (WinBox over TCP), <c>WinboxMacM2Session.MacAuthEcSrp5</c>
    /// (WinBox over MAC) and <c>MacLayerTransport.Authenticate</c>/<c>FinishAuth</c> (MAC-Telnet) — are driven end to end
    /// against a loopback peer that plays the router's half with the server-side math <see cref="EcSrp5RoundTripTests"/>
    /// already uses. The client's private key is fixed through <c>EcSrp5PrivateKeyOverride</c>, the router's key and salt
    /// are fixed here, so every byte of the exchange is deterministic and is pinned in <c>Golden/ecsrp5-handshake.txt</c>.
    /// <para>The peer also checks the exchange independently: it computes the shared point from the router's side and
    /// refuses a client confirmation that does not match it, and after the handshake one encrypted M2 message goes each
    /// way, so the stream keys the carrier derived are proven by use, not just by value. These vectors are the acceptance
    /// test of C1/C2 (one <c>EcSrp5Client</c>, <c>WinboxStreamKeys</c>).</para>
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class EcSrp5HandshakeVectorTests
    {
        private static readonly byte[] PrivA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] PrivB = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] Salt = Enumerable.Range(0xA0, 16).Select(i => (byte)i).ToArray();
        private const string Password = "t4n-secret";

        private static readonly byte[] ClientMac = { 0x02, 0, 0, 0, 0, 0x01 };
        private static readonly byte[] RouterMac = { 0x02, 0, 0, 0, 0, 0x02 };

        private static byte[] Message()
            => M2Message.BuildM2(M2Message.SysToArr(24, 1), M2Message.SysFrom(),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7), M2Message.StringUser(5, "golden"));

        private static byte[] Reply()
            => M2Message.BuildM2(M2Message.SysToArr(), M2Message.SysFrom(24),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7), M2Message.StringUser(6, "reply"));

        private static readonly byte[] ReplyIv = Enumerable.Range(0x50, 16).Select(i => (byte)i).ToArray();

        // ── the three carriers ────────────────────────────────────────────────

        [TestMethod]
        public void HandshakeVectors_MatchTheGoldenFile()
        {
            var lines = new List<string>();
            lines.Add("# T1 - EC-SRP5 client handshakes, fixed privA/privB/salt (see EcSrp5HandshakeVectorTests)");
            lines.Add("privA " + GoldenFile.Hex(PrivA));
            lines.Add("privB " + GoldenFile.Hex(PrivB));
            lines.Add("salt " + GoldenFile.Hex(Salt));
            lines.Add("password " + Password);

            foreach (string user in new[] { "admin", "admin+t" })
            {
                lines.AddRange(WinboxTcp(user).Select(l => "tcp " + user + " " + l));
                lines.AddRange(WinboxMac(user).Select(l => "wbxmac " + user + " " + l));
                lines.AddRange(MacTelnet(user).Select(l => "mactelnet " + user + " " + l));
            }

            // The four stream keys of one shared secret, both directions — C2 moves this derivation.
            byte[] secret = EcSrp5.Sha256(Enumerable.Range(0, 32).Select(i => (byte)(0xC0 ^ i)).ToArray());
            foreach (bool server in new[] { false, true })
            {
                WinboxStreamCrypto.DeriveStreamKeys(server, secret, out var sa, out var ra, out var sh, out var rh);
                string side = server ? "server" : "client";
                lines.Add("keys " + side + " secret=" + GoldenFile.Hex(secret));
                lines.Add("keys " + side + " sendAes=" + GoldenFile.Hex(sa) + " sendHmac=" + GoldenFile.Hex(sh));
                lines.Add("keys " + side + " recvAes=" + GoldenFile.Hex(ra) + " recvHmac=" + GoldenFile.Hex(rh));
            }

            GoldenFile.AssertMatches("ecsrp5-handshake.txt", lines);
        }

        /// <summary>WinBox over TCP: the real <see cref="WinboxM2Session.Open"/> against a loopback EC-SRP5 peer.</summary>
        private static List<string> WinboxTcp(string user)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var peer = new RouterSide(EcSrp5.AccountName(user));
            var lines = new List<string>();

            var server = Task.Run(() =>
            {
                using (var client = listener.AcceptTcpClient())
                {
                    var s = client.GetStream();
                    byte[] hdr = ReadExact(s, 2);
                    Assert.AreEqual(0x06, hdr[1], "hello tag");
                    byte[] hello = ReadExact(s, hdr[0]);
                    lines.Add("hello " + GoldenFile.Hex(hello));
                    byte[] challenge = peer.Challenge(hello);
                    lines.Add("challenge " + GoldenFile.Hex(challenge));
                    Write(s, new byte[] { 49, 0x06 }.Concat(challenge).ToArray());

                    hdr = ReadExact(s, 2);
                    Assert.AreEqual(0x06, hdr[1], "client confirmation tag");
                    byte[] clientCc = ReadExact(s, hdr[0]);
                    lines.Add("client-confirmation " + GoldenFile.Hex(clientCc));
                    byte[] serverCc = peer.Confirm(clientCc);
                    lines.Add("server-confirmation " + GoldenFile.Hex(serverCc));
                    Write(s, new byte[] { 32, 0x06 }.Concat(serverCc).ToArray());

                    byte[] frame = WinboxTcpTransport.ReadChunkedFrame(n => ReadExact(s, n), 0x06, encrypted: true);
                    peer.AssertDecryptsToMessage(frame);
                    byte[] reply = WinboxStreamCrypto.Encrypt(Reply(), peer.SendAes, peer.SendHmac, ReplyIv);
                    lines.Add("server-frame " + GoldenFile.Hex(reply));
                    Write(s, WinboxTcpTransport.Chunk(reply, 0x06));
                    lines.Add("secret " + GoldenFile.Hex(peer.Secret));
                }
            });

            try
            {
                using (var session = new WinboxM2Session { EcSrp5PrivateKeyOverride = PrivA })
                {
                    session.Open("127.0.0.1", port, user, Password, 5000, 5000);
                    Assert.IsTrue(session.IsEncrypted);
                    session.Send(Message());
                    CollectionAssert.AreEqual(Reply(), session.Receive(5000), "the reply did not decrypt with the session's receive key");
                }
                Assert.IsTrue(server.Wait(10000), "the loopback peer did not finish");
                server.GetAwaiter().GetResult();
            }
            finally { listener.Stop(); }
            return lines;
        }

        /// <summary>WinBox over MAC: <c>WinboxMacM2Session.MacAuthEcSrp5</c> against a loopback MAC-layer peer.</summary>
        /// <remarks>
        /// <c>Open</c> would run <c>BaseConnect</c> (NIC selection, MNDP), which is environment-dependent and not under test,
        /// so the socket and MACs are put in place by reflection and the handshake method is called directly.
        /// </remarks>
        private static List<string> WinboxMac(string user)
        {
            var lines = new List<string>();
            var peer = new RouterSide(EcSrp5.AccountName(user));
            using (var router = new LoopbackMacRouter())
            using (var session = new WinboxMacM2Session(null!))
            {
                session.EcSrp5PrivateKeyOverride = PrivA;
                router.Attach(session, 0x0f90);

                var rx = new List<byte>();
                router.OnData = payload =>
                {
                    rx.AddRange(payload);
                    byte[]? frame = WinboxTcpTransport.TryTakeChunkedFrame(rx, encrypted: peer.Secret != null);
                    if (frame == null) return;
                    if (peer.Challenged == false)
                    {
                        lines.Add("hello " + GoldenFile.Hex(frame));
                        byte[] challenge = peer.Challenge(frame);
                        lines.Add("challenge " + GoldenFile.Hex(challenge));
                        router.SendData(WinboxTcpTransport.Chunk(challenge, 0x06));
                    }
                    else if (peer.Secret == null)
                    {
                        lines.Add("client-confirmation " + GoldenFile.Hex(frame));
                        byte[] serverCc = peer.Confirm(frame);
                        lines.Add("server-confirmation " + GoldenFile.Hex(serverCc));
                        router.SendData(WinboxTcpTransport.Chunk(serverCc, 0x06));
                    }
                    else
                    {
                        peer.AssertDecryptsToMessage(frame);
                        byte[] reply = WinboxStreamCrypto.Encrypt(Reply(), peer.SendAes, peer.SendHmac, ReplyIv);
                        lines.Add("server-frame " + GoldenFile.Hex(reply));
                        router.SendData(WinboxTcpTransport.Chunk(reply, 0x06));
                    }
                };
                router.Start();

                try
                {
                    typeof(WinboxMacM2Session).GetMethod("MacAuthEcSrp5", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(session, new object[] { user, Password, 5000 });
                    Assert.IsTrue(session.IsEncrypted);
                    session.Send(Message());
                    CollectionAssert.AreEqual(Reply(), session.Receive(5000), "the reply did not decrypt with the session's receive key");
                }
                catch (Exception) { router.ThrowIfFailed(); throw; }
                router.ThrowIfFailed();
                lines.Add("secret " + GoldenFile.Hex(peer.Secret));
            }
            return lines;
        }

        /// <summary>MAC-Telnet: <c>MacLayerTransport.Authenticate</c> over control packets against a loopback peer.</summary>
        /// <remarks>
        /// MAC-Telnet gets no server confirmation (the router answers <c>CTRL_END_AUTH</c>), so the peer instead says which
        /// validator name the client confirmation was built from. Today that is the whole login name, unlike both WinBox
        /// carriers, which use the account name before '+' — review item X6, pinned here as today's behaviour.
        /// </remarks>
        private static List<string> MacTelnet(string user)
        {
            var lines = new List<string>();
            var full = new RouterSide(user);
            var account = new RouterSide(EcSrp5.AccountName(user));
            using (var router = new LoopbackMacRouter())
            using (var client = new MacTelnetAuthProbe())
            {
                client.EcSrp5PrivateKeyOverride = PrivA;
                router.Attach(client, 0x0015);
                router.OnData = payload =>
                {
                    var ctrl = ParseCtrl(payload);
                    lines.Add("client-data " + GoldenFile.Hex(payload));
                    if (ctrl.Any(c => c.type == 1))   // CTRL_PASSSALT carrying the hello
                    {
                        byte[] hello = ctrl.First(c => c.type == 1).data;
                        byte[] challenge = full.Challenge(hello);
                        account.Challenge(hello);
                        lines.Add("challenge " + GoldenFile.Hex(challenge));
                        router.SendData(BuildCtrl(1, challenge));
                    }
                    else if (ctrl.Any(c => c.type == 2))   // CTRL_PASSWORD carrying the client confirmation
                    {
                        byte[] cc = ctrl.First(c => c.type == 2).data;
                        string name = full.Matches(cc) ? "full-login-name" : account.Matches(cc) ? "account-name" : "neither";
                        lines.Add("validator " + name);
                        lines.Add("expected-server-confirmation " + GoldenFile.Hex(full.Matches(cc) ? full.Confirm(cc) : account.Confirm(cc)));
                        router.SendData(BuildCtrl(9, new byte[0]));   // CTRL_END_AUTH
                    }
                };
                router.Start();
                try { client.Login(user, Password); }
                catch (Exception) { router.ThrowIfFailed(); throw; }
                router.ThrowIfFailed();
            }
            return lines;
        }

        // ── the stream cipher ─────────────────────────────────────────────────

        /// <summary>
        /// <see cref="WinboxStreamCrypto.Encrypt(byte[], byte[], byte[], byte[])"/> with a fixed IV against golden bytes,
        /// and <see cref="WinboxStreamCrypto.Decrypt"/> back, over the padding edge cases: plaintext plus HMAC an exact
        /// multiple of 16 (a whole block of padding), one byte short of it (one byte of padding), empty, and multi-block.
        /// </summary>
        [TestMethod]
        public void StreamCipherVectors_MatchTheGoldenFile()
        {
            byte[] aes = Enumerable.Range(0x10, 16).Select(i => (byte)i).ToArray();
            byte[] hmac = Enumerable.Range(0x30, 20).Select(i => (byte)i).ToArray();
            byte[] iv = Enumerable.Range(0x70, 16).Select(i => (byte)i).ToArray();
            var lines = new List<string> { "# T1 - WinboxStreamCrypto with a fixed key, HMAC key and IV",
                "aes " + GoldenFile.Hex(aes), "hmac " + GoldenFile.Hex(hmac), "iv " + GoldenFile.Hex(iv) };

            // 12 + 20 = 32 (exact multiple: a whole padding block), 11 + 20 = 31 (one byte short: one pad byte).
            foreach (int len in new[] { 0, 1, 11, 12, 13, 27, 28, 44, 300 })
            {
                byte[] msg = Enumerable.Range(0, len).Select(i => (byte)(i * 7 + 3)).ToArray();
                byte[] frame = WinboxStreamCrypto.Encrypt(msg, aes, hmac, iv);
                lines.Add("encrypt len=" + len + " " + GoldenFile.Hex(frame));
                CollectionAssert.AreEqual(msg, WinboxStreamCrypto.Decrypt(frame, aes), "decrypt of len=" + len);
            }

            byte[] sample = WinboxStreamCrypto.Encrypt(Encoding.ASCII.GetBytes("M2-golden"), aes, hmac, iv);
            byte[] wrongKey = aes.Select(b => (byte)(b ^ 0xFF)).ToArray();
            lines.Add("decrypt wrong-key " + GoldenFile.Hex(WinboxStreamCrypto.Decrypt(sample, wrongKey)));
            lines.Add("decrypt truncated " + GoldenFile.Hex(WinboxStreamCrypto.Decrypt(sample.Take(sample.Length - 1).ToArray(), aes)));
            lines.Add("decrypt header-only " + GoldenFile.Hex(WinboxStreamCrypto.Decrypt(sample.Take(18).ToArray(), aes)));
            lines.Add("decrypt null " + GoldenFile.Hex(WinboxStreamCrypto.Decrypt(null, aes)));
            // The random-IV overload: same length and the same plaintext back, never the same bytes twice.
            byte[] a = WinboxStreamCrypto.Encrypt(Encoding.ASCII.GetBytes("M2-golden"), aes, hmac);
            byte[] b = WinboxStreamCrypto.Encrypt(Encoding.ASCII.GetBytes("M2-golden"), aes, hmac);
            Assert.AreEqual(sample.Length, a.Length);
            CollectionAssert.AreNotEqual(a, b, "two frames of the random-IV overload must differ");
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("M2-golden"), WinboxStreamCrypto.Decrypt(a, aes));

            GoldenFile.AssertMatches("stream-cipher.txt", lines);
        }

        // ── the router's half ─────────────────────────────────────────────────

        /// <summary>
        /// The router's side of one EC-SRP5 exchange: holds <c>V = i·G</c> and the salt, publishes <c>wB = b·G − v</c>,
        /// reaches the shared point as <c>z = b·(j·V + wA)</c> — the other order of operations, so a client that computes
        /// something self-consistent but wrong does not match it.
        /// </summary>
        private sealed class RouterSide
        {
            private readonly string _validatorName;
            private ECPoint _bigV;
            private byte[] _xWA = new byte[0], _xWB = new byte[0], _zMont = new byte[0], _j = new byte[0];
            private int _parityA;

            internal RouterSide(string validatorName) { _validatorName = validatorName; }

            internal bool Challenged { get; private set; }
            internal byte[]? Secret { get; private set; }
            internal byte[] SendAes = new byte[0], SendHmac = new byte[0], RecvAes = new byte[0], RecvHmac = new byte[0];

            /// <summary>Takes the client's hello <c>[user]\0[xWA 32][parity]</c>, answers the 49-byte challenge.</summary>
            internal byte[] Challenge(byte[] hello)
            {
                int nul = Array.IndexOf(hello, (byte)0);
                Assert.IsTrue(nul >= 0 && hello.Length == nul + 34, "hello shape: " + GoldenFile.Hex(hello));
                _xWA = hello.Skip(nul + 1).Take(32).ToArray();
                _parityA = hello[nul + 33];

                byte[] valPriv = EcSrp5.GenPasswordValidatorPriv(_validatorName, Password, Salt);
                var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
                ECPoint v = EcSrp5.Redp1(xGamma, 1);
                _bigV = EcSrp5.ECScalarMul(EcSrp5.BEToBI(valPriv), EcSrp5.G);
                ECPoint wB = EcSrp5.ECAdd(EcSrp5.ECScalarMul(EcSrp5.BEToBI(PrivB), EcSrp5.G),
                    v.IsInfinity ? v : new ECPoint { X = v.X, Y = (EcSrp5.P - v.Y) % EcSrp5.P });
                var (xWB, parityB) = EcSrp5.ToMontgomery(wB);
                _xWB = xWB;

                _j = EcSrp5.Sha256(_xWA.Concat(_xWB).ToArray());
                ECPoint wA = EcSrp5.LiftX(EcSrp5.BEToBI(_xWA), _parityA);
                ECPoint sum = EcSrp5.ECAdd(EcSrp5.ECScalarMul(EcSrp5.BEToBI(_j), _bigV), wA);
                (_zMont, _) = EcSrp5.ToMontgomery(EcSrp5.ECScalarMul(EcSrp5.BEToBI(PrivB), sum));
                Challenged = true;
                return _xWB.Concat(new[] { (byte)parityB }).Concat(Salt).ToArray();
            }

            internal bool Matches(byte[] clientCc)
                => clientCc.SequenceEqual(EcSrp5.Sha256(_j.Concat(_zMont).ToArray()));

            /// <summary>Checks the client confirmation, answers the server confirmation and derives the keys.</summary>
            internal byte[] Confirm(byte[] clientCc)
            {
                Assert.IsTrue(Matches(clientCc), "the client's confirmation does not match the router's shared point ("
                    + _validatorName + ")");
                Secret = EcSrp5.Sha256(_zMont);
                WinboxStreamCrypto.DeriveStreamKeys(true, Secret, out SendAes, out RecvAes, out SendHmac, out RecvHmac);
                return EcSrp5.Sha256(_j.Concat(clientCc).Concat(_zMont).ToArray());
            }

            /// <summary>Decrypts the client's first encrypted frame and checks its HMAC with the router's receive key.</summary>
            internal void AssertDecryptsToMessage(byte[] frame)
            {
                byte[]? plain = WinboxStreamCrypto.Decrypt(frame, RecvAes);
                CollectionAssert.AreEqual(Message(), plain, "the client's frame did not decrypt with the router's receive key");

                // Decrypt strips the HMAC without checking it (X11), so it is checked here: the client's send-HMAC key
                // is the router's receive-HMAC key.
                byte[] padded;
                using (var a = Aes.Create())
                {
                    a.Mode = CipherMode.CBC; a.Padding = PaddingMode.None; a.Key = RecvAes; a.IV = frame.Skip(2).Take(16).ToArray();
                    using (var d = a.CreateDecryptor()) padded = d.TransformFinalBlock(frame, 18, frame.Length - 18);
                }
                byte[] mac = padded.Skip(Message().Length).Take(20).ToArray();
                using (var h = new HMACSHA1(RecvHmac))
                    CollectionAssert.AreEqual(h.ComputeHash(Message()), mac, "HMAC of the client's frame");
            }
        }

        // ── loopback plumbing ─────────────────────────────────────────────────

        private static byte[] ReadExact(Stream s, int n)
        {
            byte[] buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) throw new IOException("peer closed");
                got += r;
            }
            return buf;
        }

        private static void Write(Stream s, byte[] bytes) { s.Write(bytes, 0, bytes.Length); s.Flush(); }

        private static byte[] BuildCtrl(byte type, byte[] data)
        {
            var b = new List<byte> { 0x56, 0x34, 0x12, 0xFF, type,
                (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
            b.AddRange(data);
            return b.ToArray();
        }

        private static List<(byte type, byte[] data)> ParseCtrl(byte[] payload)
        {
            var list = new List<(byte, byte[])>();
            int pos = 0;
            while (pos + 9 <= payload.Length && payload[pos] == 0x56)
            {
                int len = (payload[pos + 5] << 24) | (payload[pos + 6] << 16) | (payload[pos + 7] << 8) | payload[pos + 8];
                list.Add((payload[pos + 4], payload.Skip(pos + 9).Take(len).ToArray()));
                pos += 9 + len;
            }
            return list;
        }

        /// <summary>Exposes MAC-Telnet's protected EC-SRP5 login over a socket the test put in place.</summary>
        private sealed class MacTelnetAuthProbe : MacLayerTransport
        {
            internal void Login(string user, string pass) => Authenticate(user, pass);
        }

        /// <summary>
        /// The router end of a MAC-layer session on loopback UDP: acknowledges SESSIONSTART and every new DATA packet,
        /// drops duplicates, and hands each new DATA payload to <see cref="OnData"/>, which answers through
        /// <see cref="SendData"/>.
        /// </summary>
        private sealed class LoopbackMacRouter : IDisposable
        {
            private readonly UdpClient _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private IPEndPoint? _client;
            private Thread? _thread;
            private volatile bool _stop;
            private uint _inCounter, _outCounter;
            private Exception? _failure;

            internal Action<byte[]> OnData = _ => { };

            /// <summary>Gives <paramref name="transport"/> a loopback socket aimed at this router, by reflection.</summary>
            internal void Attach(MacLayerTransport transport, ushort clientType)
            {
                var self = (IPEndPoint)_udp.Client.LocalEndPoint!;
                var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                _client = (IPEndPoint)udp.Client.LocalEndPoint!;
                Set(transport, "_udp", udp);
                Set(transport, "_routerEp", self);
                Set(transport, "_routerUnicastEp", self);
                Set(transport, "_localMac", ClientMac);
                Set(transport, "_routerMac", RouterMac);
                Set(transport, "_clientType", clientType);
                Set(transport, "_sessionKey", (ushort)0x1234);
            }

            private static void Set(MacLayerTransport t, string field, object value)
                => typeof(MacLayerTransport).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(t, value);

            internal void Start()
            {
                _udp.Client.ReceiveTimeout = 200;
                _thread = new Thread(Loop) { IsBackground = true, Name = "loopback MAC router" };
                _thread.Start();
            }

            private void Loop()
            {
                try
                {
                    while (!_stop)
                    {
                        byte[] pkt;
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        try { pkt = _udp.Receive(ref ep); }
                        catch (SocketException) { continue; }
                        if (pkt.Length < 22) continue;
                        byte type = pkt[1];
                        uint counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) | ((uint)pkt[20] << 8) | pkt[21];
                        byte[] payload = pkt.Skip(22).ToArray();
                        if (type == 0) Send(2, 0, null);                 // SESSIONSTART → ACK
                        else if (type == 1)
                        {
                            if (counter != _inCounter) { Send(2, _inCounter, null); continue; }   // duplicate
                            _inCounter += (uint)payload.Length;
                            Send(2, _inCounter, null);
                            OnData(payload);
                        }
                    }
                }
                catch (Exception ex) { _failure = ex; }
            }

            internal void SendData(byte[] payload)
            {
                Send(1, _outCounter, payload);
                _outCounter += (uint)payload.Length;
            }

            private void Send(byte type, uint counter, byte[]? payload)
            {
                byte[] pkt = new byte[22 + (payload?.Length ?? 0)];
                pkt[0] = 1; pkt[1] = type;
                Buffer.BlockCopy(RouterMac, 0, pkt, 2, 6);
                Buffer.BlockCopy(ClientMac, 0, pkt, 8, 6);
                pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
                pkt[20] = (byte)(counter >> 8); pkt[21] = (byte)counter;
                if (payload != null) Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
                _udp.Send(pkt, pkt.Length, _client);
            }

            internal void ThrowIfFailed()
            {
                if (_failure != null) throw new AssertFailedException("loopback router failed: " + _failure.Message, _failure);
            }

            public void Dispose()
            {
                _stop = true;
                _thread?.Join(2000);
                _udp.Close();
            }
        }
    }
}
