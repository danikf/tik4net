using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>The bounded TCP connect the binary API, Telnet and the WinBox TCP carrier share.</summary>
    [TestClass]
    public class TikTcpTests
    {
        // A loopback port nothing listens on: bound to learn a free number, then released.
        private static int ClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [TestMethod]
        public void ARefusedConnect_IsTheSocketException_NotAnAggregate()
        {
            using (var tcp = new TcpClient())
            {
                var ex = Assert.ThrowsException<SocketException>(
                    () => TikTcp.Connect(tcp, IPAddress.Loopback.ToString(), ClosedPort(), 5000));
                Assert.AreEqual(SocketError.ConnectionRefused, ex.SocketErrorCode);
            }
        }

        [TestMethod]
        public async Task ARefusedConnectAsync_IsTheSocketException()
        {
            using (var tcp = new TcpClient())
            {
                var ex = await Assert.ThrowsExceptionAsync<SocketException>(
                    () => TikTcp.ConnectAsync(tcp, IPAddress.Loopback.ToString(), ClosedPort(), 5000, CancellationToken.None));
                Assert.AreEqual(SocketError.ConnectionRefused, ex.SocketErrorCode);
            }
        }

        [TestMethod]
        public async Task AListeningPort_Connects()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var tcp = new TcpClient())
                {
                    TikTcp.Connect(tcp, IPAddress.Loopback.ToString(), port, 5000);
                    Assert.IsTrue(tcp.Connected);
                }
                using (var tcp = new TcpClient())
                {
                    await TikTcp.ConnectAsync(tcp, IPAddress.Loopback.ToString(), port, 5000, CancellationToken.None);
                    Assert.IsTrue(tcp.Connected);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ATimeoutOfZeroOrLess_LeavesTheConnectToTheOs()
        {
            // ConnectTimeout <= 0 means "no bound of its own": the OS bounds the connect. Before, Wait(0) and Delay(0) gave
            // up at once, before a connect could finish.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                foreach (int timeoutMs in new[] { 0, -5, Timeout.Infinite })
                {
                    using (var tcp = new TcpClient())
                    {
                        TikTcp.Connect(tcp, IPAddress.Loopback.ToString(), port, timeoutMs);
                        Assert.IsTrue(tcp.Connected, "Connect, " + timeoutMs);
                    }
                    using (var tcp = new TcpClient())
                    {
                        await TikTcp.ConnectAsync(tcp, IPAddress.Loopback.ToString(), port, timeoutMs, CancellationToken.None);
                        Assert.IsTrue(tcp.Connected, "ConnectAsync, " + timeoutMs);
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task ACancelledConnectAsync_IsACancel_NotATimeout()
        {
            using (var cts = new CancellationTokenSource())
            using (var tcp = new TcpClient())
            {
                cts.Cancel();
                // TEST-NET-1 never answers, so only the cancel can end the wait.
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => TikTcp.ConnectAsync(tcp, "192.0.2.1", 9, 30000, cts.Token));
            }
        }
    }
}
