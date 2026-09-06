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
    /// <see cref="ITikConnection.ReceiveTimeout"/> on the binary API bounds the wait for the <b>next
    /// sentence</b>, not the whole command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the property that makes a large streamed read possible at all: <c>/ip/firewall/mangle/print</c>
    /// on a few thousand rules takes far longer than any sensible <c>ReceiveTimeout</c>, and it must not fail
    /// for that reason alone. <c>ApiSentenceDispatcher.Wait</c> computes a fresh deadline on every call and
    /// <c>GetAll</c> calls it once per sentence, so only a <i>gap</i> between two sentences can trip it — the
    /// total is unbounded on purpose.
    /// </para>
    /// <para>
    /// Worth pinning because the alternative reading is the intuitive one, and because the failure it
    /// produces is indistinguishable from a genuinely silent router unless something says how much arrived
    /// (see <see cref="ApiPartialResponseOnTimeoutTests"/>).
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiReceiveTimeoutIsPerSentenceTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        [TestMethod]
        public void ASlowStreamOutlastingTheTimeoutManyTimesOverStillSucceeds()
        {
            const int receiveTimeoutMs = 400;
            const int rows = 12;
            const int gapMs = 150;      // comfortably inside the budget, but 12 x 150 ms is 4.5x the budget

            using (var server = new FakeRouterServer())
            {
                Task serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();                  // login
                    server.WriteSentence("!done");
                    server.ReadSentence();                  // the command
                    for (int i = 0; i < rows; i++)
                    {
                        Thread.Sleep(gapMs);
                        server.WriteSentence("!re", "=name=ether" + i.ToString());
                    }
                    Thread.Sleep(gapMs);
                    server.WriteSentence("!done");
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = receiveTimeoutMs;
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    var sw = Stopwatch.StartNew();
                    var result = connection.CallCommandSync(new[] { "/interface/print" }).ToList();
                    sw.Stop();

                    Assert.AreEqual(rows + 1, result.Count, "every row plus the !done must come back");
                    Assert.IsTrue(sw.ElapsedMilliseconds > receiveTimeoutMs,
                        $"the read finished in {sw.ElapsedMilliseconds} ms, inside the {receiveTimeoutMs} ms "
                        + "budget — the server was not slow enough for this test to prove anything");
                }

                Assert.IsTrue(serverTask.Wait(15000));
            }
        }

        [TestMethod]
        public void AGapLongerThanTheTimeoutIsWhatActuallyFails()
        {
            // The other half of the same contract: what IS bounded is one gap. Without this the test above
            // would pass on a build that had no timeout at all.
            const int receiveTimeoutMs = 400;

            using (var server = new FakeRouterServer())
            {
                Task serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();                  // login
                    server.WriteSentence("!done");
                    server.ReadSentence();                  // the command
                    server.WriteSentence("!re", "=name=ether1");
                    Thread.Sleep(5000);                     // one gap, far longer than the budget
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = receiveTimeoutMs;
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => connection.CallCommandSync(new[] { "/interface/print" }).ToList());

                    Assert.AreEqual(receiveTimeoutMs, ex.TimeoutMilliseconds);
                    StringAssert.Contains(ex.PartialResponse ?? "", "ether1",
                        "the row that did arrive is what tells a stalled stream from a silent router");
                }

                Assert.IsTrue(serverTask.Wait(15000));
            }
        }
    }
}
