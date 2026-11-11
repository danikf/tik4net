using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.WinboxCli
{
    /// <summary>
    /// The M2 trace rendering (<see cref="M2Message.Describe"/>) never prints a login credential (security review X2).
    /// </summary>
    /// <remarks>
    /// Through a RoMON agent the WinBox CLI's terminal open goes through <c>WinboxRomonChannel</c>, which traces every
    /// message it relays as a note, rendered by <c>Describe</c>, before the agent session encrypts it. Measured on the lab
    /// (CHR1 agent, CHR2 target): <c>wbx.romon &gt;&gt; (link 518: M2[74B] 0xFF0001=u32[]:[76] … 0x1=str: 0x2=str:vt102 …)</c>
    /// — key 1 of the mepty login is the target's password, printed as it was.
    /// </remarks>
    [TestClass]
    public class WinboxCliTraceRedactionTests
    {
        private const string Password = "T4n-Secret!";

        /// <summary>Records what the client sends and answers the terminal open with a session id.</summary>
        private sealed class CapturingChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _toDeliver = new Queue<byte[]>();

            internal List<byte[]> Sent { get; } = new List<byte[]>();

            public bool IsEncrypted => true;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool DataAvailable => _toDeliver.Count > 0;
            public long BytesReceived => 0;

            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public void StartIdleServicing() { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7);
            public void Send(byte[] m2)
            {
                Sent.Add(m2);
                _toDeliver.Enqueue(M2Message.BuildM2(M2Message.SysToArr(), M2Message.SysFrom(WinboxM2Protocol.Mepty.Handler[0]),
                    M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7), M2Message.SessionIdField(42)));
            }
            public byte[] Receive(int timeoutMs) => _toDeliver.Count == 0 ? null : _toDeliver.Dequeue();
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void Dispose() { }
        }

        private static byte[] TerminalOpenFrame()
        {
            var channel = new CapturingChannel();
            using (var client = new WinboxCliClient(channel, Encoding.UTF8, CliClientTimeouts.Fixed(1000, 1000), "WinBox CLI", WinboxCliSession.TraceChannel))
                client.OpenTerminalSession(Password, "vt102", 80, 25);
            return channel.Sent[0];
        }

        [TestMethod]
        public void TheTerminalOpen_IsRenderedWithoutItsPassword()
        {
            string rendered = M2Message.Describe(TerminalOpenFrame());

            Assert.IsFalse(rendered.Contains(Password), rendered);
            StringAssert.Contains(rendered, "0x1=str:<redacted>", "the field stays, so a trace still shows a password went out");
            StringAssert.Contains(rendered, "0x2=str:vt102", "only the credential is redacted");
        }

        /// <summary>The same frame routed through the agent (<c>SYS_TO</c> = proxy, link, mepty) is redacted as well.</summary>
        [TestMethod]
        public void TheTerminalOpen_RoutedThroughTheAgent_IsRenderedWithoutItsPassword()
        {
            byte[] routed = M2Message.Request(new WinboxHandler(2, 518, 76), WinboxM2Protocol.Mepty.Login,
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Password, Password));

            Assert.IsFalse(M2Message.Describe(routed).Contains(Password), M2Message.Describe(routed));
        }

        /// <summary>The RoMON link request carries the target's password in key 8 (the agent's msg-proxy, command 2001).</summary>
        [TestMethod]
        public void TheRomonLinkRequest_IsRenderedWithoutItsPassword()
        {
            byte[] connect = M2Message.Request(new WinboxHandler(2), 2001,
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7),
                M2Message.BoolSys(6, true), M2Message.StringUser(8, Password), M2Message.StringUser(7, "admin"));

            string rendered = M2Message.Describe(connect);
            Assert.IsFalse(rendered.Contains(Password), rendered);
            StringAssert.Contains(rendered, "0x7=str:admin");
        }

        /// <summary>An empty password is shown as empty: there is nothing to hide, and "none was set" is the diagnosis.</summary>
        [TestMethod]
        public void AnEmptyPassword_IsShownAsEmpty()
        {
            byte[] open = M2Message.Request(WinboxM2Protocol.Mepty.Handler, WinboxM2Protocol.Mepty.Login,
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Password, ""));

            string rendered = M2Message.Describe(open);
            StringAssert.EndsWith(rendered, "0x1=str:", rendered);
        }

        /// <summary>Key 1 of another handler is an ordinary field and keeps its value.</summary>
        [TestMethod]
        public void Key1OfAnotherHandler_KeepsItsValue()
        {
            byte[] other = M2Message.Request(new WinboxHandler(24, 2), 0x0A0065,
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 7), M2Message.StringUser(1, "ether1"));

            StringAssert.Contains(M2Message.Describe(other), "0x1=str:ether1");
        }
    }
}
