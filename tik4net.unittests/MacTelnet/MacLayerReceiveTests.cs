using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// <see cref="MacLayerTransport.ReceiveWithin"/>, the timed receive under MAC-Telnet's pump, login and drain loops.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // timing over loopback UDP
    public class MacLayerReceiveTests
    {
        /// <summary>
        /// A datagram that arrives just as a timed receive gives up is not lost. On Windows a UDP receive under
        /// <c>SO_RCVTIMEO</c> loses one that lands as it times out (about one in ten against a 5 ms timeout on
        /// loopback), and the router then has to resend it, or never does.
        /// </summary>
        [TestMethod]
        public void ADatagramArrivingAtTheTimeout_IsNotLost()
        {
            const int count = 1000;
            using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var sender = new UdpClient();
            var target = (IPEndPoint)receiver.Client.LocalEndPoint!;

            var senderThread = new Thread(() =>
            {
                var payload = new byte[16];
                for (int i = 0; i < count; i++)
                {
                    sender.Send(payload, payload.Length, target);
                    Thread.Sleep(i % 3);   // 0–2 ms apart: many arrivals land on a receive's 5 ms timeout
                }
            }) { IsBackground = true };

            int received = 0, quietTimeouts = 0;
            senderThread.Start();
            while (quietTimeouts < 100)   // 500 ms of silence after the sender is done
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    MacLayerTransport.ReceiveWithin(receiver, 5, ref ep);
                    received++;
                    quietTimeouts = 0;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    if (!senderThread.IsAlive) quietTimeouts++;
                }
            }

            Assert.AreEqual(count, received, "datagrams lost at a receive timeout");
        }
    }
}
