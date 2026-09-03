using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Pointing the plain binary API at the API-SSL port, or the reverse, is the single most reported
    /// problem in this library's issue tracker — and for years every one of those mistakes produced the same
    /// unattributable I/O error, so nobody could tell which of them they had made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two mistakes need opposite fixes, and the port that was used almost always settles which is
    /// which, so the diagnosis is worth stating outright rather than leaving to the reader.
    /// </para>
    /// <para>
    /// These tests need no router: both mistakes reduce to "the peer does not complete the handshake", and a
    /// <see cref="TcpListener"/> that accepts and then hangs up reproduces that exactly. The silent-peer case
    /// is the one that cannot be reproduced against the live lab router at all — RouterOS's plain API port
    /// sometimes closes on a ClientHello and sometimes just waits, and the waiting half is what used to hang
    /// <c>Open</c> forever, because an async TLS handshake does not honour <c>NetworkStream.ReadTimeout</c>.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ProtocolMismatchDiagnosticsTests
    {
        private const int PeerBudgetMs = 4000;

        /// <summary>
        /// A listener on a free loopback port that runs <paramref name="serve"/> for the first client.
        /// </summary>
        private sealed class FakePeer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            public int Port { get; }

            public FakePeer(Action<TcpClient, CancellationToken> serve)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                Task.Run(() =>
                {
                    try
                    {
                        using (TcpClient client = _listener.AcceptTcpClient())
                            serve(client, _cts.Token);
                    }
                    catch { /* the listener being torn down mid-accept is the normal end of this task */ }
                });
            }

            public void Dispose()
            {
                _cts.Cancel();
                try { _listener.Stop(); } catch { }
                _cts.Dispose();
            }
        }

        /// <summary>Accepts, reads whatever arrives, and hangs up — what a wrong-protocol port does.</summary>
        private static void HangUp(TcpClient client, CancellationToken ct)
        {
            var buffer = new byte[512];
            try
            {
                client.Client.ReceiveTimeout = 500;
                client.GetStream().Read(buffer, 0, buffer.Length);
            }
            catch { /* nothing arriving is fine too — the point is what happens next */ }
            client.Close();
        }

        /// <summary>Accepts and then says nothing at all, holding the socket open.</summary>
        private static void StaySilent(TcpClient client, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && client.Connected)
                Thread.Sleep(50);
        }

        // ── the two mistakes ──────────────────────────────────────────────────

        [TestMethod]
        public void PlainApiAgainstAPeerThatHangsUpNamesTheMismatchRatherThanTheIoError()
        {
            using (var peer = new FakePeer(HangUp))
            {
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "") { Port = peer.Port };

                var ex = Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                    () => setup.Create(TikConnectionType.Api),
                    "a peer that hangs up during login must be reported as a protocol mismatch");

                Assert.AreEqual(peer.Port, ex.Port, "the exception must carry the port that was used");
                Assert.IsFalse(ex.TlsAttempted, "this connection did not attempt TLS");
                StringAssert.Contains(ex.Message, "login handshake",
                    "the message must say which handshake failed");
            }
        }

        [TestMethod]
        public void TlsAgainstAPeerThatHangsUpIsAMismatchAndNotAnSslError()
        {
            using (var peer = new FakePeer(HangUp))
            {
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "")
                {
                    Port = peer.Port,
                    AllowInvalidCertificate = true,
                };

                var ex = Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                    () => setup.Create(TikConnectionType.ApiSsl),
                    "a peer that never speaks TLS is not an SSL error — there is no SSL there to go wrong");

                Assert.IsTrue(ex.TlsAttempted);
                Assert.IsInstanceOfType(ex, typeof(TikConnectionException));
                Assert.IsNotInstanceOfType(ex, typeof(TikConnectionSSLErrorException),
                    "TikConnectionSSLErrorException means TLS was negotiated and went wrong; this is the "
                    + "opposite case and the fix is the opposite too");
            }
        }

        /// <summary>
        /// The regression pin for the hang: a silent peer must cost <c>ConnectTimeout</c>, not the process.
        /// </summary>
        [TestMethod]
        public void TlsAgainstASilentPeerIsBoundedByConnectTimeout()
        {
            using (var peer = new FakePeer(StaySilent))
            {
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "")
                {
                    Port = peer.Port,
                    AllowInvalidCertificate = true,
                    ConnectTimeout = TimeSpan.FromSeconds(1),
                };

                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                    () => setup.Create(TikConnectionType.ApiSsl),
                    "an unanswered TLS handshake must end, and must say why it ended");
                sw.Stop();

                Assert.IsTrue(sw.ElapsedMilliseconds < PeerBudgetMs,
                    $"the handshake must be bounded by ConnectTimeout; took {sw.ElapsedMilliseconds} ms. "
                    + "An async TLS handshake ignores NetworkStream.ReadTimeout, so without an explicit "
                    + "bound this waits forever.");
                StringAssert.Contains(ex.Message, "never answered the TLS handshake");
            }
        }

        // ── what must NOT be swallowed ────────────────────────────────────────

        /// <summary>
        /// A router that answers the login and refuses it is not a protocol mismatch — the whole exchange
        /// worked. Catching too widely here would relabel every wrong password as a wiring problem.
        /// </summary>
        [TestMethod]
        public void ARefusedLoginStaysALoginException()
        {
            using (var peer = new FakePeer(RefuseLogin))
            {
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "wrong") { Port = peer.Port };

                var ex = Assert.ThrowsException<TikConnectionLoginException>(
                    () => setup.Create(TikConnectionType.Api),
                    "a peer that answers !trap spoke the protocol correctly — it just said no");

                StringAssert.Contains(ex.Message, "invalid user name or password");
            }
        }

        /// <summary>Speaks just enough binary API to read one sentence and answer <c>!trap</c>.</summary>
        private static void RefuseLogin(TcpClient client, CancellationToken ct)
        {
            NetworkStream stream = client.GetStream();
            client.Client.ReceiveTimeout = PeerBudgetMs;

            // Read the /login sentence: length-prefixed words until a zero-length one ends it.
            string tag = null;   // the unit-test project is not in a nullable context
            while (true)
            {
                int length = stream.ReadByte();
                if (length <= 0)
                    break;                      // 0 = end of sentence, -1 = peer gone
                var word = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int n = stream.Read(word, read, length - read);
                    if (n == 0) return;
                    read += n;
                }

                string text = System.Text.Encoding.ASCII.GetString(word);
                if (text.StartsWith(".tag=", StringComparison.Ordinal))
                    tag = text;
            }

            // The tag has to come back, or the reply belongs to nobody: replies are routed to the waiting
            // caller by it, and an untagged answer to a tagged request is simply never delivered.
            WriteWord(stream, "!trap");
            WriteWord(stream, "=message=invalid user name or password");
            if (tag != null) WriteWord(stream, tag);
            stream.WriteByte(0);

            // A !trap is followed by !done — a refusal is still a completed exchange. Without it the client
            // is right to keep waiting, and the socket closing under it really would be a lost connection.
            WriteWord(stream, "!done");
            if (tag != null) WriteWord(stream, tag);
            stream.WriteByte(0);
            stream.Flush();

            // Hold the socket open: hanging up immediately after answering would race the reader and turn a
            // legitimate refusal into a dropped connection, which is a different test.
            while (!ct.IsCancellationRequested && client.Connected)
                Thread.Sleep(50);
        }

        /// <summary>Only short words are needed here, so the single-byte length form is enough.</summary>
        private static void WriteWord(NetworkStream stream, string word)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(word);
            Assert.IsTrue(bytes.Length < 0x80, "test words use the single-byte length form");
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        // ── the advice itself ─────────────────────────────────────────────────

        /// <summary>
        /// The port is what turns "something is wrong" into "you used the wrong transport for this port",
        /// so each of the four combinations has to give its own answer.
        /// </summary>
        [TestMethod]
        public void TheAdviceFollowsThePort()
        {
            const int Plain = 8728, Ssl = 8729;

            var tlsOnPlainPort = new TikConnectionProtocolMismatchException(Plain, true, Plain, Ssl, "sym.", null);
            StringAssert.Contains(tlsOnPlainPort.Message, "TikConnectionType.Api for it",
                "TLS on 8728 means the caller wanted the plain transport");

            var plainOnSslPort = new TikConnectionProtocolMismatchException(Ssl, false, Plain, Ssl, "sym.", null);
            StringAssert.Contains(plainOnSslPort.Message, "TikConnectionType.ApiSsl for it",
                "plain words on 8729 means the caller wanted the TLS transport");

            // On a non-default port the transport cannot be inferred, so the advice turns to the service.
            var tlsElsewhere = new TikConnectionProtocolMismatchException(9999, true, Plain, Ssl, "sym.", null);
            StringAssert.Contains(tlsElsewhere.Message, "certificate",
                "api-ssl without a certificate accepts and drops exactly like a wrong port");

            var plainElsewhere = new TikConnectionProtocolMismatchException(9999, false, Plain, Ssl, "sym.", null);
            StringAssert.Contains(plainElsewhere.Message, "'api' enabled");

            foreach (var ex in new[] { tlsOnPlainPort, plainOnSslPort, tlsElsewhere, plainElsewhere })
                StringAssert.StartsWith(ex.Message, "sym.",
                    "the observed symptom leads, and the advice follows it — evidence before conclusion");
        }
    }
}
