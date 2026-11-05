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

        // The channel of a login past its confirmation, whose router then ended the session.
        private sealed class EndedChannel : IWinboxM2Channel
        {
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public void Dispose() { }
            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public long BytesReceived => 0;
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);
            public void Send(byte[] m2) { }
            public byte[] Receive(int timeoutMs) => throw new WinboxMacSessionEndedException();
            public byte[] SendReceive(byte[] m2, int timeoutMs) => throw new NotSupportedException();
            public byte[] ReceiveNextFrame() => null;
            public void StartIdleServicing() { }
        }

        [TestMethod]
        public void AnEndWhileTheTerminalOpens_IsRetried_BecauseNoCommandHasRun()
        {
            var channel = new EndedChannel();
            var client = new tik4net.WinboxCli.WinboxCliClient(channel, System.Text.Encoding.UTF8, tik4net.Cli.CliClientTimeouts.Fixed(1000),
                "WinBox CLI MAC", tik4net.WinboxCli.WinboxCliSession.TraceChannel);
            var thrown = Assert.ThrowsException<MacLayerLoginClosedSilentlyException>(() => client.OpenTerminalSession("", "vt102", 80, 25));
            Assert.IsTrue(TikOpenRetry.IsTransientRefusal(thrown));
            StringAssert.Contains(thrown.Message, "WinBox CLI MAC", "the client names the transport it serves (X1)");
        }

        [TestMethod]
        public void TheSessionsEndIsAnIOExceptionOfItsOwnType()
        {
            using (var session = new WinboxMacM2Session(null))
            {
                typeof(WinboxMacM2Session).GetField("_routerEnded", Flags).SetValue(session, true);
                Assert.ThrowsException<WinboxMacSessionEndedException>(() => session.Receive(100));
            }
            Assert.IsInstanceOfType(new WinboxMacSessionEndedException(), typeof(System.IO.IOException));
        }

        private static void Set(object o, string field, object value)
            => typeof(MacLayerTransport).GetField(field, Flags).SetValue(o, value);
    }
}
