// RomonWinboxProxyProbe.cs — what does WinBox say to a RoMON agent? (5.0 RoMON research)
//
// A decrypting relay between WinBox and the RoMON agent: WinBox connects here as though this were the agent, this end
// answers its EC-SRP5 login as the server would (it knows the lab account's password), logs in to the real agent with
// the account WinBox named (WinBox sends "admin+r" for a RoMON session; the relay does not need the option), and
// passes every M2 message through, logging its plain form. Nothing is changed on the way.
//
// Opt-in: TIK4NET_ROMON_PROXY=1; TIK4NET_ROMON_PROXY_PORT (default 8291) on 127.0.0.1; TIK4NET_PROBE_LOG keeps the log.
// Then in WinBox: RoMON Agent = 127.0.0.1, Connect via RoMON. The agent and the password are the App.config ones — the
// account WinBox logs in with must have that password, which is what lets this end act as the server.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using tik4net.Crypto;
using tik4net.Winbox;
using M2 = tik4net.Winbox.M2Message;
using Tcp = tik4net.Winbox.WinboxTcpTransport;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    public class RomonWinboxProxyProbe : LockedTestBase
    {
        private static readonly object LogLock = new object();
        private static readonly DateTime Start = DateTime.Now;

        private static void Log(string line)
        {
            line = $"{(DateTime.Now - Start).TotalMilliseconds,9:F1} {line}";
            lock (LogLock)
            {
                Console.WriteLine(line);
                string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
                if (!string.IsNullOrEmpty(path))
                    try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
            }
        }

        [TestMethod]
        public void Probe_Romon_WinboxProxy()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROXY") != "1")
                Assert.Inconclusive("WinBox RoMON proxy — set TIK4NET_ROMON_PROXY=1 to run.");
            int port = int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROXY_PORT"), out int p) ? p : 8291;
            int seconds = int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROXY_SECONDS"), out int s) ? s : 300;
            Run(port, seconds);
        }

        /// <summary>
        /// The proxy's own server side, checked with our client before WinBox is pointed at it: a plain login (no
        /// "+r") through the proxy must read the agent's identity.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_WinboxProxy_SelfTest()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROXY") != "1")
                Assert.Inconclusive("WinBox RoMON proxy — set TIK4NET_ROMON_PROXY=1 to run.");
            const int port = 18291;
            var server = new Thread(() => Run(port, 15)) { IsBackground = true };
            server.Start();
            Thread.Sleep(500);
            using (var s = new WinboxM2Session())
            {
                s.Open("127.0.0.1", port, LabConfig.Get("user"), LabConfig.Get("pass") ?? "", 5000, 10000);
                byte[] resp = s.SendReceive(M2.BuildM2(M2.SysToArr(13, 4), M2.SysFrom(),
                    M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                    M2.U32Sys(WinboxM2Protocol.SysKey.Command, 7)), 5000);
                Log("self-test reply: " + M2.Describe(resp));
                Assert.AreEqual(0, M2.ParseSysStatus(resp));
            }
            server.Join(20000);
        }

        private static void Run(int port, int seconds)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Log($"listening on 127.0.0.1:{port} for {seconds} s");
            var deadline = DateTime.Now.AddSeconds(seconds);
            int n = 0;
            var threads = new System.Collections.Generic.List<Thread>();
            try
            {
                while (DateTime.Now < deadline)
                {
                    if (!listener.Pending()) { Thread.Sleep(100); continue; }
                    var client = listener.AcceptTcpClient();
                    int id = ++n;
                    var t = new Thread(() => Relay(client, id)) { IsBackground = true };
                    t.Start();
                    threads.Add(t);
                }
            }
            finally { listener.Stop(); }
            foreach (var t in threads) t.Join(2000);
        }

        private static void Relay(TcpClient client, int id)
        {
            string tag = "#" + id;
            Log($"{tag} accepted {client.Client.RemoteEndPoint}");
            try
            {
                using (client)
                {
                    var ns = client.GetStream();
                    Func<int, byte[]> readExact = count =>
                    {
                        var buf = new byte[count];
                        int got = 0;
                        while (got < count)
                        {
                            int r = ns.Read(buf, got, count - got);
                            if (r <= 0) throw new System.IO.IOException("WinBox closed the connection");
                            got += r;
                        }
                        return buf;
                    };

                    // ── EC-SRP5, server side ──
                    byte[] hdr = readExact(2);
                    if (hdr[1] != 0x06) { Log($"{tag} first frame tag 0x{hdr[1]:x2} — not an EC-SRP5 hello; dropping"); return; }
                    byte[] hello = readExact(hdr[0]);
                    int zero = Array.IndexOf(hello, (byte)0);
                    string login = Encoding.UTF8.GetString(hello, 0, zero);
                    byte[] xWA = hello.Skip(zero + 1).Take(32).ToArray();
                    int parityA = hello[zero + 33];
                    Log($"{tag} WinBox hello: login '{login}' ({hello.Length} B)");

                    int plus = login.IndexOf('+');
                    string account = plus > 0 ? login.Substring(0, plus) : login;
                    string password = LabConfig.Get("pass") ?? "";

                    byte[] salt = new byte[16], privB = new byte[32];
                    using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(salt); rng.GetBytes(privB); }
                    BigInteger i = EcSrp5.BEToBI(EcSrp5.GenPasswordValidatorPriv(account, password, salt));
                    ECPoint gamma = EcSrp5.ECScalarMul(i, EcSrp5.G);
                    byte[] xGamma = EcSrp5.ToMontgomery(gamma).xMontBytes;
                    ECPoint v = EcSrp5.Redp1(xGamma, 1);
                    BigInteger b = EcSrp5.BEToBI(privB);
                    ECPoint bigB = EcSrp5.ECScalarMul(b, EcSrp5.G);
                    ECPoint wB = EcSrp5.ECAdd(bigB, new ECPoint { X = v.X, Y = (EcSrp5.P - v.Y) % EcSrp5.P });
                    var (xWB, parityB) = EcSrp5.ToMontgomery(wB);
                    byte[] challenge = xWB.Concat(new[] { (byte)parityB }).Concat(salt).ToArray();
                    ns.Write(new byte[] { (byte)challenge.Length, 0x06 }.Concat(challenge).ToArray(), 0, challenge.Length + 2);

                    hdr = readExact(2);
                    byte[] clientCc = readExact(hdr[0]);
                    byte[] j = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
                    ECPoint wA = EcSrp5.LiftX(EcSrp5.BEToBI(xWA), parityA);
                    ECPoint z = EcSrp5.ECScalarMul(b, EcSrp5.ECAdd(wA, EcSrp5.ECScalarMul(EcSrp5.BEToBI(j), gamma)));
                    byte[] zMont = EcSrp5.ToMontgomery(z).xMontBytes;
                    if (!clientCc.SequenceEqual(EcSrp5.Sha256(j.Concat(zMont).ToArray())))
                    {
                        Log($"{tag} client confirmation does not match — wrong password for '{account}' or a server-side math error");
                        return;
                    }
                    byte[] serverCc = EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray());
                    ns.Write(new byte[] { (byte)serverCc.Length, 0x06 }.Concat(serverCc).ToArray(), 0, serverCc.Length + 2);
                    WinboxStreamCrypto.DeriveStreamKeys(true, EcSrp5.Sha256(zMont),
                        out byte[] sendAes, out byte[] recvAes, out byte[] sendHmac, out byte[] recvHmac);
                    Log($"{tag} WinBox side logged in");

                    // ── the real agent, logged in with the same login name ──
                    using (var agent = new WinboxM2Session())
                    {
                        // As the account, without the login-name options: our EC-SRP5 client hashes the name it sends,
                        // and the router hashes only the part before '+'. The relay does not need "+r".
                        agent.Open(LabConfig.Get("host"), 8291, account, password, 5000, 30000);
                        Log($"{tag} agent side logged in as '{account}'");

                        var up = new Thread(() =>
                        {
                            try
                            {
                                while (true)
                                {
                                    byte[] frame = Tcp.ReadChunkedFrame(readExact, 0x06, encrypted: true);
                                    byte[] plain = WinboxStreamCrypto.Decrypt(frame, recvAes)
                                        ?? throw new InvalidOperationException("a WinBox frame did not decrypt");
                                    Log($"{tag} W>A {Describe(plain)}");
                                    agent.Send(plain);
                                }
                            }
                            catch (Exception ex) { Log($"{tag} W>A ended: {ex.GetType().Name}: {ex.Message}"); try { agent.Dispose(); } catch { } }
                        }) { IsBackground = true };
                        up.Start();

                        while (true)
                        {
                            byte[] plain = agent.ReceiveNextFrame();
                            if (plain == null) { Log($"{tag} A>W ended: agent closed"); break; }
                            Log($"{tag} A>W {Describe(plain)}");
                            byte[] enc = WinboxStreamCrypto.Encrypt(plain, sendAes, sendHmac);
                            byte[] chunked = Tcp.Chunk(enc, 0x06);
                            ns.Write(chunked, 0, chunked.Length);
                        }
                        up.Join(2000);
                    }
                }
            }
            catch (Exception ex) { Log($"{tag} ended: {ex.GetType().Name}: {ex.Message}"); }
        }

        private static string Describe(byte[] plain)
        {
            string text;
            try { text = M2.Describe(plain); } catch (Exception ex) { text = "(describe failed: " + ex.Message + ")"; }
            return $"[{plain.Length} B] {text}\n          hex {BitConverter.ToString(plain).Replace("-", " ")}";
        }
    }
}
