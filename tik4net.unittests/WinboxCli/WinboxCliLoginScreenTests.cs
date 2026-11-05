using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;
using tik4net.WinboxCli;

namespace tik4net.unittests.WinboxCli
{
    /// <summary>
    /// The screen a WinBox CLI login lands on after the M2 authentication (review D2): the change-password nag is
    /// answered with Ctrl-C at most three times, and the router's refusal is reported as one.
    /// </summary>
    /// <remarks>
    /// The login waited out its whole timeout on a refusal: it never looked for one. The channel answers each
    /// keystroke the client sends with the next screen the test scripts.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class WinboxCliLoginScreenTests
    {
        private const string Nag = "\r\nChange your password\r\nnew password> ";
        private const string Prompt = "\r\n[admin@MikroTik] > ";

        private sealed class ScreenChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _toDeliver = new Queue<byte[]>();
            private readonly Queue<string> _afterCtrlC;
            internal int CtrlCs;

            internal ScreenChannel(string first, params string[] afterEachCtrlC)
            {
                _afterCtrlC = new Queue<string>(afterEachCtrlC);
                Deliver(first);
            }

            private void Deliver(string text)
                => _toDeliver.Enqueue(M2Message.BuildM2(
                    M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                    M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                    M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, Encoding.UTF8.GetBytes(text))));

            public bool IsEncrypted => true;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public bool DataAvailable => _toDeliver.Count > 0;
            public long BytesReceived => 0;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public void StartIdleServicing() { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 1);
            public void Send(byte[] m2)
            {
                byte[] keys = M2Message.ParseUserBytes(m2, WinboxM2Protocol.Mepty.Key.Input);
                if (keys != null && keys.Length == 1 && keys[0] == 0x03)
                {
                    CtrlCs++;
                    if (_afterCtrlC.Count > 0) Deliver(_afterCtrlC.Dequeue());
                }
            }
            public byte[] Receive(int timeoutMs) => _toDeliver.Count > 0 ? _toDeliver.Dequeue() : null;
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void Dispose() { }
        }

        // The prompt wait of a login whose terminal is already open.
        private static void WaitForPrompt(ScreenChannel channel, int timeoutMs = 3000, string transport = "WinBox CLI")
        {
            using (var client = new WinboxCliClient(channel, Encoding.UTF8, CliClientTimeouts.Fixed(timeoutMs, timeoutMs), transport, WinboxCliSession.TraceChannel))
            {
                try { typeof(WinboxCliClient).GetMethod("WaitForPromptSync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(client, null); }
                catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
            }
        }

        [TestMethod]
        public void ARefusal_IsReportedAsOne_AtOnce()
        {
            var sw = Stopwatch.StartNew();
            var ex = Assert.ThrowsException<TikConnectionLoginRefusedException>(
                () => WaitForPrompt(new ScreenChannel("Login failed, incorrect username or password\r\n")));
            Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
            StringAssert.Contains(ex.Message, "Login failed");
        }

        [TestMethod]
        public void ANagThatComesBackTwice_IsAnsweredThreeTimes_ThenThePrompt()
        {
            var channel = new ScreenChannel(Nag, Nag, Nag, Prompt);
            WaitForPrompt(channel);
            Assert.AreEqual(3, channel.CtrlCs);
        }

        [TestMethod]
        public void ANagThatPersists_EndsTheLogin_AfterThreeCtrlCs()
        {
            var channel = new ScreenChannel(Nag, Nag, Nag, Nag);
            var ex = Assert.ThrowsException<TikConnectionLoginException>(() => WaitForPrompt(channel));
            Assert.AreEqual(3, channel.CtrlCs, "nothing but three Ctrl-C into the prompt");
            Assert.IsNotInstanceOfType(ex, typeof(TikConnectionLoginRefusedException), "not a refusal: a retry cannot help");
            StringAssert.Contains(ex.Message, "new password");
        }

        /// <summary>The login timeout says what it was given (review T10), as MAC-Telnet's always has, and on whose carrier.</summary>
        [TestMethod]
        public void NoPrompt_TimesOut_SayingWhatWasOnScreen()
        {
            var ex = Assert.ThrowsException<TimeoutException>(
                () => WaitForPrompt(new ScreenChannel("\r\nMikroTik RouterOS banner"), timeoutMs: 300, transport: "WinBox CLI MAC"));

            StringAssert.StartsWith(ex.Message, "WinBox CLI MAC: timed out waiting for shell prompt after ");
            StringAssert.Contains(ex.Message, "1 terminal packet(s) received");
            StringAssert.Contains(ex.Message, "screen tail: \\r\\nMikroTik RouterOS banner");
        }
    }
}
