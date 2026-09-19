#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// The polled <c>/listen</c> shared by the CLI, REST and native-WinBox transports diffs table snapshots, and the
    /// first snapshot is the baseline it diffs against. A change made after the listen call has returned is a change
    /// the caller asked to hear about — the binary API's <c>=listen</c> reports it — so the baseline has to be taken
    /// before the call returns. Taken later, on the worker, a change made in the meantime is absorbed into the
    /// baseline and never reported; through a RoMON relay or on MAC-Telnet that first read takes a second or more.
    /// </summary>
    [TestClass]
    public class PolledListenSeedTests
    {
        private const int PollIntervalMs = 100;

        /// <summary>A table read that takes as long as a slow link does, like a print through a RoMON relay.</summary>
        private sealed class SlowTableHost : IPollingMonitorHost
        {
            private readonly object _lock = new object();
            private readonly Dictionary<string, Dictionary<string, string>> _rows = new Dictionary<string, Dictionary<string, string>>();

            public int PollDelayMs { get; set; } = 400;
            public Exception? FailWith { get; set; }
            public int Polls;

            public bool IsOpen => true;

            public void Set(string id, string name)
            {
                lock (_lock)
                    _rows[id] = new Dictionary<string, string> { { TikSpecialProperties.Id, id }, { "name", name } };
            }

            public void Remove(string id)
            {
                lock (_lock) _rows.Remove(id);
            }

            public IList<TikRecordSentence> PollSnapshot(TikCommandDescriptor printDescriptor)
            {
                Interlocked.Increment(ref Polls);
                Thread.Sleep(PollDelayMs);
                if (FailWith != null)
                    throw FailWith;
                lock (_lock)
                    return _rows.Values.Select(r => new TikRecordSentence(new Dictionary<string, string>(r))).ToList();
            }

            public TikTrapSentenceResult ToTrap(Exception ex) => TikTrapSentenceResult.FromException(ex);
        }

        private sealed class Listener
        {
            public readonly BlockingCollection<TikRecordSentence> Rows = new BlockingCollection<TikRecordSentence>();
            public readonly List<TikTrapSentenceResult> Errors = new List<TikTrapSentenceResult>();
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim();
            public TikMonitorHandle Handle = null!;

            public TikRecordSentence? Next(int timeoutMs)
                => Rows.TryTake(out var row, timeoutMs) ? row : null;
        }

        private static Listener Start(IPollingMonitorHost host)
        {
            var listener = new Listener();
            listener.Handle = PollingMonitorEngine.StartListen("test-listen", host, new TikCommandDescriptor("/ip/address/print",
                new List<ITikCommandParameter>()), null, PollIntervalMs,
                listener.Rows.Add, trap => { lock (listener.Errors) listener.Errors.Add(trap); }, listener.Done.Set);
            return listener;
        }

        [TestMethod]
        public void RowAddedRightAfterTheListenReturns_IsReported()
        {
            var host = new SlowTableHost();
            host.Set("*1", "existing");

            var listener = Start(host);
            try
            {
                host.Set("*2", "added");

                var row = listener.Next(3000);
                Assert.IsNotNull(row, "the row added after the listen call returned was never reported");
                Assert.AreEqual("*2", row!.GetResponseField(TikSpecialProperties.Id));
                Assert.AreEqual("added", row.GetResponseField("name"));
            }
            finally { listener.Handle.Join(5000); }
        }

        [TestMethod]
        public void RowChangedRightAfterTheListenReturns_IsReported()
        {
            var host = new SlowTableHost();
            host.Set("*1", "before");

            var listener = Start(host);
            try
            {
                host.Set("*1", "after");

                var row = listener.Next(3000);
                Assert.IsNotNull(row, "the change made after the listen call returned was never reported");
                Assert.AreEqual("after", row!.GetResponseField("name"));
            }
            finally { listener.Handle.Join(5000); }
        }

        [TestMethod]
        public void RowRemovedRightAfterTheListenReturns_IsReportedDead()
        {
            var host = new SlowTableHost();
            host.Set("*1", "doomed");

            var listener = Start(host);
            try
            {
                host.Remove("*1");

                var row = listener.Next(3000);
                Assert.IsNotNull(row, "the removal made after the listen call returned was never reported");
                Assert.AreEqual("*1", row!.GetResponseField(TikSpecialProperties.Id));
                Assert.AreEqual("true", row.GetResponseField(".dead"));
            }
            finally { listener.Handle.Join(5000); }
        }

        /// <summary>The baseline is still silent: a listen reports changes, it never replays the table.</summary>
        [TestMethod]
        public void TheExistingTable_IsNotReplayed()
        {
            var host = new SlowTableHost { PollDelayMs = 20 };
            host.Set("*1", "a");
            host.Set("*2", "b");

            var listener = Start(host);
            try
            {
                SpinWait.SpinUntil(() => Volatile.Read(ref host.Polls) >= 4, 3000);
                Assert.IsNull(listener.Next(0), "a row that existed before the listen was reported");
            }
            finally { listener.Handle.Join(5000); }
        }

        /// <summary>
        /// The baseline read is taken on the caller's thread, but a failure of it is not thrown there: it is
        /// reported the way every later poll failure is — onError, then onDone, from the worker — so a caller
        /// wired for the binary API's asynchronous trap sees the same shape.
        /// </summary>
        [TestMethod]
        public void AFailedBaselineRead_IsReportedThroughTheCallbacks_NotThrown()
        {
            var host = new SlowTableHost { PollDelayMs = 0, FailWith = new InvalidOperationException("no such command prefix") };

            var listener = Start(host);

            Assert.IsTrue(listener.Done.Wait(3000), "onDone was not called after the baseline read failed");
            lock (listener.Errors)
            {
                Assert.AreEqual(1, listener.Errors.Count);
                StringAssert.Contains(listener.Errors[0].Message, "no such command prefix");
            }
        }

        /// <summary>A listen cancelled before its first later poll still ends with exactly one onDone.</summary>
        [TestMethod]
        public void CancelledImmediately_CallsOnDoneOnce()
        {
            var host = new SlowTableHost { PollDelayMs = 20 };
            int done = 0;
            var handle = PollingMonitorEngine.StartListen("test-listen", host, new TikCommandDescriptor("/ip/address/print",
                new List<ITikCommandParameter>()), null, PollIntervalMs, _ => { }, _ => { }, () => Interlocked.Increment(ref done));

            Assert.IsTrue(handle.Join(3000));
            Assert.AreEqual(1, done);
        }
    }
}
