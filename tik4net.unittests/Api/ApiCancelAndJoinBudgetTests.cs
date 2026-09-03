using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// <see cref="ITikCommand.CancelAndJoin(int)"/> must come back within the budget it was given.
    /// </summary>
    /// <remarks>
    /// Cancelling is two steps: <c>/cancel</c> to the router, then a join on the reading thread. Only the
    /// second one used to be bounded, so the first could spend the connection's <c>ReceiveTimeout</c> first —
    /// a <c>CancelAndJoin(2000)</c> that throws after 30 s instead of answering after 2 s. Seen once on a
    /// live router under load (62 s, receive timeout raised from inside <c>CancelInternal</c>), which is also
    /// why the pin lives here rather than in the integration suite: reproducing it there needs a router that
    /// happens to be busy, while a scripted router that simply never answers the cancel reproduces it every
    /// time and in half a second.
    /// </remarks>
    [TestClass]
    public class ApiCancelAndJoinBudgetTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private const int ReceiveTimeoutMs = 30000;   // the budget the OLD code would have spent
        private const int CancelBudgetMs = 500;       // what the caller actually asks for
        private const int ToleranceMs = 10000;        // generous: the point is 0.5 s vs 30 s, not precision

        /// <summary>
        /// A router that streams a monitor and then goes silent on <c>/cancel</c>. That silence is the whole
        /// scenario — it is what turns "which timeout applies" from a detail into an observable 60× difference.
        /// </summary>
        private static Task RunSilentOnCancelRouter(FakeRouterServer server, ManualResetEventSlim cancelSeen)
            => Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                server.ReadSentence();                       // /interface/monitor-traffic
                // No explicit .tag word: FakeRouterServer.EchoTags puts the request's own tag on the reply,
                // and a .tag written by hand here would SUPPRESS that echo — addressing the row to nobody and
                // failing this test on its precondition rather than on what it is about.
                server.WriteSentence("!re", "=name=ether1", "=rx-bits-per-second=1000");

                server.ReadSentence();                       // /cancel — deliberately never answered
                cancelSeen.Set();
            });

        [TestMethod]
        public void CancelAndJoin_WhenTheRouterNeverAnswersTheCancel_GivesUpOnTheCallersBudgetNotTheConnectionsTimeout()
        {
            using var server = new FakeRouterServer();
            using var cancelSeen = new ManualResetEventSlim(false);
            var serverTask = RunSilentOnCancelRouter(server, cancelSeen);

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = ReceiveTimeoutMs;
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = new List<ITikReSentence>();
                var command = connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", "ether1");
                command.ExecuteWithCallback(re => { lock (rows) rows.Add(re); });

                Assert.IsTrue(SpinWait.SpinUntil(() => { lock (rows) return rows.Count > 0; }, 5000),
                    "precondition: the scripted monitor produced a row");

                var elapsed = Stopwatch.StartNew();
                try
                {
                    command.CancelAndJoin(CancelBudgetMs);
                }
                catch (TikConnectionReceiveTimeoutException)
                {
                    // Expected, and deliberately not swallowed into `false`: a router that never answers the
                    // cancel is a connection in trouble, not a command that merely did not stop in time.
                }
                elapsed.Stop();

                Assert.IsTrue(cancelSeen.Wait(5000), "precondition: the /cancel actually reached the router");
                Assert.IsTrue(elapsed.ElapsedMilliseconds < CancelBudgetMs + ToleranceMs,
                    $"CancelAndJoin({CancelBudgetMs}) took {elapsed.ElapsedMilliseconds} ms — it spent the "
                    + $"connection's ReceiveTimeout ({ReceiveTimeoutMs} ms) on the /cancel round trip instead "
                    + "of the budget it was given.");
            }
        }
    }
}
