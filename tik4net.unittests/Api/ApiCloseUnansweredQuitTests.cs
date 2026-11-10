using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// <c>Close()</c> on a router that does not answer <c>/quit</c> must neither throw nor leave the socket behind.
    /// </summary>
    /// <remarks>
    /// <c>Close</c> asks the router to end the session with <c>/quit</c> and waits for the answer on plain API. A router
    /// that has stalled once — a single mid-print stall, before the session is presumed dead — does not answer it, and the
    /// wait ends in a receive timeout. That timeout must not escape <c>Close</c>, and above all it must not skip the dispose
    /// that follows: <c>Dispose()</c> swallows whatever <c>Close</c> throws, so through a <c>using</c> block the socket and
    /// the reader thread would stay alive with nobody holding a reference that could release them.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiCloseUnansweredQuitTests
    {
        private static Socket UnderlyingSocket(ApiConnection connection)
        {
            var field = typeof(ApiConnection).GetField("_tcpConnection", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return ((TcpClient)field.GetValue(connection)!).Client;
        }

        [TestMethod]
        public void CloseOnARouterThatDoesNotAnswerQuit_DoesNotThrowAndReleasesTheSocket()
        {
            using var server = new FakeRouterServer();
            var quitRead = new ManualResetEventSlim(false);
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); server.WriteSentence("!done");   // login
                try { server.ReadSentence(); quitRead.Set(); } catch { } // /quit — not answered (a stalled router)
                Thread.Sleep(3000);
            });

            var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(1);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            var socket = UnderlyingSocket(connection);

            Exception caught = null;
            var sw = Stopwatch.StartNew();
            try { connection.Close(); } catch (Exception ex) { caught = ex; }
            sw.Stop();

            Assert.IsTrue(quitRead.Wait(2000), "Close never sent /quit");
            Assert.IsNull(caught, "Close() threw, and so never reached the dispose: " + caught);
            Assert.IsFalse(connection.IsOpened);
            Assert.ThrowsException<ObjectDisposedException>(() => _ = socket.Available,
                "the socket outlived Close(): a router that does not answer /quit leaked it");
            Assert.IsTrue(sw.ElapsedMilliseconds < 5000, "Close took " + sw.ElapsedMilliseconds + " ms");
        }

        [TestMethod]
        public void DisposeOnARouterThatDoesNotAnswerQuit_ReleasesTheSocket()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); server.WriteSentence("!done");   // login
                try { server.ReadSentence(); } catch { }                // /quit — not answered
                Thread.Sleep(3000);
            });

            Socket socket;
            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = TimeSpan.FromSeconds(1);
                connection.Open("127.0.0.1", server.Port, "admin", "");
                socket = UnderlyingSocket(connection);
            }

            Assert.ThrowsException<ObjectDisposedException>(() => _ = socket.Available,
                "Dispose() swallowed Close's timeout and returned with the socket still open");
        }
    }
}
