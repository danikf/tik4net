#nullable enable
// WinboxRomonAgentNameTests.cs — a RoMON agent reached over the MAC layer by its MAC alone is named in the relay's
// messages (WinBox + MAC review, §5 X10).
//
// The WinBox relay named the agent by the host it was opened with; a MAC-layer agent given by RouterMac alone has an
// empty host, so its messages read "RoMON agent  refused the login" and "through  ended". MAC-Telnet already named
// such an agent by its MAC (MacTelnetConnection.AgentName); the relay now shares that rule.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxRomonAgentNameTests
    {
        private static TikConnectionLoginException RefusedOpen(Func<IWinboxM2Channel, WinboxRomonChannel> relayOver)
        {
            var relay = relayOver(new RefusingAgent());
            return Assert.ThrowsException<TikConnectionLoginException>(
                () => relay.Open("", 20561, "admin", "wrong", 1000, 1000));
        }

        [TestMethod]
        public void AnAgentWithNoHost_IsNeverNamedByTheEmptyString()
        {
            var ex = RefusedOpen(agent => new WinboxRomonChannel(agent, new RomonRelayTarget("00:11:22:33:44:55", "admin", "")));
            StringAssert.DoesNotMatch(ex.InnerException!.Message, new System.Text.RegularExpressions.Regex("RoMON agent  "),
                "the agent must be named, not left blank");
        }

        [TestMethod]
        public void AMacOnlyAgent_IsNamedByItsMac()
        {
            var ex = RefusedOpen(agent => new WinboxRomonChannel(agent, new RomonRelayTarget("00:11:22:33:44:55", "admin", ""),
                agentMac: "AA:BB:CC:DD:EE:FF"));
            StringAssert.Contains(ex.InnerException!.Message, "RoMON agent AA:BB:CC:DD:EE:FF refused the login");
        }

        [TestMethod]
        public void AnAgentWithAHost_IsNamedByTheHost()
        {
            Assert.AreEqual("192.168.4.1", CliConnectionBase.AgentName("192.168.4.1", "AA:BB:CC:DD:EE:FF"));
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", CliConnectionBase.AgentName("", "AA:BB:CC:DD:EE:FF"));
            Assert.AreEqual("(unnamed)", CliConnectionBase.AgentName("", null));
        }

        /// <summary>An agent carrier that refuses the login.</summary>
        private sealed class RefusingAgent : IWinboxM2Channel
        {
            public bool IsEncrypted => true;
            public bool DataAvailable => false;
            public long BytesReceived => 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0)
                => throw new TikConnectionLoginException(new Exception("Wrong username or password"));
            public byte[] NextReqIdField() => new byte[0];
            public void Send(byte[] m2) => throw new NotSupportedException();
            public byte[] Receive(int timeoutMs) => null!;
            public byte[] SendReceive(byte[] m2, int timeoutMs) => null!;
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }
        }
    }
}
