using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests
{
    /// <summary>
    /// Pins the Task-based command surface (<see cref="ITikCommandAsync"/> reached through the
    /// <c>Execute*Async</c> extension methods) at the transport-neutral layer, router-free.
    /// </summary>
    /// <remarks>
    /// Two properties are worth a test rather than a review. First, <b>the async path must reach the async
    /// hook</b>: a Task-returning method that quietly blocks a thread-pool thread on the synchronous hook
    /// behaves correctly in every assertion about its result, and is exactly the façade the async contract
    /// forbids — only the call log distinguishes them. Second, <b>the two surfaces must agree on what a
    /// response means</b>: an empty answer is "not found" after a read and "succeeded with nothing to return"
    /// after a write, and the sync and async paths deciding that differently would be a difference nothing
    /// else detects.
    /// </remarks>
    [TestClass]
    public class AsyncCommandSurfaceTests
    {
        // ── The transport double ──────────────────────────────────────────────

        /// <summary>
        /// A <see cref="TikCommandConnectionBase"/> that records which hook was called on which surface and
        /// answers from a script. Its async hooks are real (already-completed) tasks, never the sync hook in
        /// disguise, so "which one ran" is an observable fact.
        /// </summary>
        private sealed class ScriptedConnection : TikCommandConnectionBase
        {
            public bool DeclareAsync { get; set; } = true;
            public bool ImplementAsyncHooks { get; set; } = true;
            public readonly List<string> Calls = new List<string>();
            public IList<TikRecordSentence> Rows = new List<TikRecordSentence>();
            public string AddedId = "*1";
            public CancellationToken SeenToken = CancellationToken.None;

            public override TikConnectionCapability Capabilities => DeclareAsync
                ? TikConnectionCapability.Crud | TikConnectionCapability.AsyncCommands
                : TikConnectionCapability.Crud;

            public override void Open(string host, string user, string password) => SetOpened();
            public override void Open(string host, int port, string user, string password) => SetOpened();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { SetOpened(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { SetOpened(); return Task.FromResult(0); }
            public override void Close() => SetClosed();

            protected override IList<TikRecordSentence> RunPrint(TikCommandDescriptor descriptor)
            { Calls.Add("sync:print"); return Rows; }

            protected override string RunAdd(TikCommandDescriptor descriptor)
            { Calls.Add("sync:add"); return AddedId; }

            protected override void RunNonQuery(TikCommandDescriptor descriptor)
            { Calls.Add("sync:nonquery"); }

            protected override Task<IList<TikRecordSentence>> RunPrintAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                if (!ImplementAsyncHooks) return base.RunPrintAsync(descriptor, cancellationToken);
                Calls.Add("async:print");
                SeenToken = cancellationToken;
                return Task.FromResult(Rows);
            }

            protected override Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                if (!ImplementAsyncHooks) return base.RunAddAsync(descriptor, cancellationToken);
                Calls.Add("async:add");
                SeenToken = cancellationToken;
                return Task.FromResult(AddedId);
            }

            protected override Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
            {
                if (!ImplementAsyncHooks) return base.RunNonQueryAsync(descriptor, cancellationToken);
                Calls.Add("async:nonquery");
                SeenToken = cancellationToken;
                return Task.FromResult(0);
            }
        }

        private static ScriptedConnection OpenedConnection()
        {
            var connection = new ScriptedConnection();
            connection.Open("host", "user", "pass");
            return connection;
        }

        private static TikRecordSentence Row(params string[] namesAndValues)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < namesAndValues.Length; i += 2)
                fields[namesAndValues[i]] = namesAndValues[i + 1];
            return new TikRecordSentence(fields);
        }

        // ── The async path is really async ────────────────────────────────────

        [TestMethod]
        public async Task AwaitingACommand_ReachesTheAsyncHook_NotTheBlockingOne()
        {
            var connection = OpenedConnection();
            connection.Rows = new List<TikRecordSentence> { Row(".id", "*1", "address", "10.0.0.1/24") };

            var rows = await connection.CreateCommand("/ip/address/print").ExecuteListAsync();

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("10.0.0.1/24", rows[0].GetResponseField("address"));
            CollectionAssert.AreEqual(new[] { "async:print" }, connection.Calls,
                "ExecuteListAsync must drive the transport's async hook — going through the blocking one would "
                + "produce the same rows while occupying a thread for the whole round trip.");
        }

        [TestMethod]
        public async Task EveryAsyncMethod_ReachesItsOwnHook()
        {
            var connection = OpenedConnection();
            connection.Rows = new List<TikRecordSentence> { Row(".id", "*7", "name", "ether1") };

            await connection.CreateCommand("/interface/set").ExecuteNonQueryAsync();
            await connection.CreateCommand("/interface/add").ExecuteScalarAsync();
            await connection.CreateCommand("/interface/print").ExecuteSingleRowAsync();
            await connection.CreateCommand("/interface/print").ExecuteScalarAsync("name");

            CollectionAssert.AreEqual(
                new[] { "async:nonquery", "async:add", "async:print", "async:print" }, connection.Calls);
        }

        [TestMethod]
        public async Task TheSynchronousSurfaceIsUnchanged_AndStillUsesTheSynchronousHooks()
        {
            var connection = OpenedConnection();
            connection.Rows = new List<TikRecordSentence> { Row(".id", "*1", "name", "ether1") };

            connection.CreateCommand("/interface/print").ExecuteList().ToList();
            connection.CreateCommand("/interface/set").ExecuteNonQuery();
            await Task.FromResult(0);

            CollectionAssert.AreEqual(new[] { "sync:print", "sync:nonquery" }, connection.Calls,
                "adding the async surface must not reroute the synchronous one through it.");
        }

        // ── Cancellation ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task APreCancelledToken_SendsNothingAtAll()
        {
            var connection = OpenedConnection();
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            try
            {
                await connection.CreateCommand("/ip/address/print").ExecuteListAsync(cancelled.Token);
                Assert.Fail("a token cancelled before dispatch must throw OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
                // expected — Level 0 of the cancellation contract
            }

            Assert.AreEqual(0, connection.Calls.Count,
                "nothing may be written when the token was already cancelled: the connection must be left "
                + "exactly as it was.");
        }

        [TestMethod]
        public async Task TheTokenIsHandedToTheTransport()
        {
            var connection = OpenedConnection();
            using (var cts = new CancellationTokenSource())
            {
                await connection.CreateCommand("/ip/address/print").ExecuteListAsync(cts.Token);
                Assert.AreEqual(cts.Token, connection.SeenToken,
                    "the caller's token must reach the transport — a hook that receives CancellationToken.None "
                    + "cannot honour a cancel that arrives mid-command.");
            }
        }

        // ── Fail-closed ───────────────────────────────────────────────────────

        [TestMethod]
        public async Task ATransportThatDoesNotDeclareAsyncCommands_ThrowsBeforeDispatch()
        {
            var connection = OpenedConnection();
            connection.DeclareAsync = false;

            try
            {
                await connection.CreateCommand("/ip/address/print").ExecuteListAsync();
                Assert.Fail("expected TikConnectionCapabilityNotSupportedException.");
            }
            catch (TikConnectionCapabilityNotSupportedException ex)
            {
                Assert.AreEqual(TikConnectionCapability.AsyncCommands, ex.Capability);
            }

            Assert.AreEqual(0, connection.Calls.Count, "the check must happen before anything is sent.");
        }

        [TestMethod]
        public async Task ATransportWithUnimplementedAsyncHooks_ReportsTheCapability_NotSomethingObscure()
        {
            // The other half of fail-closed: a transport whose flag says yes but whose hooks are the inherited
            // defaults. It must still name AsyncCommands, and it must NOT quietly fall back to blocking.
            var connection = OpenedConnection();
            connection.ImplementAsyncHooks = false;

            try
            {
                await connection.CreateCommand("/ip/address/print").ExecuteListAsync();
                Assert.Fail("expected TikConnectionCapabilityNotSupportedException.");
            }
            catch (TikConnectionCapabilityNotSupportedException ex)
            {
                Assert.AreEqual(TikConnectionCapability.AsyncCommands, ex.Capability);
            }
            CollectionAssert.DoesNotContain(connection.Calls, "sync:print");
        }

        // ── The two surfaces agree on what a response means ───────────────────

        [TestMethod]
        public async Task AReadThatMatchedNothing_IsNoSuchItem_OnBothSurfaces()
        {
            var connection = OpenedConnection();   // Rows stays empty

            Assert.ThrowsException<TikNoSuchItemException>(
                () => connection.CreateCommand("/ip/address/print").ExecuteScalar());

            try
            {
                await connection.CreateCommand("/ip/address/print").ExecuteScalarAsync();
                Assert.Fail("expected TikNoSuchItemException.");
            }
            catch (TikNoSuchItemException)
            {
                // expected
            }
        }

        [TestMethod]
        public async Task AWriteThatPrintedNothing_IsAnEmptyResponse_OnBothSurfaces()
        {
            // Not "no such item": set/unset/remove/enable succeed silently, and calling that a missing record
            // fabricates a router error for a command that worked (P2.34).
            var connection = OpenedConnection();   // Rows stays empty

            Assert.ThrowsException<TikCommandEmptyResponseException>(
                () => connection.CreateCommand("/ip/address/set").ExecuteScalar());

            try
            {
                await connection.CreateCommand("/ip/address/set").ExecuteScalarAsync();
                Assert.Fail("expected TikCommandEmptyResponseException.");
            }
            catch (TikCommandEmptyResponseException)
            {
                // expected
            }
        }

        [TestMethod]
        public async Task AnAddWithoutAnId_IsAnEmptyResponse_NotAMissingRecord()
        {
            var connection = OpenedConnection();
            connection.AddedId = null;

            try
            {
                await connection.CreateCommand("/ip/address/add").ExecuteScalarAsync();
                Assert.Fail("expected TikCommandEmptyResponseException.");
            }
            catch (TikCommandEmptyResponseException)
            {
                // expected
            }

            Assert.IsNull(await connection.CreateCommand("/ip/address/add").ExecuteScalarOrDefaultAsync());
        }

        [TestMethod]
        public async Task MoreThanOneRow_IsAmbiguous_ForTheSingleRowReaders()
        {
            var connection = OpenedConnection();
            connection.Rows = new List<TikRecordSentence> { Row(".id", "*1"), Row(".id", "*2") };

            try
            {
                await connection.CreateCommand("/ip/address/print").ExecuteSingleRowAsync();
                Assert.Fail("expected TikCommandAmbiguousResultException.");
            }
            catch (TikCommandAmbiguousResultException)
            {
                // expected
            }

            Assert.AreEqual(2, (await connection.CreateCommand("/ip/address/print").ExecuteListAsync()).Count);
        }

        [TestMethod]
        public async Task SingleRowOrDefault_IsNullWhenNothingMatched()
        {
            var connection = OpenedConnection();
            Assert.IsNull(await connection.CreateCommand("/ip/address/print").ExecuteSingleRowOrDefaultAsync());
        }

        // ── The fake connection consumers unit-test against ───────────────────

        [TestMethod]
        public async Task TikFakeConnection_SupportsTheAsyncSurface()
        {
            // Without this, a consumer who adopts the async API loses the ability to unit-test the code that
            // uses it — which would undercut the feature.
            var connection = new tik4net.Testing.TikFakeConnection()
                .WithResponse(rows => rows.First() == "/ip/address/print", new ITikSentence[]
                {
                    new tik4net.Testing.TikFakeReSentence(new Dictionary<string, string> { { ".id", "*1" }, { "address", "10.0.0.1/24" } }),
                    new tik4net.Testing.TikFakeDoneSentence(),
                });

            var rows = await connection.CreateCommand("/ip/address/print").ExecuteListAsync();

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("10.0.0.1/24", rows[0].GetResponseField("address"));
        }

        [TestMethod]
        public async Task TikFakeConnection_CanBeToldItHasNoAsyncSurface()
        {
            var connection = new tik4net.Testing.TikFakeConnection { Capabilities = TikConnectionCapability.Crud };

            try
            {
                await connection.CreateCommand("/ip/address/print").ExecuteListAsync();
                Assert.Fail("expected TikConnectionCapabilityNotSupportedException.");
            }
            catch (TikConnectionCapabilityNotSupportedException ex)
            {
                Assert.AreEqual(TikConnectionCapability.AsyncCommands, ex.Capability);
            }
        }
    }
}
