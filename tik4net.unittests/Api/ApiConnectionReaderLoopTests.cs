using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// Concurrency contract of <see cref="ApiConnection"/>'s sentence reader (P2.3, F8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These describe properties that the "every caller is a potential reader" model cannot have, and were
    /// written before the reader loop existed — per the P2.1 design doc, which asks for the scenarios to land
    /// <b>first</b> so the refactor has a net rather than a story.
    /// </para>
    /// <para>
    /// The protocol behaviour they build on (login, framing, tag routing, EOF→<c>!fatal</c>) is pinned by
    /// <see cref="ApiConnectionProtocolTests"/> and must keep passing unchanged.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiConnectionReaderLoopTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static string ExtractTag(System.Collections.Generic.IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        /// <summary>
        /// A caller's <see cref="ApiConnection.ReceiveTimeout"/> must be <b>its own</b> deadline, whatever
        /// else is in flight on the connection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what F8 actually costs. The starvation the finding describes does not reproduce — whoever
        /// holds the read lock reads for everyone and shelves what is not theirs, so a reply is not withheld
        /// from its caller. What is not shared is <i>time</i>: a caller blocked on the read lock has not
        /// started its own deadline yet, so its budget begins only once the previous reader gives up. Two
        /// silent commands therefore fail at 1× and 2× the timeout, and N of them at N×.
        /// </para>
        /// <para>
        /// A single reader has nothing to queue behind: every caller waits on its own registration, so every
        /// deadline runs from dispatch.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ConcurrentCallers_EachGetTheirOwnDeadline()
        {
            const int receiveTimeoutMs = 800;

            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");
                server.ReadSentence();                       // first command  — never answered
                server.ReadSentence();                       // second command — never answered
                Thread.Sleep(5000);
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = receiveTimeoutMs;
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var firstDispatched = new ManualResetEventSlim(false);
                var first = Task.Run(() =>
                {
                    firstDispatched.Set();
                    try { connection.CallCommandSync(new[] { "/one/print", $"{TikSpecialProperties.Tag}=one" }).ToList(); }
                    catch (TikConnectionReceiveTimeoutException) { /* expected */ }
                });

                Assert.IsTrue(firstDispatched.Wait(2000));
                Thread.Sleep(100);                           // let the first caller take ownership of the read

                var sw = Stopwatch.StartNew();
                Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => connection.CallCommandSync(new[] { "/two/print", $"{TikSpecialProperties.Tag}=two" }).ToList());
                sw.Stop();

                Assert.IsTrue(sw.ElapsedMilliseconds < receiveTimeoutMs * 1.6,
                    $"The second caller took {sw.ElapsedMilliseconds} ms for a {receiveTimeoutMs} ms budget: it spent " +
                    "the first caller's timeout queued behind it before its own even started (F8).");

                Assert.IsTrue(first.Wait(5000));
            }

            Assert.IsTrue(serverTask.Wait(8000));
        }

        /// <summary>
        /// When the peer disappears, <b>every</b> waiting caller must be released — not just whichever one
        /// happened to own the socket read at that moment.
        /// </summary>
        [TestMethod]
        public void PeerDisappears_ReleasesEveryWaitingCaller()
        {
            using var server = new FakeRouterServer();
            var bothDispatched = new ManualResetEventSlim(false);

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                server.ReadSentence();                       // first command — never answered
                server.ReadSentence();                       // second command — never answered
                bothDispatched.Set();
                server.CloseClientConnection();              // router vanishes with both in flight
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var first = Task.Run(() => connection.CallCommandSync(new[] { "/one/print", $"{TikSpecialProperties.Tag}=one" }).ToList());
                var second = Task.Run(() => connection.CallCommandSync(new[] { "/two/print", $"{TikSpecialProperties.Tag}=two" }).ToList());

                Assert.IsTrue(bothDispatched.Wait(5000));
                Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, 10000),
                    "A caller was left waiting for a router that is gone: the EOF reached one reader and the " +
                    "other never learned of it.");

                foreach (var result in new[] { first.Result, second.Result })
                    Assert.IsTrue(result.Any(s => s is ApiFatalSentence),
                        "A vanished peer must surface as !fatal to every caller.");
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// An idle connection must survive longer than <see cref="ApiConnection.ReceiveTimeout"/>.
        /// </summary>
        /// <remarks>
        /// The receive timeout bounds <i>a command waiting for its answer</i>, not the connection's right to
        /// exist. A reader that sits on the socket between commands must therefore not take its own idleness
        /// for a timeout — otherwise a pooled connection dies quietly after 30 s of nothing and the next
        /// command reports a dead peer that was never dead.
        /// </remarks>
        [TestMethod]
        public void IdleConnection_SurvivesTheReceiveTimeout()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var cmd = server.ReadSentence();             // arrives only after the idle period
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={ExtractTag(cmd)}", "=name=alive");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={ExtractTag(cmd)}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 300;
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                Thread.Sleep(1000);                          // > 3× the receive timeout, with no traffic

                var rows = connection.CallCommandSync(new[] { "/alive/print", $"{TikSpecialProperties.Tag}=alive" }).ToList();
                Assert.AreEqual("alive", rows.OfType<ITikReSentence>().Single().GetResponseField("name"));
                Assert.IsTrue(connection.IsOpened, "An idle connection must still be open.");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// A connection's reader must be gone once the connection is disposed.
        /// </summary>
        /// <remarks>
        /// A dedicated reader per connection is the whole design, so a reader that outlives its connection is
        /// the one leak this refactor could plausibly introduce — and it would show up not as a failure but
        /// as a suite that slowly accumulates threads. Opening and disposing many connections must therefore
        /// leave the thread count where it started.
        /// <para>
        /// Scope, measured by mutation: this catches a reader that never <i>ends</i>. It does not pin the
        /// join in <c>DisposeConnectionResources</c> — with the join removed the test still passes, because a
        /// disposed stream makes the blocked read throw straight away and the thread unwinds on its own. The
        /// join is there to make "closed" mean the reader is already gone rather than about to be.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void DisposedConnections_DoNotLeaveTheirReadersBehind()
        {
            const int cycles = 20;

            int before = Process.GetCurrentProcess().Threads.Count;

            for (int i = 0; i < cycles; i++)
            {
                using var server = new FakeRouterServer();
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();               // login
                    server.WriteSentence("!done");
                    server.ReadSentence();               // /quit
                    server.WriteSentence("!fatal", "=message=session terminated on request");
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                    connection.Close();
                }

                Assert.IsTrue(serverTask.Wait(5000));
            }

            int after = Process.GetCurrentProcess().Threads.Count;
            Assert.IsTrue(after - before < cycles / 2,
                $"Thread count went {before} → {after} over {cycles} open/dispose cycles: disposed connections " +
                "are leaving their reader behind.");
        }

        /// <summary>
        /// A command whose answer never comes still fails on the receive timeout, and says so — the bound the
        /// idle test above must not have removed.
        /// </summary>
        [TestMethod]
        public void UnansweredCommand_StillTimesOut()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");
                server.ReadSentence();                       // command — deliberately unanswered
                Thread.Sleep(3000);
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 500;
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => connection.CallCommandSync(new[] { "/silent/print", $"{TikSpecialProperties.Tag}=silent" }).ToList());
                sw.Stop();

                Assert.IsTrue(sw.ElapsedMilliseconds < 2500,
                    $"The timeout took {sw.ElapsedMilliseconds} ms for a 500 ms budget.");
            }

            Assert.IsTrue(serverTask.Wait(6000));
        }
    }
}
