// GenericCommandRunningGuardTests.cs — the transport-neutral command refuses a second execute, and a change of its
// command text or connection, while it runs, as ApiCommand does; and every way a run ends clears the flag again.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class GenericCommandRunningGuardTests
    {
        private const string AlreadyRunning = "Command is already running.";
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        /// <summary>
        /// A recording transport whose first armed print blocks (sync: on <see cref="Release"/>; async: on
        /// <see cref="AsyncRelease"/>) and whose monitor runs on <see cref="PollingMonitorEngine"/> until the test ends
        /// it the way it chooses. Every other call records and returns at once, so a second execute that the old code
        /// lets through shows up as a second call rather than a deadlock.
        /// </summary>
        private sealed class BlockingConnection : TikCommandConnectionBase, ITikMonitorTransport, IPollingMonitorHost
        {
            public readonly List<string> Calls = new List<string>();
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
            public readonly TaskCompletionSource<bool> AsyncRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _armed = 1;

            /// <summary>No print blocks: for the monitor tests, whose held run is the monitor.</summary>
            public void Disarm() => Interlocked.Exchange(ref _armed, 0);

            public BlockingConnection() => SetOpened();

            public string MonitorEnding = "";
            public readonly ManualResetEventSlim EndMonitor = new ManualResetEventSlim(false);

            public override TikConnectionCapability Capabilities
                => TikConnectionCapability.Crud | TikConnectionCapability.AsyncCommands | TikConnectionCapability.Listen;

            private void Record(string hook, TikCommandDescriptor d)
            {
                lock (Calls)
                    Calls.Add(hook + " " + d.CommandText);
            }

            public int CallCount { get { lock (Calls) return Calls.Count; } }

            private static IList<TikRecordSentence> OneRow()
                => new List<TikRecordSentence> { new TikRecordSentence(new Dictionary<string, string> { { ".id", "*1" }, { "name", "a" } }) };

            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            {
                Record("RunPrint", descriptor);
                if (Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.Set();
                    Release.Wait(Wait);
                }
                return OneRow();
            }

            protected override async Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                Record("RunPrintAsync", descriptor);
                if (Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.Set();
                    await AsyncRelease.Task.ConfigureAwait(false);
                }
                return OneRow();
            }

            protected override string RunAdd(TikCommandDescriptor descriptor)
            {
                Record("RunAdd", descriptor);
                return "*1";
            }

            protected override void RunNonQuery(TikCommandDescriptor descriptor) => Record("RunNonQuery", descriptor);

            protected override string RunRawText(TikCommandDescriptor descriptor)
            {
                Record("RunRawText", descriptor);
                return "";
            }

            protected override Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
                => Task.FromResult(RunAdd(descriptor));

            protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                RunNonQuery(descriptor);
                return Task.CompletedTask;
            }

            protected override Task<string> RunRawTextAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
                => Task.FromResult(RunRawText(descriptor));

            // The monitor runs as every polled transport's does: PollingMonitorEngine.Run on a worker, so the endings
            // below are the engine's own (done, trap, cancel, connection lost), not a scripted onDone.
            TikMonitorHandle ITikMonitorTransport.RunMonitorAsync(TikCommandDescriptor descriptor,
                Action<TikRecordSentence> onRow, Action<TikTrapSentenceResult> onError, Action onDone)
            {
                Record("RunMonitor", descriptor);
                return PollingMonitorEngine.StartWorker("test-monitor", handle => PollingMonitorEngine.Run(this, handle, onError, onDone, () =>
                {
                    while (!handle.CancelRequested && !EndMonitor.Wait(20))
                    {
                    }
                    switch (MonitorEnding)
                    {
                        case "trap":
                            throw new InvalidOperationException("monitor failed");
                        case "lost":
                            // The transport closes itself because the connection broke (CliConnectionBase.CloseAfterAbandonedRead).
                            MarkLost();
                            SetClosed();
                            throw new IOException("connection lost");
                        case "closed":
                            // The caller's Close() on another thread: the poll then fails on the closed channel.
                            SetClosed();
                            throw new IOException("channel closed");
                    }
                }));
            }

            bool IPollingMonitorHost.ClosedByCaller => ClosedByCaller;
            IList<TikRecordSentence> IPollingMonitorHost.PollSnapshot(TikCommandDescriptor printDescriptor) => OneRow();
            TikTrapSentenceResult IPollingMonitorHost.ToTrap(Exception ex) => new TikTrapSentenceResult(ex.Message);

            public override void Open(string host, string user, string password) { }
            public override void Open(string host, int port, string user, string password) { }
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override void Close() { }
        }

        private static void AssertRefused(Action call, string what)
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(call, what);
            Assert.AreEqual(AlreadyRunning, ex.Message, what);
        }

        private static void AssertEverySecondUseIsRefused(BlockingConnection conn, ITikCommand cmd)
        {
            Assert.IsTrue(cmd.IsRunning, "IsRunning while the first run is held");
            int calls = conn.CallCount;
            AssertRefused(() => cmd.ExecuteList(), "ExecuteList");
            AssertRefused(() => cmd.ExecuteScalar(), "ExecuteScalar");
            AssertRefused(() => cmd.ExecuteNonQuery(), "ExecuteNonQuery");
            AssertRefused(() => cmd.CommandText = "/x", "CommandText setter");
            AssertRefused(() => cmd.Connection = conn, "Connection setter");
            Assert.AreEqual("/ip/address/print", cmd.CommandText);
            Assert.AreEqual(calls, conn.CallCount, "a refused execute reaches no hook");
        }

        [TestMethod]
        public void ASyncRunRefusesASecondExecuteUntilItEnds()
        {
            var conn = new BlockingConnection();
            var cmd = conn.CreateCommand("/ip/address/print");
            var first = Task.Run(() => cmd.ExecuteList());
            Assert.IsTrue(conn.Entered.Wait(Wait), "the first run reached its hook");

            try
            {
                AssertEverySecondUseIsRefused(conn, cmd);
            }
            finally
            {
                conn.Release.Set();
            }

            Assert.IsTrue(first.Wait(Wait));
            Assert.IsFalse(cmd.IsRunning, "the run that set the flag cleared it");
            cmd.ExecuteList();
        }

        [TestMethod]
        public async Task AnAsyncRunRefusesASecondExecuteUntilItEnds()
        {
            var conn = new BlockingConnection();
            var cmd = conn.CreateCommand("/ip/address/print");
            var first = cmd.ExecuteListAsync();
            Assert.IsTrue(conn.Entered.Wait(Wait), "the first run reached its hook");

            try
            {
                AssertEverySecondUseIsRefused(conn, cmd);
                var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => cmd.ExecuteListAsync());
                Assert.AreEqual(AlreadyRunning, ex.Message);
            }
            finally
            {
                conn.AsyncRelease.SetResult(true);
            }

            await first;
            Assert.IsFalse(cmd.IsRunning, "the run that set the flag cleared it");
            await cmd.ExecuteListAsync();
        }

        [DataTestMethod]
        [DataRow("done")]
        [DataRow("trap")]
        [DataRow("cancel")]
        [DataRow("lost")]
        [DataRow("closed")]
        public void EveryWayAMonitorEndsClearsTheFlag(string ending)
        {
            var conn = new BlockingConnection { MonitorEnding = ending };
            conn.Disarm();
            var cmd = conn.CreateCommand("/ip/address/print");
            var ended = new ManualResetEventSlim(false);
            var traps = new List<string>();

            // Ended: onDone, or the error for the monitor that failed (D21: no onDone after an error).
            cmd.ExecuteWithCallback(row => { }, trap => { lock (traps) traps.Add(trap.Message); ended.Set(); }, () => ended.Set());

            try
            {
                AssertEverySecondUseIsRefused(conn, cmd);
            }
            finally
            {
                if (ending == "cancel")
                    cmd.Cancel();
                else
                    conn.EndMonitor.Set();
            }

            Assert.IsTrue(ended.Wait(Wait), "the monitor ended");
            Assert.IsTrue(cmd.CancelAndJoin(Wait), "the worker finished");
            lock (traps)
                CollectionAssert.AreEqual(ending == "trap" ? new[] { "monitor failed" } : ending == "lost" ? new[] { "connection lost" } : new string[0],
                    traps.ToArray(), "an error is reported only when the monitor failed or lost its connection");
            Assert.IsFalse(cmd.IsRunning, "a monitor that ended (" + ending + ") clears the flag");
            cmd.ExecuteList();   // the fake still answers after "lost" and "closed": what is under test is the flag
        }

        /// <summary>
        /// D21: onDone fires once when the monitor ends without an error (it ended by itself, or the caller cancelled
        /// it or closed the connection), and not after an error, which goes to errorCallback alone. The running flag
        /// clears either way.
        /// </summary>
        /// <remarks>
        /// "lost" is the transport closing a connection that broke, "closed" the caller's Close(): both leave the
        /// connection closed when the poll fails, and only the caller's is a quiet ending. A lost connection is an
        /// error, as the binary API reports it (ApiCallbackEndingTests.LostConnection_CallsTheErrorCallbackOnly).
        /// </remarks>
        [DataTestMethod]
        [DataRow("done", true)]
        [DataRow("cancel", true)]
        [DataRow("closed", true)]
        [DataRow("lost", false)]
        [DataRow("trap", false)]
        public void OnDoneFiresOnlyWhenTheMonitorEndsWithoutAnError(string ending, bool expectDone)
        {
            var conn = new BlockingConnection { MonitorEnding = ending };
            conn.Disarm();
            var cmd = conn.CreateCommand("/ip/address/print");
            var ended = new ManualResetEventSlim(false);
            var traps = new List<string>();
            int done = 0;

            cmd.ExecuteWithCallback(row => { },
                trap => { lock (traps) traps.Add(trap.Message); ended.Set(); },
                () => { Interlocked.Increment(ref done); ended.Set(); });
            if (ending == "cancel")
                cmd.Cancel();
            else
                conn.EndMonitor.Set();

            Assert.IsTrue(ended.Wait(Wait), "a callback said the monitor ended");
            Assert.IsTrue(cmd.CancelAndJoin(Wait), "the worker finished");
            lock (traps)
                Assert.AreEqual(ending == "trap" || ending == "lost" ? 1 : 0, traps.Count, "errors reported");
            Assert.AreEqual(expectDone ? 1 : 0, Volatile.Read(ref done), "onDone calls after '" + ending + "'");
            Assert.IsFalse(cmd.IsRunning, "the flag clears on every ending");
        }
    }
}
