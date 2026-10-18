using System;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A direct WinBox CLI login asks for a console without colours (<c>user+c</c>), as every other terminal login does;
    /// through a RoMON agent the agent's login is left as it is.
    /// </summary>
    [TestClass]
    public class WinboxCliLoginNameTests
    {
        // Records the login name the session opens the channel with, then stops the login there.
        private sealed class RecordingChannel : IWinboxM2Channel
        {
            public string User { get; private set; }

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs,
                int sendTimeoutMs = 0)
            {
                User = user;
                throw new TikConnectionLoginRefusedException("Test", "recorded");
            }

            public void Dispose() { }
            public bool IsEncrypted => false;
            public bool DataAvailable => false;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public long BytesReceived => 0;
            public bool SupportsReaderLoop => false;
            public byte[] NextReqIdField() => throw new NotSupportedException();
            public void Send(byte[] m2) => throw new NotSupportedException();
            public byte[] Receive(int timeoutMs) => throw new NotSupportedException();
            public byte[] SendReceive(byte[] m2, int timeoutMs) => throw new NotSupportedException();
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() => throw new NotSupportedException();
        }

        private static string LoginName(RomonRelayTarget target)
        {
            var channel = new RecordingChannel();
            var open = WinboxCliSession.Open(() => channel, target, Encoding.UTF8, CliClientTimeouts.Fixed(1000),
                "192.0.2.1", 8291, "admin", "", _ => { });
            try
            {
                open.Login(CancellationToken.None).GetAwaiter().GetResult();
                Assert.Fail("the recording channel stops every login");
            }
            catch (TikConnectionLoginException) { /* the recorded refusal, as itself or as the agent's */ }
            return channel.User;
        }

        [TestMethod]
        public void ADirectLogin_AsksForAConsoleWithoutColours()
            => Assert.AreEqual("admin" + RouterOsCliLogin.TerminalLoginFlags, LoginName(null));

        [TestMethod]
        public void AnAgentLogin_IsTheBareName()
            => Assert.AreEqual("admin", LoginName(new RomonRelayTarget("AA:BB:CC:DD:EE:FF", "admin", "")));
    }
}
