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
    [DoNotParallelize]   // a fake peer answers within a receive timeout
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

        /// <summary>
        /// The same contract, with the router doing what a real one does: answer <c>/quit</c> and then close
        /// the socket.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The test above leaves the socket open, so the reader loop never fails and the shutdown is
        /// classified by <c>_readerStopRequested</c>, which <c>Close()</c> sets on its way out. A real router
        /// closes right after the <c>!fatal</c>, so the reader's very NEXT read fails — and it used to do so
        /// while that flag was still false, which turned the caller's own <c>Close()</c> into
        /// <c>connection lost: IOException…</c> on every other tag. Whether the router's FIN or our own
        /// Dispose won the race decided whether the caller saw it, which is why
        /// <c>ToolTests.PingLocalhostAsyncWithCloseWillNotFail</c> failed intermittently rather than always.
        /// </para>
        /// <para>
        /// Several rounds because it IS a race: one round that happens to lose it proves nothing, and the
        /// point of the fix is that the classification no longer depends on who wins.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void ARouterClosingTheSocketAfterQuitIsStillNotReportedAsAnError()
        {
            for (int round = 1; round <= 5; round++)
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
                        server.CloseClientConnection(); // …and the router hangs up, as a real one does
                    });

                    using (var connection = new ApiConnection(false))
                    {
                        connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                        ITikTrapSentence trap = null;
                        ITikCommand command = connection.CreateCommand("/interface/listen");
                        command.ExecuteWithCallback(row => { }, error => { trap = error; }, () => { });

                        Thread.Sleep(300);              // let the monitor settle into its read
                        connection.Close();
                        Thread.Sleep(500);              // and let the reader's EOF propagate

                        Assert.IsNull(trap,
                            $"round {round}: the router closing the socket after answering /quit is part of "
                            + "an ordinary shutdown, not a lost connection — reported: " + trap?.Message);
                    }

                    Assert.IsTrue(serverTask.Wait(10000), $"round {round}: the scripted server did not finish");
                }
            }
        }

        /// <summary>
        /// A monitor that has nothing to report keeps running past <c>ReceiveTimeout</c>, as on the polled transports.
        /// </summary>
        /// <remarks>
        /// A <c>/listen</c> on a table nobody changes, or a <c>monitor-traffic</c> with a long interval, is silent for as
        /// long as it likes, and that silence is not a failure. The pump used to wait for each sentence with the
        /// connection's <c>ReceiveTimeout</c>, so such a monitor died after 30 s with "connection lost", the router went on
        /// running it, and its later replies were dropped. The pump now waits without a deadline: the caller's
        /// <c>Cancel</c>, a <c>Close</c> or a dead socket end it (the next test).
        /// </remarks>
        [TestMethod]
        public void AQuietMonitorRunsOnPastTheReceiveTimeout()
        {
            const int receiveTimeoutMs = 500;

            using (var server = new FakeRouterServer())
            {
                var cancelled = new ManualResetEventSlim(false);
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();          // the monitor command: nothing changes, so nothing is sent…
                    var cancel = server.ReadSentence();   // …until the caller cancels it
                    cancelled.Set();
                    string monitorTag = cancel.Find(w => w.StartsWith("=tag=", StringComparison.Ordinal)).Substring(5);
                    server.WriteSentence("!trap", "=category=2", "=message=interrupted", ".tag=" + monitorTag);
                    server.WriteSentence("!done", ".tag=" + monitorTag);
                    server.WriteSentence("!done");   // the /cancel's own (echoed tag)
                    try { server.ReadSentence(); } catch { }
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = TimeSpan.FromMilliseconds(receiveTimeoutMs);
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    var reported = new ManualResetEventSlim(false);
                    var done = new ManualResetEventSlim(false);
                    ITikTrapSentence trap = null;

                    ITikCommand command = connection.CreateCommand("/interface/listen");
                    command.ExecuteWithCallback(
                        row => { },
                        error => { trap = error; reported.Set(); },
                        () => done.Set());

                    Assert.IsFalse(reported.Wait(receiveTimeoutMs * 5),
                        "a monitor with nothing to report was ended after the connection's ReceiveTimeout: " + trap?.Message);
                    Assert.IsFalse(done.IsSet, "nothing ended the monitor, so nothing may report it done");
                    Assert.IsTrue(command.IsRunning, "the quiet monitor must still be running");

                    Assert.IsTrue(command.CancelAndJoin(TimeSpan.FromSeconds(5)), "the cancel did not stop it");
                    Assert.IsTrue(cancelled.IsSet, "the router was never asked to stop the monitor");
                    Assert.IsTrue(done.Wait(2000), "a cancelled monitor ends in onDone");
                    Assert.IsNull(trap, "a cancel is not an error: " + trap?.Message);
                }

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        /// <summary>
        /// Without the per-sentence deadline, a dead socket still ends the monitor and reaches its error callback.
        /// </summary>
        [TestMethod]
        public void AQuietMonitorWhoseSocketDiesReportsIt()
        {
            const int receiveTimeoutMs = 500;

            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();          // the monitor command — quiet…
                    Thread.Sleep(receiveTimeoutMs * 3);
                    server.CloseClientConnection(); // …and then the router goes away without a word
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = TimeSpan.FromMilliseconds(receiveTimeoutMs);
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    var reported = new ManualResetEventSlim(false);
                    ITikTrapSentence trap = null;
                    bool doneFired = false;

                    ITikCommand command = connection.CreateCommand("/interface/listen");
                    command.ExecuteWithCallback(
                        row => { },
                        error => { trap = error; reported.Set(); },
                        () => { doneFired = true; reported.Set(); });

                    Assert.IsTrue(reported.Wait(receiveTimeoutMs * 10),
                        "the monitor stopped without telling anyone when its connection died. A caller has no other "
                        + "way to find out, so it goes on believing it is listening.");

                    Assert.IsFalse(doneFired,
                        "a lost monitor is not a completed one: !done means the router finished the command");
                    Assert.IsNotNull(trap, "the failure must arrive as a trap, as it does on the other transports");
                    StringAssert.Contains(trap.Message, "connection lost");
                    Assert.IsNotNull(trap.CategoryCode, "ITikTrapSentence declares CategoryCode non-nullable");
                    Assert.IsFalse(command.IsRunning);
                }

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        /// <summary>
        /// A socket that dies without a FIN or a reset is what keepalive is for: with no deadline on a quiet monitor,
        /// nothing else would end it.
        /// </summary>
        [TestMethod]
        public void TheApiSocketHasTcpKeepAlive()
        {
            using (var server = new FakeRouterServer())
            {
                var serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();          // login
                    server.WriteSentence("!done");
                    server.ReadSentence();          // /quit, sent by Close()
                    server.WriteSentence("!fatal", "session terminated on request");
                });

                using (var connection = new ApiConnection(false))
                {
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                    var tcp = (System.Net.Sockets.TcpClient)typeof(ApiConnection)
                        .GetField("_tcpConnection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .GetValue(connection);
                    var keepAlive = (int)tcp.Client.GetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                        System.Net.Sockets.SocketOptionName.KeepAlive);
                    Assert.AreNotEqual(0, keepAlive, "TCP keepalive is off on the API socket");
                }
            }
        }
    }
}
