using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.MacTelnet;

namespace tik4net.unittests.MacTelnet
{
    /// <summary>
    /// Router-free coverage of the MAC layer's outbound reliability: which packet a retransmission picks,
    /// what a cumulative ACK retires, and when a send counts as abandoned.
    /// </summary>
    /// <remarks>
    /// These are the rules P2.42 had to change before the MAC channel could carry more than one request at
    /// a time, and they are exactly the rules a live run cannot pin down — the lab router does not drop
    /// packets on demand, so the interesting cases (a hole in the stream, a partial ACK, a budget spent to
    /// exhaustion) simply never occur there. The transport runs over a loopback UDP pair, so nothing here
    /// needs a router or even a network.
    /// </remarks>
    [TestClass]
    public class MacLayerRetransmitTests
    {
        private LoopbackMacTransport _client;
        private UdpClient _router;

        [TestInitialize]
        public void Init()
        {
            _router = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _router.Client.ReceiveTimeout = 2000;
            _client = new LoopbackMacTransport((IPEndPoint)_router.Client.LocalEndPoint);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _client?.Dispose();
            _router?.Close();
        }

        /// <summary>
        /// The P2.42 regression in one test. The MAC counter is a cumulative byte offset, so when the first
        /// of two packets is lost the router can acknowledge neither — and the packet that has to go again
        /// is the first one. Holding only the most recent send (as the transport did while every caller was
        /// lockstep) resends the wrong one and the stream never recovers.
        /// </summary>
        [TestMethod]
        public void Retransmit_ResendsTheOldestUnackedPacket_NotTheNewest()
        {
            _client.SendData(Payload(10, 0xAA));
            _client.SendData(Payload(10, 0xBB));
            DrainRouter(2);

            _client.Ack(0);   // the router has taken nothing: offset 0 is still the head of the stream

            Assert.IsTrue(_client.Retransmit());
            var resent = ReceiveFromRouter();
            Assert.AreEqual(0u, resent.counter, "the resend must be the packet at the head of the stream");
            Assert.AreEqual(0xAA, resent.payload[0]);
        }

        /// <summary>A cumulative ACK past both packets retires both — there is nothing left to resend.</summary>
        [TestMethod]
        public void CumulativeAck_RetiresEveryPacketBelowIt()
        {
            _client.SendData(Payload(10, 0xAA));
            _client.SendData(Payload(10, 0xBB));
            DrainRouter(2);

            _client.Ack(20);

            Assert.IsFalse(_client.Retransmit(), "nothing is outstanding");
            Assert.IsFalse(_client.Abandoned);
        }

        /// <summary>
        /// An ACK that covers only the first packet leaves the second outstanding, and the second is then
        /// what goes again.
        /// </summary>
        [TestMethod]
        public void PartialAck_LeavesTheRemainderQueued()
        {
            _client.SendData(Payload(10, 0xAA));
            _client.SendData(Payload(10, 0xBB));
            DrainRouter(2);

            _client.Ack(10);   // first packet consumed, second not

            Assert.IsTrue(_client.Retransmit());
            var resent = ReceiveFromRouter();
            Assert.AreEqual(10u, resent.counter);
            Assert.AreEqual(0xBB, resent.payload[0]);
        }

        /// <summary>
        /// Retiring the head hands the retransmission budget to the next packet rather than carrying the
        /// spent attempts over — otherwise a stream that lost one packet early would arrive at the next one
        /// with no attempts left and report it abandoned without ever having resent it.
        /// </summary>
        [TestMethod]
        public void RetiringTheHead_ResetsTheRetransmissionBudget()
        {
            _client.SendData(Payload(10, 0xAA));
            _client.SendData(Payload(10, 0xBB));
            DrainRouter(2);

            _client.Ack(0);
            ExhaustRetransmits();
            Assert.IsTrue(_client.Abandoned, "the head was resent to exhaustion and never taken");

            _client.Ack(10);   // the router finally takes the first packet
            Assert.IsFalse(_client.Abandoned, "the budget belongs to the new head, which has not been tried");
        }

        /// <summary>
        /// Nothing is abandoned until the budget is actually spent — the signal exists to tell "the router
        /// did not take our bytes" from "the router is slow", and a premature true makes a retry unsafe (P2.39).
        /// </summary>
        [TestMethod]
        public void Abandoned_OnlyAfterTheBudgetIsSpent()
        {
            _client.SendData(Payload(10, 0xAA));
            DrainRouter(1);

            Assert.IsFalse(_client.Abandoned, "no ACK has arrived at all yet");
            _client.Ack(0);
            Assert.IsFalse(_client.Abandoned, "unacknowledged, but not yet retried");

            ExhaustRetransmits();
            Assert.IsTrue(_client.Abandoned);
            Assert.IsFalse(_client.Retransmit(), "past the budget the transport must stop resending");
        }

        /// <summary>
        /// An ACK arriving while another thread is sending must not corrupt the queue. This is the shape the
        /// multiplexed MAC channel produces: the reader loop notes ACKs while callers send requests.
        /// </summary>
        [TestMethod]
        public void ConcurrentSendsAndAcks_KeepTheQueueConsistent()
        {
            const int perThread = 50;
            var sender = new Thread(() =>
            {
                for (int i = 0; i < perThread; i++) _client.SendData(Payload(4, (byte)i));
            });
            var acker = new Thread(() =>
            {
                for (int i = 0; i < perThread; i++) { _client.Ack((uint)(i * 4)); Thread.Yield(); }
            });

            sender.Start(); acker.Start();
            Assert.IsTrue(sender.Join(10000) && acker.Join(10000), "sends and ACKs deadlocked");

            // Whatever the interleaving, the queue must end up describing the stream: acking past everything
            // sent empties it, and nothing is left claiming to be unsent.
            _client.Ack((uint)(perThread * 4));
            Assert.IsFalse(_client.Retransmit());
            Assert.IsFalse(_client.Abandoned);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static byte[] Payload(int len, byte fill)
        {
            var b = new byte[len];
            for (int i = 0; i < len; i++) b[i] = fill;
            return b;
        }

        // Spends the whole retransmission budget on the current head. The transport rate-limits itself, so
        // this polls rather than sleeping a fixed total.
        private void ExhaustRetransmits()
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!_client.Abandoned && DateTime.UtcNow < deadline)
            {
                if (!_client.Retransmit()) Thread.Sleep(25);
                else DrainRouter(1);
            }
        }

        private void DrainRouter(int count)
        {
            for (int i = 0; i < count; i++) ReceiveFromRouter();
        }

        private (uint counter, byte[] payload) ReceiveFromRouter()
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            byte[] pkt = _router.Receive(ref ep);
            Assert.IsTrue(pkt.Length >= 22, "short MAC-layer datagram");
            uint counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) | ((uint)pkt[20] << 8) | pkt[21];
            byte[] payload = new byte[pkt.Length - 22];
            Buffer.BlockCopy(pkt, 22, payload, 0, payload.Length);
            return (counter, payload);
        }

        /// <summary>
        /// Drives <see cref="MacLayerTransport"/> against a loopback peer instead of a router, and exposes the
        /// protected reliability surface. Deliberately bypasses <c>BaseConnect</c>: NIC selection, MNDP and
        /// the broadcast SESSIONSTART are environment-dependent and none of them is under test here.
        /// </summary>
        private sealed class LoopbackMacTransport : MacLayerTransport
        {
            internal LoopbackMacTransport(IPEndPoint peer)
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                _routerUnicastEp = peer;
                _routerEp = peer;
                _localMac = new byte[] { 0x02, 0, 0, 0, 0, 0x01 };
                _routerMac = new byte[] { 0x02, 0, 0, 0, 0, 0x02 };
            }

            internal void SendData(byte[] payload) => Send(PKT_DATA, payload);
            internal void Ack(uint counter) => NoteAck(counter);
            internal bool Retransmit() => RetransmitIfUnacked();
            internal bool Abandoned => LastSendAbandoned;
        }
    }
}
