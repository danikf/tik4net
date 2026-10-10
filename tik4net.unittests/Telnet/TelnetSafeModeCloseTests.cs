using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Telnet;

namespace tik4net.unittests.Telnet
{
    /// <summary>
    /// A Telnet close in Safe Mode leaves the connection open until the router has ended the session.
    /// </summary>
    /// <remarks>
    /// The close answers <c>/quit</c>'s Safe Mode question with <c>y</c>. Closing the socket in the same instant raced
    /// the console reading that <c>y</c>; a session that ended inside the question wedged RouterOS 6.49.13's console
    /// until a reboot (2 in 36 racing closes, 0 in 60 that waited). A local listener stands in for the router.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a local listener answers within a time budget
    public class TelnetSafeModeCloseTests
    {
        private static TelnetClient Connect(TcpListener listener)
        {
            var client = new TelnetClient(Encoding.ASCII, 5000, 5000);
            client.Connect(IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port, 2000);
            return client;
        }

        private static string ReadUntil(Socket socket, string expected)
        {
            var sb = new StringBuilder();
            var buffer = new byte[256];
            var sw = Stopwatch.StartNew();
            while (!sb.ToString().Contains(expected) && sw.ElapsedMilliseconds < 3000)
            {
                if (socket.Poll(100_000, SelectMode.SelectRead))
                {
                    int n = socket.Receive(buffer);
                    if (n == 0) break;
                    sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }
            }
            return sb.ToString();
        }

        // The peer's end has been closed: readable with nothing to read.
        private static bool PeerClosed(Socket socket)
            => socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;

        [TestMethod]
        public void ACloseInSafeMode_WaitsForTheRouterToEndTheSession()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = Connect(listener);
                using (Socket router = listener.AcceptSocket())
                {
                    var closing = Task.Run(() => client.Close(answerSafeModeQuestion: true));

                    StringAssert.Contains(ReadUntil(router, "/quit\r\ny"), "/quit\r\ny");
                    Thread.Sleep(400);
                    Assert.IsFalse(PeerClosed(router), "the client closed before the router had ended the session");
                    Assert.IsFalse(closing.IsCompleted);

                    router.Shutdown(SocketShutdown.Both);
                    router.Close();
                    Assert.IsTrue(closing.Wait(2000), "the close did not return once the router had ended the session");
                }
            }
            finally { listener.Stop(); }
        }

        [TestMethod]
        public void ACloseInSafeMode_GivesUpWaitingAfterItsBound()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = Connect(listener);
                using (Socket router = listener.AcceptSocket())
                {
                    var sw = Stopwatch.StartNew();
                    client.Close(answerSafeModeQuestion: true);   // the router never closes
                    Assert.IsTrue(sw.ElapsedMilliseconds < TelnetClient.SafeModeQuitWaitMs + 1500, sw.ElapsedMilliseconds + " ms");
                }
            }
            finally { listener.Stop(); }
        }

        [TestMethod]
        public void APlainClose_DoesNotWait()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = Connect(listener);
                using (Socket router = listener.AcceptSocket())
                {
                    var sw = Stopwatch.StartNew();
                    client.Close();
                    Assert.IsTrue(sw.ElapsedMilliseconds < 1000, sw.ElapsedMilliseconds + " ms");
                    StringAssert.Contains(ReadUntil(router, "/quit\r\n"), "/quit\r\n");
                }
            }
            finally { listener.Stop(); }
        }
    }
}
