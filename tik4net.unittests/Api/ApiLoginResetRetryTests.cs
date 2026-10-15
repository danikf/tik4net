using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// An API login the router resets mid-handshake is opened again by <see cref="TikConnectionSetup"/> on a new
    /// connection — RouterOS resets a login now and then under load (seen once in a full parallel test run). A peer
    /// that hangs up in an orderly way is a protocol mismatch, as before, and is not retried.
    /// </summary>
    [TestClass]
    public class ApiLoginResetRetryTests
    {
        private static TikConnectionSetup Setup(FakeRouterServer server)
            => new TikConnectionSetup("127.0.0.1", "admin", "secret") { Port = server.Port };

        // Accepts one login and either resets it or answers it.
        private static void ServeLogin(FakeRouterServer server, bool reset)
        {
            server.AcceptClient();
            server.ReadSentence();
            if (reset)
            {
                server.ResetClientConnection();
                return;
            }
            server.WriteSentence("!done");
            server.ReadSentence();               // the /quit of the client's Dispose
            server.CloseClientConnection();
        }

        [TestMethod]
        public void ALoginTheRouterResetsIsOpenedAgain()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                ServeLogin(server, reset: true);
                ServeLogin(server, reset: false);
            });

            using (var connection = Setup(server).Create(TikConnectionType.Api))
                Assert.IsTrue(connection.IsOpened);

            Assert.IsTrue(serverTask.Wait(5000), "the second login never came");
        }

        [TestMethod]
        public async Task ALoginTheRouterResetsIsOpenedAgain_Async()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                ServeLogin(server, reset: true);
                ServeLogin(server, reset: false);
            });

            using (var connection = await Setup(server).CreateAsync(TikConnectionType.Api))
                Assert.IsTrue(connection.IsOpened);

            Assert.IsTrue(serverTask.Wait(5000), "the second login never came");
        }

        [TestMethod]
        public void ARouterThatResetsEveryLoginIsReportedAfterTheLastAttempt()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                for (int i = 0; i < TikConnectionSetup.ApiLoginResetAttempts; i++)
                    ServeLogin(server, reset: true);
            });

            Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                () => Setup(server).Create(TikConnectionType.Api).Dispose());
            Assert.IsTrue(serverTask.Wait(5000), "fewer attempts than ApiLoginResetAttempts were made");
        }

        [TestMethod]
        public void APeerThatHangsUpIsNotRetried()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();
                server.CloseClientConnection();
            });

            Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                () => Setup(server).Create(TikConnectionType.Api).Dispose());
            serverTask.Wait(5000);
            Assert.ThrowsException<TimeoutException>(() => server.AcceptClient(500),
                "an orderly close was opened again");
        }
    }
}
