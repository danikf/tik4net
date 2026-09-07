using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// A callback-based monitor on the binary API must tell its caller when it stops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ExecuteWithCallback</c> gives the caller three callbacks and no other way to learn anything. The
    /// reader pump turns a per-tag receive timeout into a synthetic <c>!fatal</c> carrying the reason — the
    /// comment on it says the caller "must be told" — but the dispatcher routed a <c>!fatal</c> to
    /// <c>onTerminalCallback</c> only, and <c>ExecuteWithCallback</c> passes <c>null</c> for that hook.
    /// <c>onDoneCallback</c> fires on <c>!done</c>, <c>errorCallback</c> on <c>!trap</c>. So none of the
    /// three fired: an idle <c>/listen</c> on a quiet menu stopped after <c>ReceiveTimeout</c> and said
    /// nothing, and the caller went on believing it was listening.
    /// </para>
    /// <para>
    /// The other nine transports report the same failure as a trap through
    /// <c>PollingMonitorEngine</c>, so this is also what makes the callback contract mean the same thing on
    /// every transport.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiMonitorFailureReportingTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        /// <summary>
        /// The other side of the same contract: closing the connection yourself is not an error.
        /// </summary>
        /// <remarks>
        /// Both a lost connection and a deliberate <c>Close()</c> end a monitor with a <c>!fatal</c>, so
        /// routing every fatal to <c>errorCallback</c> reported the caller's own shutdown back to them as a
        /// failure — caught by <c>ToolTests.PingLocalhostAsyncWithCloseWillNotFail</c> in the integration
        /// suite, which closes mid-ping and asserts nothing was reported. The two are distinguished by
        /// <c>ApiFatalSentence.ClientInitiated</c>, not by the message text.
        /// </remarks>
        [TestMethod]
        public void ClosingTheConnectionUnderAMonitorIsNotReportedAsAnError()
        {
            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();          // the monitor command — left running
                    server.ReadSentence();          // /quit, sent by Close()
                    server.WriteSentence("!fatal", "session terminated on request");
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    ITikTrapSentence trap = null;
                    ITikCommand command = connection.CreateCommand("/interface/listen");
                    command.ExecuteWithCallback(row => { }, error => { trap = error; }, () => { });

                    Thread.Sleep(300);              // let the monitor settle into its read
                    connection.Close();
                    Thread.Sleep(500);              // and let the fatal propagate

                    Assert.IsNull(trap,
                        "the caller closed the connection themselves — reporting that back to their error "
                        + "callback turns an ordinary shutdown into a failure they have to filter out");
                }

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        [TestMethod]
        public void AMonitorThatTimesOutReportsItRatherThanStoppingSilently()
        {
            const int receiveTimeoutMs = 800;

            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();          // the monitor command — deliberately never answered
                    Thread.Sleep(6000);             // outlive the client's timeout without closing the socket
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = receiveTimeoutMs;
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    var reported = new ManualResetEventSlim(false);
                    ITikTrapSentence trap = null;
                    bool doneFired = false;

                    ITikCommand command = connection.CreateCommand("/interface/listen");
                    command.ExecuteWithCallback(
                        row => { },
                        error => { trap = error; reported.Set(); },
                        () => { doneFired = true; reported.Set(); });

                    // Generous: the point is that SOMETHING is reported, not how fast. A silent stop shows
                    // up here as the wait running out with all three callbacks untouched.
                    Assert.IsTrue(reported.Wait(receiveTimeoutMs * 5),
                        $"the monitor stopped without telling anyone — no callback fired within "
                        + $"{receiveTimeoutMs * 5} ms of a {receiveTimeoutMs} ms ReceiveTimeout. A caller has "
                        + "no other way to find out, so it goes on believing it is listening.");

                    Assert.IsFalse(doneFired,
                        "a timed-out monitor is not a completed one: !done means the router finished the "
                        + "command, and nothing here did");
                    Assert.IsNotNull(trap, "the failure must arrive as a trap, as it does on the other transports");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(trap.Message),
                        "an unattributed failure is barely better than a silent one");
                    Assert.IsNotNull(trap.CategoryCode, "ITikTrapSentence declares CategoryCode non-nullable");
                }

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }
    }
}
