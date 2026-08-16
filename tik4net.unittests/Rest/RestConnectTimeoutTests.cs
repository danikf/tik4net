using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers <see cref="RestConnection.ConnectTimeout"/> — the REST half of P1.2, which was deferred
    /// because <c>SocketsHttpHandler.ConnectTimeout</c> does not exist on <c>netstandard2.0</c> and was
    /// closed by bounding the open probe with a <see cref="System.Threading.CancellationTokenSource"/> of
    /// its own instead.
    /// </summary>
    /// <remarks>
    /// Every test here runs against a peer that accepts the connection and then answers nothing. That is
    /// deliberate: a peer that refuses or resets fails fast by itself, so a test against one would pass
    /// whether or not any timeout is applied. The failure mode being pinned is a wait that does not end.
    /// </remarks>
    [TestClass]
    public class RestConnectTimeoutTests
    {
        private const int ShortTimeoutMs = 1000;
        // Well below the 30 s ReceiveTimeout default, well above the 1 s bound under test: an open that
        // finishes inside this was bounded by ConnectTimeout and by nothing else.
        private const int GenerousLimitMs = 10000;

        [TestMethod]
        public void OpenIsBoundedByConnectTimeoutWhenTheRouterNeverAnswers()
        {
            using (var server = new ScriptedHttpServer()) // no scripted reply: the probe stalls
            using (var conn = new RestConnection(useSsl: false))
            {
                conn.ConnectTimeout = ShortTimeoutMs;
                Assert.AreEqual(30000, conn.ReceiveTimeout, "precondition: the receive timeout is the longer one");

                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<IOException>(
                    () => conn.Open("127.0.0.1", server.Port, "admin", ""));
                sw.Stop();

                Assert.IsTrue(sw.ElapsedMilliseconds < GenerousLimitMs,
                    $"Open took {sw.ElapsedMilliseconds} ms; ConnectTimeout ({ShortTimeoutMs} ms) did not bound it.");
                StringAssert.Contains(ex.Message, "ConnectTimeout");
                var inner = ex.InnerException as TikConnectionReceiveTimeoutException;
                Assert.IsNotNull(inner, "the connect failure carries the timeout that elapsed");
                Assert.AreEqual(ShortTimeoutMs, inner.TimeoutMilliseconds);
            }
        }

        [TestMethod]
        public void TikConnectionSetupAppliesConnectTimeoutToRest()
        {
            // The property existing is not the same thing as the entry point applying it — the gap this
            // whole item is about (AGENTS.md: check what the value is applied to, not that it is read).
            using (var server = new ScriptedHttpServer())
            {
                var setup = new TikConnectionSetup("127.0.0.1", "admin", "")
                {
                    Port = server.Port,
                    ConnectTimeout = TimeSpan.FromMilliseconds(ShortTimeoutMs),
                };

                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<IOException>(() => setup.CreateRestConnection());
                sw.Stop();

                Assert.IsTrue(sw.ElapsedMilliseconds < GenerousLimitMs,
                    $"CreateRestConnection took {sw.ElapsedMilliseconds} ms; the setup's ConnectTimeout was not applied.");
                var inner = ex.InnerException as TikConnectionReceiveTimeoutException;
                Assert.IsNotNull(inner);
                Assert.AreEqual(ShortTimeoutMs, inner.TimeoutMilliseconds);
            }
        }

        [TestMethod]
        public void CommandsAfterOpenAreBoundedByReceiveTimeoutNotConnectTimeout()
        {
            // The other direction, and the reason HttpClient.Timeout is no longer set to one value for the
            // client's lifetime: the open probe and the commands after it are bounded by different settings,
            // and a swap between them would leave both tests above green.
            using (var server = new ScriptedHttpServer("{}")) // the probe is answered, the command is not
            using (var conn = new RestConnection(useSsl: false))
            {
                conn.ConnectTimeout = 120000;
                conn.SendTimeout = ShortTimeoutMs;
                conn.ReceiveTimeout = ShortTimeoutMs;
                conn.Open("127.0.0.1", server.Port, "admin", "");

                var descriptor = new TikCommandDescriptor("/ip/address/print", new List<ITikCommandParameter>());
                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => conn.RunPrint(descriptor));
                sw.Stop();

                Assert.IsTrue(sw.ElapsedMilliseconds < GenerousLimitMs,
                    $"the command took {sw.ElapsedMilliseconds} ms; ReceiveTimeout ({ShortTimeoutMs} ms) did not bound it.");
                Assert.AreEqual(ShortTimeoutMs, ex.TimeoutMilliseconds);
            }
        }
    }
}
