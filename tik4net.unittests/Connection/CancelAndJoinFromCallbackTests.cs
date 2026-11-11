using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;
using tik4net.Testing;
using tik4net.unittests.Api;
using tik4net.unittests.Winbox;
using tik4net.Winbox;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <c>CancelAndJoin()</c> called from the command's own callback cancels and returns; it does not join its own thread.
    /// </summary>
    /// <remarks>
    /// Every callback of <c>ExecuteWithCallback</c> runs on the command's worker, and <c>CancelAndJoin</c> waits for that
    /// worker to end. Called from the callback, that is a thread waiting for itself, which never returns: "stop after the
    /// first N rows" with a <c>CancelAndJoin()</c> in the row callback, or a cleanup in <c>onDone</c>, hung the worker for
    /// good and left the command running. Now the call from the worker cancels without joining and returns as if joined
    /// (<c>true</c> from the bounded overload): the join the caller asked for completes the moment the callback returns.
    /// The same on the binary API, on the polled transports (here WinBox native) and on <see cref="TikFakeConnection"/>.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class CancelAndJoinFromCallbackTests
    {
        // ── Binary API ───────────────────────────────────────────────────────────

        private static FakeRouterServer ListenServer(out ManualResetEventSlim cancelArrived)
        {
            var server = new FakeRouterServer();
            var cancel = new ManualResetEventSlim(false);
            cancelArrived = cancel;
            Task.Run(() =>
            {
                try
                {
                    server.AcceptClient();
                    server.ReadSentence(); server.WriteSentence("!done");   // login
                    var listen = server.ReadSentence();
                    string listenTag = listen.First(w => w.StartsWith(".tag=", StringComparison.Ordinal));
                    server.WriteSentence("!re", "=.id=*1", "=name=ether1", listenTag);
                    server.ReadSentence();                                  // /cancel
                    cancel.Set();
                    server.WriteSentence("!trap", "=category=2", "=message=interrupted", listenTag);
                    server.WriteSentence("!done", listenTag);
                    server.WriteSentence("!done");                          // the /cancel's own (echoed tag)
                    server.ReadSentence();                                  // /quit
                    server.WriteSentence("!fatal", "session terminated on request");
                }
                catch { /* the test has its verdict already */ }
            });
            return server;
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Api_CancelAndJoinFromTheRowCallback_DoesNotDeadlock(bool bounded)
        {
            using var server = ListenServer(out var cancelArrived);
            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(5);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            var cmd = connection.CreateCommand("/interface/listen");
            var returned = new ManualResetEventSlim(false);
            var done = new ManualResetEventSlim(false);
            bool joined = false;
            cmd.ExecuteWithCallback(row =>
            {
                if (bounded) joined = cmd.CancelAndJoin(TimeSpan.FromSeconds(3));
                else { cmd.CancelAndJoin(); joined = true; }
                returned.Set();
            }, null, () => done.Set());

            Assert.IsTrue(returned.Wait(5000),
                "CancelAndJoin() called from the row callback never returned: it joined the callback's own thread");
            Assert.IsTrue(joined, "the call from the worker returns as if joined");
            Assert.IsTrue(cancelArrived.IsSet, "the router was asked to stop");
            Assert.IsTrue(done.Wait(5000), "once the callback returned, the cancelled command ended in onDone");
            Assert.IsFalse(cmd.IsRunning);
        }

        // ── A polled transport (WinBox native's monitor engine) ─────────────────

        private const int StartCmd = 0xFE0012;
        private const int CancelCmd = 0xFE0013;
        private static readonly string TorchWindow =
            "[{name:'Tools',c:[{name:'Torch',c:[{name:'Torch',title:'Torch',type:'query',path:[ 24,7 ],startcmd:" + StartCmd
            + ",cancelcmd:" + CancelCmd + ",autorefresh:100,c:[" +
            "{name:'Interface',type:'string',id:'s1',ro:1}]}]}]}]";

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Polled_CancelAndJoinFromACallback_DoesNotDeadlock(bool bounded)
        {
            using var h = new NativeConnectionHarness(TorchWindow);
            h.Answer = req => NativeConnectionHarness.CommandOf(req) == StartCmd
                ? NativeConnectionHarness.ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, 3))
                : NativeConnectionHarness.ReplyTo(req);
            var returned = new ManualResetEventSlim(false);
            bool joined = false;
            ITikCommand cmd = null;
            cmd = h.Connection.CreateCommand("/tool/torch",
                new tik4net.Connection.TikCommandParameter("interface", "ether1"),
                new tik4net.Connection.TikCommandParameter("duration", "1s"));
            cmd.ExecuteWithCallback(_ => { }, _ => { }, () =>
            {
                if (bounded) joined = cmd.CancelAndJoin(TimeSpan.FromSeconds(3));
                else { cmd.CancelAndJoin(); joined = true; }
                returned.Set();
            });

            Assert.IsTrue(returned.Wait(6000),
                "CancelAndJoin() called from onDone never returned: it joined the monitor's own worker thread");
            Assert.IsTrue(joined, "the call from the worker returns as if joined");
            Assert.IsTrue(SpinWait.SpinUntil(() => !cmd.IsRunning, 5000), "the worker never ended after its callback");
        }

        // ── TikFakeConnection ─────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Fake_CancelAndJoinFromTheRowCallback_DoesNotDeadlock(bool bounded)
        {
            var cmd = new TikFakeConnection()
                .WithResponse(_ => true, new ITikSentence[] { new TikFakeReSentence(new Dictionary<string, string> { ["a"] = "1" }) })
                .CreateCommand("/interface/listen");
            var returned = new ManualResetEventSlim(false);
            var done = new ManualResetEventSlim(false);
            bool joined = false;
            cmd.ExecuteWithCallback(row =>
            {
                if (bounded) joined = cmd.CancelAndJoin(TimeSpan.FromSeconds(3));
                else { cmd.CancelAndJoin(); joined = true; }
                returned.Set();
            }, null, () => done.Set());

            Assert.IsTrue(returned.Wait(5000),
                "CancelAndJoin() called from the row callback never returned: it joined the callback's own thread");
            Assert.IsTrue(joined, "the call from the worker returns as if joined");
            Assert.IsTrue(done.Wait(5000), "once the callback returned, the cancelled command ended in onDone");
            Assert.IsTrue(SpinWait.SpinUntil(() => !cmd.IsRunning, 5000));
        }
    }
}
