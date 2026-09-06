using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// A binary-API command that times out part-way through must keep what the router already said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TikConnectionReceiveTimeoutException.PartialResponse</c> exists because a timeout that reports only
    /// "no response received" throws away the diagnosis: whether the router sent nothing, or sent half a
    /// table and stopped, are different problems with different causes, and the rows already received are
    /// what tells them apart. The five CLI transports carry it and the WinBox <c>getall</c> cursor loop
    /// carries it; the binary API was the one that discarded it — the sync path lost the rows in
    /// <c>GetAll(...).ToList()</c> and the async path left them in a local list the throw walked past.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> returned as a result. A truncated table is indistinguishable from a short
    /// one, so handing the rows back as if the command had succeeded is the silent-wrong-answer failure the
    /// property exists to avoid — see the remarks on <c>PartialResponse</c> itself.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiPartialResponseOnTimeoutTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";
        private const int ReceiveTimeoutMs = 800;

        /// <summary>Answers the login, sends two !re rows for the command, then goes quiet.</summary>
        private static Task StartHalfAnsweringServer(FakeRouterServer server)
            => Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                     // login
                server.WriteSentence("!done");
                server.ReadSentence();                     // the command
                server.WriteSentence("!re", "=name=ether1");
                server.WriteSentence("!re", "=name=ether2");
                Thread.Sleep(6000);                        // ... and never a !done
            });

        [TestMethod]
        public void CallCommandSync_KeepsTheRowsThatArrivedBeforeTheTimeout()
        {
            using (var server = new FakeRouterServer())
            {
                Task serverTask = StartHalfAnsweringServer(server);

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = ReceiveTimeoutMs;
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                        () => connection.CallCommandSync(new[] { "/interface/print" }).ToList());

                    AssertCarriesBothRows(ex);
                }

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        [TestMethod]
        public async Task CallCommandAsync_KeepsTheRowsThatArrivedBeforeTheTimeout()
        {
            using (var server = new FakeRouterServer())
            {
                Task serverTask = StartHalfAnsweringServer(server);

                using (var connection = new ApiConnection(false))
                {
                    connection.ReceiveTimeout = ReceiveTimeoutMs;
                    connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                    TikConnectionReceiveTimeoutException ex = null;
                    try
                    {
                        await connection.CallCommandAsync(new[] { "/interface/print" });
                    }
                    catch (TikConnectionReceiveTimeoutException caught)
                    {
                        ex = caught;
                    }

                    Assert.IsNotNull(ex, "the command must still fail — a partial table is not a result");
                    AssertCarriesBothRows(ex);
                }

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        private static void AssertCarriesBothRows(TikConnectionReceiveTimeoutException ex)
        {
            Assert.AreEqual(ReceiveTimeoutMs, ex.TimeoutMilliseconds, "the elapsed budget is reported");
            Assert.IsNotNull(ex.PartialResponse,
                "the router sent two !re rows before going quiet; discarding them leaves the caller unable "
                + "to tell 'the router said nothing' from 'the router stopped half way'");
            StringAssert.Contains(ex.PartialResponse, "ether1");
            StringAssert.Contains(ex.PartialResponse, "ether2");
            StringAssert.Contains(ex.Message, "2",
                "the message should say how much arrived, so the count is visible without a debugger");
        }
    }
}
