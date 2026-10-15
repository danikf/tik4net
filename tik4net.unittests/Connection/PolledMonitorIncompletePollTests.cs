#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// A polled monitor or listen skips a poll whose answer the transport refused as incomplete — over MAC-Telnet a
    /// loaded router drops part of its own output now and then, and the next poll is a whole new snapshot — and
    /// reports the refusal only when <see cref="PollingMonitorEngine.MaxIncompletePollsInARow"/> polls in a row fail
    /// that way. Seen once in a full parallel test run, as a torch that ended with an error after one lost frame.
    /// </summary>
    [TestClass]
    public class PolledMonitorIncompletePollTests
    {
        private const int PollIntervalMs = 20;

        /// <summary>Answers one row per poll, except the polls named as incomplete.</summary>
        private sealed class ScriptedHost : IPollingMonitorHost
        {
            private readonly HashSet<int> _incomplete;
            private int _polls;

            public ScriptedHost(params int[] incompletePolls) { _incomplete = new HashSet<int>(incompletePolls); }

            public bool IsOpen => true;

            public IList<TikRecordSentence> PollSnapshot(TikCommandDescriptor printDescriptor)
            {
                int poll = Interlocked.Increment(ref _polls);
                if (_incomplete.Contains(poll))
                    throw new TikConnectionResponseIncompleteException("poll " + poll + " came back incomplete", 2);
                return new List<TikRecordSentence>
                {
                    new TikRecordSentence(new Dictionary<string, string> { { TikSpecialProperties.Id, "*1" }, { "poll", poll.ToString() } }),
                };
            }

            public TikTrapSentenceResult ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);
        }

        private sealed class Run
        {
            public readonly BlockingCollection<TikRecordSentence> Rows = new BlockingCollection<TikRecordSentence>();
            public readonly ConcurrentQueue<TikTrapSentenceResult> Errors = new ConcurrentQueue<TikTrapSentenceResult>();
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim();
            public TikMonitorHandle Handle = null!;
        }

        private static readonly TikCommandDescriptor Descriptor =
            new TikCommandDescriptor("/interface/monitor-traffic", new List<ITikCommandParameter>());

        private static Run Snapshot(IPollingMonitorHost host)
        {
            var run = new Run();
            run.Handle = PollingMonitorEngine.StartWorker("test-snapshot", h =>
                PollingMonitorEngine.SnapshotLoop(host, Descriptor, PollIntervalMs, h, run.Rows.Add, run.Errors.Enqueue, run.Done.Set));
            return run;
        }

        private static Run Listen(IPollingMonitorHost host)
        {
            var run = new Run();
            run.Handle = PollingMonitorEngine.StartListen("test-listen", host, Descriptor, null, PollIntervalMs,
                run.Rows.Add, run.Errors.Enqueue, run.Done.Set);
            return run;
        }

        // Collects the "poll" field of the rows seen until `count` rows arrived or the run ended.
        private static List<string> RowsUntil(Run run, int count)
        {
            var polls = new List<string>();
            while (polls.Count < count && !run.Done.IsSet && run.Rows.TryTake(out var row, 2000))
                polls.Add(row.GetResponseField("poll"));
            return polls;
        }

        [TestMethod]
        public void ASnapshotMonitorSkipsAnIncompletePollAndGoesOn()
        {
            var run = Snapshot(new ScriptedHost(2, 4, 5));
            var polls = RowsUntil(run, 4);
            run.Handle.Cancel();
            Assert.IsTrue(run.Done.Wait(2000));

            CollectionAssert.AreEqual(new[] { "1", "3", "6", "7" }, polls);
            Assert.AreEqual(0, run.Errors.Count, "an incomplete poll was reported instead of skipped");
        }

        [TestMethod]
        public void ASnapshotMonitorReportsIncompletePollsInARow()
        {
            var run = Snapshot(new ScriptedHost(2, 3, 4));
            Assert.IsTrue(run.Done.Wait(2000), "the monitor went on past MaxIncompletePollsInARow incomplete polls");
            Assert.AreEqual(1, run.Errors.Count);
            StringAssert.Contains(run.Errors.ToArray()[0].Message, "poll 4");
        }

        [TestMethod]
        public void AListenSkipsAnIncompletePollAndDiffsAgainstTheLastWholeRead()
        {
            // Poll 1 is the baseline; every whole poll after it changes the row, so each one is reported.
            var run = Listen(new ScriptedHost(3));
            var polls = RowsUntil(run, 2);
            run.Handle.Cancel();
            Assert.IsTrue(run.Done.Wait(2000));

            CollectionAssert.AreEqual(new[] { "2", "4" }, polls);
            Assert.AreEqual(0, run.Errors.Count, "an incomplete poll was reported instead of skipped");
        }

        [TestMethod]
        public void AListenReportsIncompletePollsInARow()
        {
            var run = Listen(new ScriptedHost(2, 3, 4));
            Assert.IsTrue(run.Done.Wait(2000), "the listen went on past MaxIncompletePollsInARow incomplete polls");
            Assert.AreEqual(1, run.Errors.Count);
        }
    }
}
