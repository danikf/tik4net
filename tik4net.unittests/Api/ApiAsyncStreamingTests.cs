using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The <see cref="IAsyncEnumerable{T}"/> form of the two bounded streaming reads (D4, carrying P2.4's
    /// deferred half), against a scripted router.
    /// </summary>
    /// <remarks>
    /// The synchronous siblings collect every row into a list and hand it over when the read ends, so a
    /// caller watching <c>/tool/torch</c> for 30 seconds sees nothing for 30 seconds. These methods exist to
    /// change exactly that, which is why the first test measures <b>when the first row arrives</b> rather
    /// than what the enumeration eventually contains — a version that buffered everything and yielded at the
    /// end would satisfy every other assertion here.
    /// </remarks>
    [TestClass]
    public class ApiAsyncStreamingTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        [TestMethod]
        public async Task RowsReachTheCallerWhileTheCommandIsStillOpen()
        {
            using var server = new FakeRouterServer();
            var firstRowObserved = new ManualResetEventSlim(false);

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");

                // The command stays open until the consumer has actually observed the first row. Were rows
                // buffered until the end, the consumer would still be blocked in the enumeration here and
                // this wait would run out instead of the test passing.
                firstRowObserved.Wait(TimeSpan.FromSeconds(10));

                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=2");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = new List<string>();
                await foreach (var row in connection.CreateCommand("/interface/monitor-traffic")
                                                    .ExecuteListUntilDoneAsync(timeoutSec: 20))
                {
                    rows.Add(row.GetResponseField("rx"));
                    firstRowObserved.Set();                  // only reachable if row 1 arrived on its own
                }

                CollectionAssert.AreEqual(new[] { "1", "2" }, rows);
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public async Task TheDurationEndsTheStreamAndCancelsTheCommandOnTheRouter()
        {
            using var server = new FakeRouterServer();
            List<string> cancelSentence = null;

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");

                // A torch-like command the router never ends by itself: the duration has to end it, and the
                // client has to say so with /cancel, which is what keeps the connection usable afterwards.
                cancelSentence = server.ReadSentence();
                server.WriteSentence("!done");   // the /cancel command's own reply (tag echoed)
                server.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=category=2", "=message=interrupted");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                var rows = new List<ITikReSentence>();
                await foreach (var row in connection.CreateCommand("/tool/torch").ExecuteListWithDurationAsync(1))
                    rows.Add(row);
                sw.Stop();

                Assert.AreEqual(1, rows.Count);
                Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(10),
                    $"the 1 s duration should have ended the read; took {sw.ElapsedMilliseconds} ms");
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
            Assert.IsNotNull(cancelSentence);
            Assert.AreEqual("/cancel", cancelSentence[0]);
        }

        [TestMethod]
        public async Task ARouterErrorIsThrownOutOfTheEnumerationAfterTheRowsItAlreadyGave()
        {
            const string RouterMessage = "input does not match any value of interface";

            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");
                server.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=message=" + RouterMessage);
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = new List<ITikReSentence>();
                var ex = await Assert.ThrowsExceptionAsync<TikCommandAbortException>(async () =>
                {
                    await foreach (var row in connection.CreateCommand("/tool/torch").ExecuteListUntilDoneAsync(20))
                        rows.Add(row);
                });

                // Both halves matter: the row the router did send is delivered, and the read still fails —
                // a truncated stream must not be readable as a complete one.
                Assert.AreEqual(1, rows.Count);
                StringAssert.Contains(ex.Message, RouterMessage);
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public async Task StreamingIsRefusedOnATransportThatDoesNotDeclareIt()
        {
            // Fail-closed, like every other capability-gated surface: the CLI family has no way to deliver
            // several rows inside one command exchange, so asking for it is an error rather than an empty read.
            using var fake = new tik4net.Testing.TikFakeConnection();
            var command = fake.CreateCommand("/tool/torch");

            await Assert.ThrowsExceptionAsync<TikConnectionCapabilityNotSupportedException>(async () =>
            {
                await foreach (var unused in command.ExecuteListUntilDoneAsync())
                {
                }
            });
        }
    }
}
