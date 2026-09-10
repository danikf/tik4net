using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// A failure on the client's own reader thread must be reported as the client's, not as the router's.
    /// </summary>
    /// <remarks>
    /// From a field report: on .NET Framework, the reader's first word needed <c>System.Buffers</c>, the
    /// assembly failed to bind, and <c>Open</c> answered "The router closed the connection during the API login
    /// handshake … without answering", with advice about <c>/ip/service</c>. <see cref="FileLoadException"/>
    /// derives from <see cref="IOException"/>, and "any IOException" was how a peer hanging up was recognised.
    /// The original exception did not survive either, so the binding details that would have diagnosed it were
    /// gone. Here the fake router answers the login correctly and the fault is thrown on the reader thread by
    /// an <c>OnReadRow</c> handler, which runs exactly where the failed assembly load did.
    /// </remarks>
    [TestClass]
    public class ApiReaderClientFaultTests
    {
        private static Task AnswerLogin(FakeRouterServer server) => Task.Run(() =>
        {
            try
            {
                server.AcceptClient();
                server.ReadSentence();          // /login
                server.WriteSentence("!done");  // a correct, successful answer
            }
            catch { /* the client tearing the connection down is the expected end */ }
        });

        [TestMethod]
        public void AnAssemblyThatFailsToLoadOnTheReaderIsNotReportedAsTheRouterHangingUp()
        {
            using var server = new FakeRouterServer();
            var serverTask = AnswerLogin(server);

            using (var connection = new ApiConnection(false))
            {
                connection.OnReadRow += (s, e) =>
                    throw new FileLoadException("Could not load file or assembly 'Simulated.Assembly'.");

                Exception ex = Assert.ThrowsException<TikCommandFatalException>(
                    () => connection.Open("127.0.0.1", server.Port, "admin", "secret"));

                Assert.IsNotInstanceOfType(ex, typeof(TikConnectionProtocolMismatchException));
                StringAssert.Contains(ex.Message, "on this machine");
                StringAssert.Contains(ex.Message, "FileLoadException");
                Assert.IsFalse(ex.Message.Contains("/ip/service"), ex.Message);
                Assert.IsInstanceOfType(ex.InnerException, typeof(FileLoadException),
                    "the original exception is what carries the binding details — it must not be flattened away");
            }

            serverTask.Wait(5000);
        }

        [TestMethod]
        public void ARouterThatReallyHangsUpDuringLoginIsStillAProtocolMismatch()
        {
            // The control: the same path with a genuine EOF must keep its diagnosis.
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();
                server.CloseClientConnection();
            });

            using (var connection = new ApiConnection(false))
            {
                Assert.ThrowsException<TikConnectionProtocolMismatchException>(
                    () => connection.Open("127.0.0.1", server.Port, "admin", "secret"));
            }

            serverTask.Wait(5000);
        }

        [TestMethod]
        public void ANetworkFailureIsOnlyWhatTheStreamItselfRaises()
        {
            Assert.IsTrue(ApiConnection.IsNetworkFailure(new IOException("Connection reset")));
            Assert.IsTrue(ApiConnection.IsNetworkFailure(new EndOfStreamException()));
            Assert.IsTrue(ApiConnection.IsNetworkFailure(
                new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset)));

            // Every one of these is an IOException, and none of them is the network.
            Assert.IsFalse(ApiConnection.IsNetworkFailure(new FileLoadException("x")));
            Assert.IsFalse(ApiConnection.IsNetworkFailure(new FileNotFoundException("x")));
            Assert.IsFalse(ApiConnection.IsNetworkFailure(new DirectoryNotFoundException("x")));
            Assert.IsFalse(ApiConnection.IsNetworkFailure(new NullReferenceException()));
        }
    }
}
