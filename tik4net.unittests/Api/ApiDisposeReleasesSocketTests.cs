using System;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// <c>Dispose</c> must release the socket even when the connection is already considered closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The happy path was never in doubt: <c>Close()</c> sends <c>/quit</c> and then disposes, and the
    /// router releases the session. What was missing is the other path. <c>Dispose()</c> did its work only
    /// <c>if (_isOpened)</c> — and a connection whose reader has faulted (router rebooted, peer hung up,
    /// cable pulled) sets that flag to <c>false</c> from the reader thread <b>without</b> touching the
    /// socket. So a caller who did everything right, in a <c>using</c>, kept the handle open until the
    /// finalizer got round to it, and the router kept seeing a connected peer.
    /// </para>
    /// <para>
    /// Idempotence matters as much as the fix: <c>Close()</c> already ends in the same teardown, so
    /// disposing twice, or closing then disposing, must not throw.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiDisposeReleasesSocketTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        /// <summary>The connection's underlying socket, captured while it is still alive.</summary>
        private static Socket UnderlyingSocket(ApiConnection connection)
        {
            var field = typeof(ApiConnection).GetField("_tcpConnection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var tcp = field.GetValue(connection) as TcpClient;
            return tcp == null ? null : tcp.Client;
        }

        /// <summary>
        /// Whether <paramref name="socket"/> has been disposed.
        /// </summary>
        /// <remarks>
        /// Asked of the SOCKET rather than of the <c>TcpClient</c>, and by using it rather than by reading a
        /// property. <c>TcpClient.Client</c> answers null after Dispose on .NET but keeps returning the
        /// socket on .NET Framework, so a check written against either one alone reports the wrong answer on
        /// the other target — the harness deciding the result instead of the subject.
        /// <c>Socket.Poll</c> throws <see cref="ObjectDisposedException"/> on both.
        /// </remarks>
        private static bool Released(Socket socket)
        {
            if (socket == null)
                return true;
            try
            {
                socket.Poll(0, SelectMode.SelectRead);
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        [TestMethod]
        public void DisposeReleasesTheSocketAfterTheReaderHasFaulted()
        {
            using (var server = new FakeRouterServer())
            {
                var loggedIn = new ManualResetEventSlim(false);
                Task serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();              // login
                    server.WriteSentence("!done");
                    loggedIn.Set();
                    Thread.Sleep(300);
                    server.CloseClientConnection();     // peer hangs up: the reader faults, _isOpened goes false
                });

                var connection = new ApiConnection(false);
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                Assert.IsTrue(loggedIn.Wait(5000));

                // Wait for the reader to notice the peer is gone.
                for (int i = 0; i < 50 && connection.IsOpened; i++)
                    Thread.Sleep(100);
                Assert.IsFalse(connection.IsOpened, "precondition: the reader has seen the peer disappear");
                Socket socket = UnderlyingSocket(connection);
                Assert.IsFalse(Released(socket),
                    "precondition: a faulted reader does not itself dispose the socket");

                connection.Dispose();

                Assert.IsTrue(Released(socket),
                    "Dispose returned without releasing the socket — a caller who used a 'using' block still "
                    + "leaks the handle, and the router still sees a connected peer");

                Assert.IsTrue(serverTask.Wait(10000));
            }
        }

        [TestMethod]
        public void DisposeIsIdempotentAndSurvivesAnEarlierClose()
        {
            using (var server = new FakeRouterServer())
            {
                Task serverTask = Task.Run(() =>
                {
                    server.AcceptClient();
                    server.ReadSentence();              // login
                    server.WriteSentence("!done");
                    server.ReadSentence();              // /quit
                    server.WriteSentence("!fatal", "session terminated on request");
                });

                var connection = new ApiConnection(false);
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                Socket socket = UnderlyingSocket(connection);
                connection.Close();
                connection.Dispose();
                connection.Dispose();

                Assert.IsTrue(Released(socket), "Close followed by two Disposes must leave the socket released");
                Assert.IsTrue(serverTask.Wait(10000));
            }
        }
    }
}
