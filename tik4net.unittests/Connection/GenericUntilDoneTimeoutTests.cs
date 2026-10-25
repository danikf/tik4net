// GenericUntilDoneTimeoutTests.cs — ExecuteListUntilDone off the binary API (command-layer D17): its timeout is honoured
// on a transport that declares AsyncCommands, and its async form runs on every such transport, gated on AsyncCommands
// rather than Streaming (yielding the awaited list: not streamed).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class GenericUntilDoneTimeoutTests
    {
        /// <summary>
        /// A transport whose print takes <see cref="PrintTime"/> on both paths: the async one honours its token, the sync
        /// one cannot be interrupted. Every built-in generic transport's sync print is its async print awaited with no
        /// token, so the async path is the one a deadline can reach.
        /// </summary>
        private sealed class SlowTransport : TikCommandConnectionBase
        {
            private readonly TikConnectionCapability _capabilities;
            public TimeSpan PrintTime = TimeSpan.FromSeconds(3);
            public readonly List<string> Calls = new List<string>();

            public SlowTransport(TikConnectionCapability capabilities) { _capabilities = capabilities; }

            public override TikConnectionCapability Capabilities => _capabilities;

            private static IList<TikRecordSentence> Rows()
                => new List<TikRecordSentence>
                {
                    new TikRecordSentence(new Dictionary<string, string> { ["seq"] = "0", ["host"] = "192.168.4.1" }),
                    new TikRecordSentence(new Dictionary<string, string> { ["seq"] = "1", ["host"] = "192.168.4.1" }),
                };

            public override void Open(string host, int port, string user, string password) => SetOpened();
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default)
            {
                SetOpened();
                return Task.CompletedTask;
            }
            public override void Close() => SetClosed();

            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            {
                lock (Calls) Calls.Add("RunPrint");
                Thread.Sleep(PrintTime);
                return Rows();
            }

            protected override async Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                lock (Calls) Calls.Add("RunPrintAsync");
                await Task.Delay(PrintTime, cancellationToken).ConfigureAwait(false);
                return Rows();
            }

            protected override string RunAdd(TikCommandDescriptor descriptor) => "*1";
            protected override void RunNonQuery(TikCommandDescriptor descriptor) { }
        }

        private const TikConnectionCapability Async = TikConnectionCapability.Crud | TikConnectionCapability.AsyncCommands;

        [TestMethod]
        public void TheTimeoutEndsAReadStillRunning()
        {
            var conn = new SlowTransport(Async);
            var cmd = conn.CreateCommand("/ping");

            var sw = Stopwatch.StartNew();
            var ex = Assert.ThrowsException<TikCommandAbortException>(
                () => cmd.ExecuteListUntilDone(TimeSpan.FromMilliseconds(200)).ToList());
            sw.Stop();

            Assert.AreEqual("Command did not finish within 0.2 second(s).", ex.Message, "the API's wording");
            Assert.IsNull(ex.InnerException);
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.ElapsedMilliseconds} ms");
            Assert.IsFalse(cmd.IsRunning, "the run ended with the abort");
        }

        [TestMethod]
        public void AReadThatEndsInTimeReturnsItsRows()
        {
            var conn = new SlowTransport(Async) { PrintTime = TimeSpan.FromMilliseconds(10) };
            var rows = conn.CreateCommand("/ping").ExecuteListUntilDone(TimeSpan.FromSeconds(10)).ToList();
            CollectionAssert.AreEqual(new[] { "0", "1" }, rows.Select(r => r.GetResponseField("seq")).ToList());
        }

        [TestMethod]
        public void WithoutATimeoutItIsTheSynchronousList()
        {
            var conn = new SlowTransport(Async) { PrintTime = TimeSpan.Zero };
            Assert.AreEqual(2, conn.CreateCommand("/ping").ExecuteListUntilDone().Count());
            CollectionAssert.AreEqual(new[] { "RunPrint" }, conn.Calls);
        }

        [TestMethod]
        public void WithoutAsyncCommandsTheTimeoutCannotBeHonoured()
        {
            // A custom transport without an async path: the documented fallback, ExecuteList().
            var conn = new SlowTransport(TikConnectionCapability.Crud) { PrintTime = TimeSpan.FromMilliseconds(300) };
            Assert.AreEqual(2, conn.CreateCommand("/ping").ExecuteListUntilDone(TimeSpan.FromMilliseconds(50)).Count());
            CollectionAssert.AreEqual(new[] { "RunPrint" }, conn.Calls);
        }

#if NET8_0_OR_GREATER
        [TestMethod]
        public async Task TheAsyncFormRunsWithoutStreaming()
        {
            var conn = new SlowTransport(Async) { PrintTime = TimeSpan.FromMilliseconds(10) };
            var seqs = new List<string>();
            await foreach (var row in conn.CreateCommand("/ping").ExecuteListUntilDoneAsync(TimeSpan.FromSeconds(10)))
                seqs.Add(row.GetResponseField("seq"));
            CollectionAssert.AreEqual(new[] { "0", "1" }, seqs);
        }

        [TestMethod]
        public async Task TheAsyncFormNeedsAsyncCommands()
        {
            var conn = new SlowTransport(TikConnectionCapability.Crud);
            var ex = await Assert.ThrowsExceptionAsync<TikConnectionCapabilityNotSupportedException>(async () =>
            {
                await foreach (var unused in conn.CreateCommand("/ping").ExecuteListUntilDoneAsync())
                {
                }
            });
            Assert.AreEqual(TikConnectionCapability.AsyncCommands, ex.Capability);
        }

        [TestMethod]
        public async Task TheAsyncFormsTimeoutIsAnAbort()
        {
            var conn = new SlowTransport(Async);
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsExceptionAsync<TikCommandAbortException>(async () =>
            {
                await foreach (var unused in conn.CreateCommand("/ping").ExecuteListUntilDoneAsync(TimeSpan.FromMilliseconds(200)))
                {
                }
            });
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.ElapsedMilliseconds} ms");
        }

        [TestMethod]
        public async Task TheCallersTokenIsACancel()
        {
            var conn = new SlowTransport(Async);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            {
                await foreach (var unused in conn.CreateCommand("/ping").ExecuteListUntilDoneAsync(TimeSpan.FromSeconds(10), cts.Token))
                {
                }
            }, "ThrowsExceptionAsync matches the exact type; TaskCanceledException would fail it");
        }

        [TestMethod]
        public async Task ExecuteListWithDurationAsyncStillNeedsStreaming()
        {
            var conn = new SlowTransport(Async);
            var ex = await Assert.ThrowsExceptionAsync<TikConnectionCapabilityNotSupportedException>(async () =>
            {
                await foreach (var unused in conn.CreateCommand("/tool/torch").ExecuteListWithDurationAsync(TimeSpan.FromSeconds(1)))
                {
                }
            });
            Assert.AreEqual(TikConnectionCapability.Streaming, ex.Capability);
        }
#endif
    }
}
