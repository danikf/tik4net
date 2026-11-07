#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The MAC-layer terminals rebuild a session the router dropped, for its idle logout. A running
    /// <c>ExecuteWithCallback</c> keeps the session busy, so a drop under it is not that logout: it is a lost connection,
    /// reported to <c>errorCallback</c> (D21), not survived on a new session.
    /// </summary>
    [TestClass]
    public class SessionRebuildUnderCallbackTests
    {
        private sealed class Router : ScriptedCliConnectionBase
        {
            // Set by the test: from then on the first session answers as one the router dropped (WinBox over MAC: silence,
            // reported as TikConnectionSessionClosedException).
            internal volatile bool Dropped;
            internal int Rebuilds;

            internal Router()
            {
                CliFieldSeparator = null;
                OpenTerminal(() =>
                {
                    var terminal = new ReconnectingCliTerminal(Opened(Session(dropping: true)),
                        () => { Interlocked.Increment(ref Rebuilds); return Opened(Session(dropping: false)); },
                        () => SessionRebuildAllowed, "test.session");
                    return new CliTerminalOpen(terminal, terminal.Login);
                }, retryRefusedLogin: false);
            }

            private ScriptedCliTerminal Session(bool dropping) => new ScriptedCliTerminal(
                (line, ct) => dropping && Dropped
                    ? throw new TikConnectionSessionClosedException("the router dropped the session")
                    : Task.FromResult(EmptyTable(line)),
                (raw, ct) => Task.FromResult(string.Empty), () => { });

            // An empty table, counted the way the read asked for it: a windowed read's "#w=", a whole print's "#n=".
            private static string EmptyTable(string line)
                => line.Contains("\"" + CliCommandBuilder.WindowMarker + "\"") ? CliCommandBuilder.WindowMarker + "0"
                 : CliCommandBuilder.CountMarker + "0/nil";

            private static CliTerminalOpen Opened(ScriptedCliTerminal session)
                => new CliTerminalOpen(session, _ => Task.FromResult(0));

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }

        private static bool WaitUntil(Func<bool> condition, int ms)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(20);
            }
            return condition();
        }

        [TestMethod]
        public void ADropUnderARunningListen_EndsInTheErrorCallback_AndIsNotRebuilt()
        {
            using var router = new Router();
            int done = 0;
            ITikTrapSentence? error = null;
            var listen = router.CreateCommand("/ip/address/listen");
            listen.ExecuteWithCallback(_ => { }, e => error = e, () => Interlocked.Increment(ref done));
            Thread.Sleep(1500);   // a poll after the baseline has been answered
            Assert.IsNull(error, "the listen failed before the drop: " + error?.Message);

            router.Dropped = true;

            Assert.IsTrue(WaitUntil(() => error != null || Volatile.Read(ref done) > 0, 5000), "the listen did not end");
            listen.CancelAndJoin();
            Assert.IsNotNull(error, "a dropped session under a running listen ended in onDone, not errorCallback");
            Assert.AreEqual(0, Volatile.Read(ref done), "onDone after a lost connection");
            Assert.AreEqual(0, router.Rebuilds, "a session dropped under a running callback was rebuilt");
        }

        [TestMethod]
        public void APlainCommand_AfterTheListenEnded_IsRebuiltAgain()
        {
            using var router = new Router();
            var listen = router.CreateCommand("/ip/address/listen");
            listen.ExecuteWithCallback(_ => { }, _ => { }, () => { });
            listen.CancelAndJoin();

            router.Dropped = true;
            router.CreateCommand("/ip/address/print").ExecuteList();

            Assert.AreEqual(1, router.Rebuilds, "the idle-logout rebuild is back once no callback runs");
        }
    }
}
