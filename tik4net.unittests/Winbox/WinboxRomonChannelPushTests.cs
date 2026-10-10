// WinboxRomonChannelPushTests.cs — router-free tests for the two requests a RoMON relay sends while it opens: a frame
// the agent pushes between a request and its answer is not the answer.
//
// When the agent reaps links that other sessions left behind (WinBox's ~30 s after they closed, ours 2–3 minutes), it
// pushes their logouts — SYS_CMD 0xFE0014, SYS_FROM [0xFF0003, <their link>], no request id — to every WinBox session
// of the user (Docs/findings-romon.md, "Logouts that are not this link's"). In the 2026-10-10 release-gate run one
// arrived while Connect waited for its link id, and the open failed with "did not open a link ... and no error field
// either; the reply was ... 0xFF0002=[16711683, …] 0xFF0007=16646164" — the push, read as the reply.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRomonChannelPushTests
    {
        private const int AnotherLink = 205574;
        private const int OurLink = 77;
        private static readonly byte[] AgentId = { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };

        [DataTestMethod]
        [DataRow(false, DisplayName = "a push before the link id")]
        [DataRow(true, DisplayName = "a push before the agent's RoMON settings")]
        public void AnotherLinksLogout_ArrivingDuringTheOpen_IsNotTakenForTheReply(bool beforeSettings)
        {
            var agent = new ScriptedAgent(beforeSettings);
            var channel = new WinboxRomonChannel(agent, new RomonRelayTarget("00:11:22:33:44:55", "admin", ""));

            channel.Open("agent", 8291, "admin", "", 1000, 1000);

            Assert.AreEqual(OurLink, channel.Link, "the link id is the one in the connect's own reply");
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", channel.AgentRomonId);
            Assert.AreEqual(0, agent.Pending, "every frame the agent sent was read");
        }

        // Over the MAC layer the agent session's Receive answers null for a frame that carries no M2 data, and for a
        // receive that timed out. A null in the middle of the open is neither the answer nor the end of the wait.
        [TestMethod]
        public void ANonDataMacFrame_BeforeTheSettingsAnswer_IsReadPast()
        {
            var agent = new MacAgent(answer: true);
            var channel = new WinboxRomonChannel(agent, new RomonRelayTarget("00:11:22:33:44:55", "admin", ""));

            channel.Open("agent", 20561, "admin", "", 1000, 1000);

            Assert.AreEqual(OurLink, channel.Link);
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", channel.AgentRomonId);
        }

        // An agent that never answers timed out; it did not say RoMON is off, which is what reading its silence as a
        // settings reply with no 'enabled' field reported.
        [TestMethod]
        public void AnAgentThatNeverAnswers_IsATimeout_NotRomonSwitchedOff()
        {
            var agent = new MacAgent(answer: false);
            var channel = new WinboxRomonChannel(agent, new RomonRelayTarget("00:11:22:33:44:55", "admin", ""));

            var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                () => channel.Open("agent", 20561, "admin", "", 1000, 200));

            StringAssert.Contains(ex.Message, "RoMON settings");
            StringAssert.Contains(ex.Message, "agent");
        }

        // The agent's logout of a link somebody else left behind: no request id, and nothing in it is ours.
        private static byte[] ReapedLinkLogout()
            => M2Message.BuildM2(
                M2Message.SysToArr(),
                M2Message.U32ArraySys(WinboxM2Protocol.SysKey.From, 0xFF0003, AnotherLink),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Logout));

        /// <summary>
        /// An agent session answering the settings read and the connect, with a reaped link's logout queued in front
        /// of one of the answers — as the frames arrive on the socket.
        /// </summary>
        private sealed class ScriptedAgent : IWinboxM2Channel
        {
            private readonly bool _beforeSettings;
            private readonly Queue<byte[]> _inbound = new Queue<byte[]>();
            private byte _reqId;

            public ScriptedAgent(bool beforeSettings) { _beforeSettings = beforeSettings; }

            public int Pending => _inbound.Count;

            public byte[] SendReceive(byte[] m2, int timeoutMs)
            {
                Send(m2);
                return Receive(timeoutMs);
            }

            public void Send(byte[] m2)
            {
                int id = M2Message.ParseSysReqId(m2) ?? throw new AssertFailedException("a request without a request id");
                var reqId = M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)id);
                int[] to = M2Message.ParseU32ArrayField(m2, WinboxM2Protocol.SysKey.To)!;
                if (to[0] == 127)
                {
                    if (_beforeSettings) _inbound.Enqueue(ReapedLinkLogout());
                    _inbound.Enqueue(M2Message.BuildM2(reqId, M2Message.BoolSys(0x1, true), M2Message.RawUser(0x65, AgentId)));
                }
                else
                {
                    if (!_beforeSettings) _inbound.Enqueue(ReapedLinkLogout());
                    _inbound.Enqueue(M2Message.BuildM2(reqId, M2Message.U32Sys(0xFE0001, OurLink)));
                }
            }

            public byte[] Receive(int timeoutMs)
            {
                if (_inbound.Count > 0) return _inbound.Dequeue();
                throw new IOException("receive timed out");
            }

            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, ++_reqId);

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public bool IsEncrypted => true;
            public bool DataAvailable => _inbound.Count > 0;
            public long BytesReceived => 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool SupportsReaderLoop => false;
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }

        /// <summary>
        /// An agent session over the MAC layer: a non-data frame and a receive that timed out both come back null. With
        /// <c>answer</c> it answers the settings read (after one non-data frame) and the connect; without, nothing.
        /// </summary>
        private sealed class MacAgent : IWinboxM2Channel
        {
            private readonly bool _answer;
            private readonly Queue<byte[]> _inbound = new Queue<byte[]>();
            private byte _reqId;

            public MacAgent(bool answer) { _answer = answer; }

            public byte[] SendReceive(byte[] m2, int timeoutMs)
            {
                Send(m2);
                return Receive(timeoutMs);
            }

            public void Send(byte[] m2)
            {
                if (!_answer)
                    return;
                var reqId = M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)M2Message.ParseSysReqId(m2)!.Value);
                if (M2Message.ParseU32ArrayField(m2, WinboxM2Protocol.SysKey.To)![0] == 127)
                {
                    _inbound.Enqueue(null);
                    _inbound.Enqueue(M2Message.BuildM2(reqId, M2Message.BoolSys(0x1, true), M2Message.RawUser(0x65, AgentId)));
                }
                else
                    _inbound.Enqueue(M2Message.BuildM2(reqId, M2Message.U32Sys(0xFE0001, OurLink)));
            }

            public byte[] Receive(int timeoutMs)
            {
                if (_inbound.Count > 0) return _inbound.Dequeue()!;
                System.Threading.Thread.Sleep(Math.Min(timeoutMs, 50));
                return null!;
            }

            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, ++_reqId);

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public bool IsEncrypted => true;
            public bool DataAvailable => _inbound.Count > 0;
            public long BytesReceived => 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool SupportsReaderLoop => false;
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
