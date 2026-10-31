using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;
using tik4net.MacTelnet;
using tik4net.Winbox;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// A WinBox-over-MAC login the router ends (<c>PKT_END</c>) before its EC-SRP5 confirmation is a refusal whose frame
    /// was lost, and is retried like one — as MAC-Telnet's empty-screen closure is.
    /// </summary>
    /// <remarks>
    /// RouterOS refuses about one valid EC-SRP5 login in a hundred; over WinBox it answers the client's confirmation
    /// with <c>invalid user name or password (6)</c> instead of its own. Over the MAC layer that answer is one datagram,
    /// and the session's <c>PKT_END</c> follows it. Seen in Session 6b (a RoMON relay test, CLI over MAC): with the end
    /// noticed (F9) the open failed at once, not retried; before it, the open waited out the handshake timeout and failed
    /// as "no server confirmation", not retried either.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class WinboxMacLoginClosureTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestMethod]
        public async Task AnEndBeforeTheConfirmation_IsATransientRefusal()
        {
            using (var router = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var session = new WinboxMacM2Session(null))
            {
                var peer = (IPEndPoint)router.Client.LocalEndPoint;
                var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                Set(session, "_udp", udp);
                Set(session, "_routerEp", peer);
                Set(session, "_routerUnicastEp", peer);
                Set(session, "_localMac", new byte[] { 0x02, 0, 0, 0, 0, 0x01 });
                Set(session, "_routerMac", new byte[] { 0x02, 0, 0, 0, 0, 0x02 });
                var to = (IPEndPoint)udp.Client.LocalEndPoint;

                // The router side: ACK the session start, then end the session on the client's first data.
                var script = Task.Run(() =>
                {
                    router.Client.ReceiveTimeout = 3000;
                    for (int i = 0; i < 20; i++)
                    {
                        IPEndPoint ep = null;
                        byte[] pkt;
                        try { pkt = router.Receive(ref ep); } catch (SocketException) { return; }
                        byte type = pkt[1];
                        byte[] reply = new byte[22];
                        reply[0] = 1; reply[2] = 0x02; reply[7] = 0x02; reply[8] = 0x02; reply[13] = 0x01;
                        reply[16] = pkt[14]; reply[17] = pkt[15];   // the client's key, at [16] in a reply
                        reply[1] = type == 0 ? (byte)2 : (byte)255;
                        router.Send(reply, reply.Length, to);
                        if (type == 1) return;
                    }
                });

                Exception thrown = null;
                try { typeof(WinboxMacM2Session).GetMethod("MacAuthEcSrp5", Flags).Invoke(session, new object[] { "admin", "", 3000 }); }
                catch (TargetInvocationException ex) { thrown = ex.InnerException; }
                await script;

                Assert.IsNotNull(thrown, "the login must fail");
                Assert.IsTrue(TikOpenRetry.IsTransientRefusal(thrown), "retried as a refusal: " + thrown);
                Assert.IsInstanceOfType(thrown, typeof(TikConnectionSessionClosedException), "what a caller sees when it persists");
            }
        }

        private static void Set(object o, string field, object value)
            => typeof(MacLayerTransport).GetField(field, Flags).SetValue(o, value);
    }
}
