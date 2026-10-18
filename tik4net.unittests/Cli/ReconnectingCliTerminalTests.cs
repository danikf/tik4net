#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.MacTelnet;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The session rebuild MAC-Telnet and WinBox CLI MAC share (<see cref="ReconnectingCliTerminal"/>): when a command
    /// may be re-run on a fresh session after the router dropped the old one, and when it must not be.
    /// </summary>
    [TestClass]
    public class ReconnectingCliTerminalTests
    {
        private static readonly Exception Dropped = new TikConnectionSessionClosedException("the router dropped the session");

        /// <summary>A session whose answers the test scripts.</summary>
        private sealed class Session : ICliTerminal
        {
            public Func<string, Action<string>?, Task<string>> Command = (cmd, onLine) => Task.FromResult("answer of " + cmd);
            public Func<byte[], Task<string>> Raw = raw => Task.FromResult("keys");
            public bool Disposed, Ended;

            public Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
                => Command(command, onLine);
            public Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct) => Raw(raw);
            public Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct) => Raw(raw);
            public bool SupportsQuietRead => true;
            public bool SupportsLineStreaming => true;
            public void End(bool answerSafeModeQuestion) => Ended = true;
            public void Dispose() => Disposed = true;
        }

        private static Session DroppedSession() => new Session
        {
            Command = (cmd, onLine) => throw Dropped,
            Raw = raw => throw Dropped,
        };

        private static CliConnectionBase.CliTerminalOpen Opened(Session s, Func<Task>? login = null)
            => new CliConnectionBase.CliTerminalOpen(s, ct => login?.Invoke() ?? Task.FromResult(0));

        [TestMethod]
        public async Task APlainCommand_IsRebuiltOnce_AndAnswered()
        {
            var first = DroppedSession();
            var rebuilt = new List<Session>();
            var t = new ReconnectingCliTerminal(Opened(first),
                () => { var s = new Session(); rebuilt.Add(s); return Opened(s); }, () => true, "test.session");

            string answer = await t.SendCommandAndReadAsync("/x/print", null, CancellationToken.None);

            Assert.AreEqual("answer of /x/print", answer);
            Assert.AreEqual(1, rebuilt.Count);
            Assert.IsTrue(first.Disposed, "the dropped session is released");
            t.End(false);
            Assert.IsTrue(rebuilt[0].Ended, "End reaches the session in use");
        }

        [TestMethod]
        public async Task ARebuild_IsRefused_WhileMayRebuildIsFalse()
        {
            int attempts = 0;
            var t = new ReconnectingCliTerminal(Opened(DroppedSession()),
                () => { attempts++; return Opened(new Session()); }, () => false, "test.session");

            await Assert.ThrowsExceptionAsync<TikConnectionSessionClosedException>(
                () => t.SendCommandAndReadAsync("/x/print", null, CancellationToken.None));
            Assert.AreEqual(0, attempts, "with Safe Mode held the drop is the rollback; a new session would hide it");
        }

        [TestMethod]
        public async Task AStreamedRead_IsNotRebuilt_AfterItsFirstLine()
        {
            int attempts = 0;
            var first = new Session { Command = (cmd, onLine) => { onLine!("line 1"); throw Dropped; } };
            var t = new ReconnectingCliTerminal(Opened(first),
                () => { attempts++; return Opened(new Session()); }, () => true, "test.session");
            var lines = new List<string>();

            await Assert.ThrowsExceptionAsync<TikConnectionSessionClosedException>(
                () => t.SendCommandAndReadAsync("/ping", lines.Add, CancellationToken.None));
            Assert.AreEqual(0, attempts, "a re-run would deliver line 1 twice");
            CollectionAssert.AreEqual(new[] { "line 1" }, lines);
        }

        [TestMethod]
        public async Task AStreamedRead_IsRebuilt_BeforeItsFirstLine()
        {
            var t = new ReconnectingCliTerminal(Opened(DroppedSession()),
                () => Opened(new Session { Command = (cmd, onLine) => { onLine!("line 1"); return Task.FromResult("done"); } }),
                () => true, "test.session");
            var lines = new List<string>();

            Assert.AreEqual("done", await t.SendCommandAndReadAsync("/ping", lines.Add, CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "line 1" }, lines);
        }

        [TestMethod]
        public async Task Keystrokes_AreNeverRebuilt()
        {
            int attempts = 0;
            var t = new ReconnectingCliTerminal(Opened(DroppedSession()),
                () => { attempts++; return Opened(new Session()); }, () => true, "test.session");

            await Assert.ThrowsExceptionAsync<TikConnectionSessionClosedException>(
                () => t.SendRawAndReadAsync(new byte[] { 0x18 }, CancellationToken.None));
            await Assert.ThrowsExceptionAsync<TikConnectionSessionClosedException>(
                () => t.SendRawAndReadUntilQuietAsync(new byte[] { 0x09 }, 300, CancellationToken.None));
            Assert.AreEqual(0, attempts, "a key acts on a console state the new session never saw");
        }

        [TestMethod]
        public async Task ARefusedReLogin_IsRetried_AndEveryFailedAttemptsSessionIsDisposed()
        {
            var built = new List<Session>();
            var t = new ReconnectingCliTerminal(Opened(DroppedSession()),
                () =>
                {
                    var s = new Session();
                    built.Add(s);
                    // The first two re-logins are refused, as RouterOS refuses ~1 % of correct ones.
                    bool refuse = built.Count <= 2;
                    return Opened(s, () => refuse ? throw MacTelnetUdpClient.RefusalOrClosure("\r\n") : Task.FromResult(0));
                },
                () => true, "test.session");

            Assert.AreEqual("answer of /x/print", await t.SendCommandAndReadAsync("/x/print", null, CancellationToken.None));
            Assert.AreEqual(3, built.Count);
            Assert.IsTrue(built[0].Disposed && built[1].Disposed, "a refused attempt's session is not leaked");
            Assert.IsFalse(built[2].Disposed);
        }

        [TestMethod]
        public async Task Vet_RunsOnPlainAnswersOnly()
        {
            var vetted = new List<string>();
            var t = new ReconnectingCliTerminal(Opened(new Session()), () => Opened(new Session()), () => true, "test.session",
                (session, cmd, answer) => { vetted.Add(cmd); return answer + " (vetted)"; });

            Assert.AreEqual("answer of /x/print (vetted)", await t.SendCommandAndReadAsync("/x/print", null, CancellationToken.None));
            Assert.AreEqual("answer of /ping", await t.SendCommandAndReadAsync("/ping", line => { }, CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "/x/print" }, vetted);
        }

        [TestMethod]
        public async Task Login_RunsTheFirstSessionsLogin()
        {
            bool loggedIn = false;
            var t = new ReconnectingCliTerminal(Opened(new Session(), () => { loggedIn = true; return Task.FromResult(0); }),
                () => Opened(new Session()), () => true, "test.session");

            await t.Login(CancellationToken.None);

            Assert.IsTrue(loggedIn);
        }
    }
}
