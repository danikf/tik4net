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

        // ── Polling vs. waiting (P2.43) ───────────────────────────────────────

        /// <summary>
        /// The P2.43 defect in one assertion. Nearly everything on this socket is control traffic, so a
        /// caller that only wants to know whether a payload has arrived must not be made to wait for one.
        /// The blocking read is the right tool for a caller that is waiting and the wrong one for a caller
        /// that is polling — and the whole 5 s per command and per open came from using the former as the
        /// latter.
        /// </summary>
        [TestMethod]
        public void Poll_OverControlTrafficOnly_ReturnsImmediatelyInsteadOfWaiting()
        {
            SendToClient(PktAck, 0, null);
            SendToClient(PktAck, 4, null);
            WaitUntilClientHasPackets(2);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool got = _client.Poll(AcceptDataOnly());
            sw.Stop();

            Assert.IsFalse(got, "no payload arrived — only ACKs");
            Assert.IsTrue(sw.ElapsedMilliseconds < 500,
                $"a poll over control traffic must return at once, took {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// The contrast that makes the test above mean something: the waiting read behaves exactly as it
        /// always did, spending its whole timeout when nothing satisfies it. Both contracts are wanted —
        /// what was wrong was which one the terminal loops were reaching for.
        /// </summary>
        [TestMethod]
        public void WaitingRead_OverControlTrafficOnly_SpendsItsWholeTimeout()
        {
            SendToClient(PktAck, 0, null);
            WaitUntilClientHasPackets(1);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.ThrowsException<TimeoutException>(() => _client.Wait(600, AcceptDataOnly()));
            sw.Stop();

            Assert.IsTrue(sw.ElapsedMilliseconds >= 500,
                $"the waiting read must honour its deadline, returned after {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// One poll drains everything already queued. If it stopped at the first packet, a payload sitting
        /// behind two ACKs would be reported as "nothing here" and only found on some later poll — which
        /// for a caller polling a terminal means output that arrives late or not at all.
        /// </summary>
        [TestMethod]
        public void Poll_ConsumesEveryQueuedPacket_NotJustTheFirst()
        {
            SendToClient(PktAck,  0, null);
            SendToClient(PktAck,  4, null);
            SendToClient(PktData, 0, Payload(3, 0xCC));
            WaitUntilClientHasPackets(3);

            int seen = 0;
            bool got = _client.Poll((type, payload, counter) => { seen++; return false; });

            Assert.IsFalse(got, "the handler never accepted anything");
            Assert.AreEqual(3, seen, "a poll must consume everything already buffered");
            Assert.AreEqual(0, _client.Available, "the socket must be drained");
        }

        /// <summary>An empty socket is an immediate no, not a wait.</summary>
        [TestMethod]
        public void Poll_EmptySocket_ReturnsFalseAtOnce()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.IsFalse(_client.Poll(AcceptDataOnly()));
            sw.Stop();
            Assert.IsTrue(sw.ElapsedMilliseconds < 200, $"took {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// A poll must handle control traffic exactly as the waiting read does — an ACK consumed by a poll
        /// still retires what it covers. Otherwise polling would quietly disable the retransmission
        /// bookkeeping for as long as the caller kept polling.
        /// </summary>
        [TestMethod]
        public void Poll_ProcessesControlTrafficRatherThanDiscardingIt()
        {
            _client.SendData(Payload(10, 0xAA));
            DrainRouter(1);

            SendToClient(PktAck, 10, null);
            WaitUntilClientHasPackets(1);

            _client.Poll((type, payload, counter) =>
            {
                if (type == PktAck) _client.Ack(counter);
                return false;
            });

            Assert.IsFalse(_client.Retransmit(), "the ACK consumed by the poll should have retired the packet");
        }

        /// <summary>
        /// An idle poll must drive retransmission, exactly as an idle wait does. This is not symmetry for
        /// its own sake: an empty socket is the "my command never landed" moment, and it means the same
        /// thing to either caller. Missing it turns a lost datagram into a caller sitting out its whole
        /// deadline and reporting "nothing was received" — a wedge that is not one, and a defect that would
        /// otherwise arrive attached to whichever read loop was moved onto the poll.
        /// </summary>
        [TestMethod]
        public void Poll_WhenIdle_StillDrivesRetransmission()
        {
            _client.SendData(Payload(10, 0xAA));
            DrainRouter(1);
            _client.Ack(0);   // the router has taken nothing

            Assert.IsFalse(_client.Poll(AcceptDataOnly()), "the socket is empty");

            var resent = ReceiveFromRouter();
            Assert.AreEqual(0u, resent.counter, "the idle poll should have resent the unacked packet");
            Assert.AreEqual(0xAA, resent.payload[0]);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private const byte PktData = 1;
        private const byte PktAck  = 2;

        private static Func<byte, byte[], uint, bool> AcceptDataOnly()
            => (type, payload, counter) => type == PktData && payload != null && payload.Length > 0;

        /// <summary>Sends a packet from the "router" to the client, with the router's MAC as source.</summary>
        private void SendToClient(byte type, uint counter, byte[] payload)
        {
            byte[] pkt = new byte[22 + (payload?.Length ?? 0)];
            pkt[0] = 1; pkt[1] = type;
            pkt[2] = 0x02; pkt[7] = 0x02;    // source MAC = the router's (02:00:00:00:00:02)
            pkt[8] = 0x02; pkt[13] = 0x01;   // destination MAC = the client's
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16);
            pkt[20] = (byte)(counter >> 8);  pkt[21] = (byte)(counter & 0xFF);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
            _router.Send(pkt, pkt.Length, _client.LocalEndPoint);
        }

        // Loopback delivery is prompt but not synchronous, and a poll that ran before the datagrams landed
        // would pass this file's timing assertions for the wrong reason.
        private void WaitUntilClientHasPackets(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_client.Available < count && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Assert.IsTrue(_client.Available >= count, "the loopback peer's packets never arrived");
        }

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

            internal IPEndPoint LocalEndPoint => (IPEndPoint)_udp.Client.LocalEndPoint;
            internal int Available => _udp.Available;
            internal bool Poll(Func<byte, byte[], uint, bool> handler) => RecvAvailable(handler);
            internal void Wait(int timeoutMs, Func<byte, byte[], uint, bool> handler)
                => RecvUntil(timeoutMs, handler);
        }
    }
}
