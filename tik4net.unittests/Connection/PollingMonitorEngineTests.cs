using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.unittests.Cli;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The polled-monitor plumbing every polling transport shares: which verbs it serves as a table, that a worker
    /// reports a failure once and says what failed, and that rows stop at a cancel.
    /// </summary>
    [TestClass]
    public class PollingMonitorEngineTests
    {
        private sealed class Host : IPollingMonitorHost
        {
            public readonly List<string> Polled = new List<string>();
            public IList<TikRecordSentence> Rows = new List<TikRecordSentence>();

            public bool IsOpen => true;

            public IList<TikRecordSentence> PollSnapshot(TikCommandDescriptor printDescriptor)
            {
                lock (Polled) Polled.Add(printDescriptor.CommandText);
                return Rows;
            }

            public TikTrapSentenceResult ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);
        }

        private static TikCommandDescriptor Command(string text) => new TikCommandDescriptor(text, new List<ITikCommandParameter>());

        private static TikRecordSentence Row(string id) => new TikRecordSentence(new Dictionary<string, string> { { ".id", id } });

        [TestMethod]
        public void AMonitorVerb_IsNotATableShape()
        {
            var handle = PollingMonitorEngine.TryStartTableShape("test", new Host(), Command("/interface/monitor-traffic"),
                "monitor-traffic", null, r => { }, e => { }, () => { });

            Assert.IsNull(handle);
        }

        [TestMethod]
        public void AListen_PollsThePrintOfItsTable()
        {
            var host = new Host();
            using (var done = new ManualResetEventSlim())
            {
                var handle = PollingMonitorEngine.TryStartTableShape("test", host, Command("/ip/address/listen"),
                    "listen", null, r => { }, e => { }, () => done.Set());

                Assert.IsNotNull(handle);
                handle.Cancel();
                Assert.IsTrue(done.Wait(5000), "onDone never fired");
                lock (host.Polled) CollectionAssert.Contains(host.Polled, "/ip/address/print");
            }
        }

        [TestMethod]
        public void Emit_StopsAtTheCancel()
        {
            var handle = new TikMonitorHandle();
            var seen = new List<string>();

            PollingMonitorEngine.Emit(new[] { Row("*1"), Row("*2"), Row("*3") }, handle, row =>
            {
                seen.Add(row.GetResponseFieldOrDefault(".id", null));
                if (seen.Count == 2) handle.Cancel();
            });

            CollectionAssert.AreEqual(new[] { "*1", "*2" }, seen);
        }

        // ── a CLI monitor whose snapshot fails ──────────────────────────────

        private sealed class ResetCli : ScriptedCliConnectionBase
        {
            public ResetCli() { CliFieldSeparator = null; }

            public void OpenScripted()
                => OpenScripted(_ => Task.FromResult(0), (cmd, ct) => throw new IOException("reset"),
                    (raw, ct) => Task.FromResult(string.Empty), () => { });

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password,
                CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password,
                CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        [TestMethod]
        public void ACliMonitorWhoseSnapshotFails_SaysWhatFailed_AndEndsOnce()
        {
            using (var connection = new ResetCli())
            {
                connection.OpenScripted();
                var errors = new List<string>();
                int doneCount = 0;
                using (var done = new ManualResetEventSlim())
                {
                    var parameters = new List<ITikCommandParameter> { connection.CreateParameter("interface", "ether1") };
                    ((ITikMonitorTransport)connection).RunMonitorAsync(
                        new TikCommandDescriptor("/interface/monitor-traffic", parameters),
                        r => { }, e => { lock (errors) errors.Add(e.Message); },
                        () => { Interlocked.Increment(ref doneCount); done.Set(); });

                    Assert.IsTrue(done.Wait(10000), "onDone never fired");
                }
                Thread.Sleep(100);   // a second onDone would land here

                Assert.AreEqual(1, doneCount);
                Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
                StringAssert.Contains(errors[0], "IOException");
                StringAssert.Contains(errors[0], "reset");
            }
        }
    }
}
