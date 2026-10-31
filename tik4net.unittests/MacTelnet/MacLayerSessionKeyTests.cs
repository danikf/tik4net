using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.MacTelnet;
using tik4net.Winbox;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// A MAC-layer session takes only the packets that carry its own session key (review F9).
    /// </summary>
    /// <remarks>
    /// The socket is bound for broadcast, so it can see another session's traffic: another MikroTik's on the segment,
    /// or a session of ours that used the same ephemeral port before. The key was checked only while the session
    /// opened; afterwards such a packet's data was appended and acknowledged, and a <c>PKT_END</c> ended the session.
    /// Each test drives a real transport over a loopback UDP pair, with the router's side scripted here.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class MacLayerSessionKeyTests
    {
        private const ushort OurKey = 0x1234;
        private const ushort OtherKey = 0x4321;
        private const byte PktData = 1, PktEnd = 255;

        private UdpClient _router;

        [TestInitialize]
        public void Init()
        {
            _router = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _router.Client.ReceiveTimeout = 2000;
        }

        [TestCleanup]
        public void Cleanup() => _router?.Close();

        [TestMethod]
        public void MacTelnet_APktEndWithAnotherSessionKey_DoesNotEndTheSession()
        {
            using (var client = new MacTelnetUdpClient(Encoding.UTF8, CliClientTimeouts.Fixed(2000, 2000), null))
            {
                Wire(client);
                Invoke(client, "StartPump");

                SendToClient(client, PktEnd, OtherKey, 0, null);
                Thread.Sleep(600);
                Assert.IsFalse(RouterEnded(client), "another session's end is not this session's");

                SendToClient(client, PktEnd, OurKey, 0, null);
                WaitUntil(() => RouterEnded(client));
                Assert.IsTrue(RouterEnded(client), "positive control: our own key ends it");
            }
        }

        [TestMethod]
        public void MacTelnet_DataWithAnotherSessionKey_IsNeitherTakenNorAcknowledged()
        {
            using (var client = new MacTelnetUdpClient(Encoding.UTF8, CliClientTimeouts.Fixed(2000, 2000), null))
            {
                Wire(client);
                Invoke(client, "StartPump");

                SendToClient(client, PktData, OtherKey, 0, Encoding.UTF8.GetBytes("foreign"));
                _router.Client.ReceiveTimeout = 600;
                IPEndPoint ep = null;
                Assert.ThrowsException<SocketException>(() => _router.Receive(ref ep), "no ACK for another session's data");
                Assert.AreEqual(0u, (uint)typeof(MacLayerTransport).GetField("_inCounter", Flags).GetValue(client),
                    "the stream counter did not move");
            }
        }

        [TestMethod]
        public void WinboxMac_APktEndWithAnotherSessionKey_IsNotAnEnd()
        {
            using (var session = new WinboxMacM2Session(null))
            {
                Wire(session);
                SendToClient(session, PktEnd, OtherKey, 0, null);
                Assert.IsNull(session.Receive(400), "nothing of this session arrived");
            }
        }

        /// <summary>
        /// The WinBox-MAC carrier did not look at <c>PKT_END</c> at all: a session the router ended was read on to
        /// the deadline. Under the receive contract (review D9) an ended session is a broken stream and throws, as TCP does
        /// on the router's FIN.
        /// </summary>
        [TestMethod]
        public void WinboxMac_APktEndWithOurSessionKey_EndsTheSession_AtOnce()
        {
            using (var session = new WinboxMacM2Session(null))
            {
                Wire(session);
                SendToClient(session, PktEnd, OurKey, 0, null);
                var sw = Stopwatch.StartNew();
                Assert.ThrowsException<System.IO.IOException>(() => session.Receive(3000));
                Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
            }
        }

        // ── harness ──

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        // What BaseConnect would leave behind, minus NIC selection and the SESSIONSTART race.
        private void Wire(MacLayerTransport t)
        {
            var peer = (IPEndPoint)_router.Client.LocalEndPoint;
            Set(t, "_udp", new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));
            Set(t, "_routerEp", peer);
            Set(t, "_routerUnicastEp", peer);
            Set(t, "_localMac", new byte[] { 0x02, 0, 0, 0, 0, 0x01 });
            Set(t, "_routerMac", new byte[] { 0x02, 0, 0, 0, 0, 0x02 });
            Set(t, "_sessionKey", OurKey);
        }

        private static void Set(object o, string field, object value)
            => typeof(MacLayerTransport).GetField(field, Flags).SetValue(o, value);

        private static void Invoke(object o, string method)
            => o.GetType().GetMethod(method, Flags).Invoke(o, null);

        private static bool RouterEnded(MacTelnetUdpClient c)
            => (bool)typeof(MacTelnetUdpClient).GetField("_routerEnded", Flags).GetValue(c);

        private static void WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
        }

        // A packet as the router sends it: its MAC as source, ours as destination, the session key at [16].
        private void SendToClient(MacLayerTransport t, byte type, ushort key, uint counter, byte[] payload)
        {
            byte[] pkt = new byte[22 + (payload?.Length ?? 0)];
            pkt[0] = 1; pkt[1] = type;
            pkt[2] = 0x02; pkt[7] = 0x02;
            pkt[8] = 0x02; pkt[13] = 0x01;
            pkt[16] = (byte)(key >> 8); pkt[17] = (byte)key;
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)counter;
            if (payload != null) Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
            var udp = (UdpClient)typeof(MacLayerTransport).GetField("_udp", Flags).GetValue(t);
            _router.Send(pkt, pkt.Length, (IPEndPoint)udp.Client.LocalEndPoint);
        }
    }
}
