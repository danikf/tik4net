using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.integrationtests
{
    /// <summary>
    /// The Task-based command surface (<c>Execute*Async</c>) against a live router, for every transport that
    /// declares <see cref="TikConnectionCapability.AsyncCommands"/>.
    /// </summary>
    /// <remarks>
    /// The unit tests pin the shape of this surface against a transport double; what they cannot show is that a
    /// real router answers the same way through it. The assertions here are therefore comparative wherever
    /// possible — the async call must return <b>what the synchronous one returns</b>, on the same connection and
    /// the same data — because "it returned some rows" would still pass if the async path quietly read a
    /// different thing.
    /// <para>
    /// Transports without the capability report Inconclusive rather than failing: they have no async core yet,
    /// which is a scheduled gap (WinBox native is next), not a router refusal.
    /// </para>
    /// </remarks>
    [TestClass]
    public class AsyncCommandTest : TestBase
    {
        private const string Path = "/ip/firewall/filter";

        /// <summary>Rules created by the running test, newest first — drained in <see cref="OnCleanup"/>.</summary>
        private readonly Stack<string> _createdIds = new Stack<string>();

        private string _stamp;

        protected override void OnInitialize()
        {
            _stamp = "t4n-async-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        protected override void OnCleanup()
        {
            while (_createdIds.Count > 0)
            {
                string id = _createdIds.Pop();
                try
                {
                    var cmd = Connection.CreateCommand(Path + "/remove", TikCommandParameterFormat.NameValue);
                    cmd.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
                    cmd.ExecuteNonQuery();
                }
                catch { /* already removed by the test, or the connection is gone — never mask the outcome */ }
            }
        }

        // ── Reads ─────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task ExecuteListAsync_ReturnsWhatExecuteListReturns()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");

            var sync = Connection.CreateCommand("/interface/print").ExecuteList().ToList();
            var async = await Connection.CreateCommand("/interface/print").ExecuteListAsync();

            Assert.IsTrue(sync.Count > 0, "the router reported no interfaces at all — nothing to compare");
            CollectionAssert.AreEqual(
                sync.Select(r => r.GetId()).ToList(),
                async.Select(r => r.GetId()).ToList(),
                "the async read returned a different set of records than the synchronous one");
        }

        [TestMethod]
        public async Task ExecuteScalarAsync_ReadsTheSameFieldValue()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");

            string sync = Connection.CreateCommand("/system/identity/print").ExecuteScalar();
            string async = await Connection.CreateCommand("/system/identity/print").ExecuteScalarAsync();

            Assert.AreEqual(sync, async);
        }

        [TestMethod]
        public async Task ExecuteSingleRowAsync_ReadsOneRecord()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");

            var row = await Connection.CreateCommand("/system/resource/print").ExecuteSingleRowAsync();

            Assert.IsNotNull(row);
            Assert.IsTrue(row.Words.Count > 0, "/system/resource answered with an empty record");
        }

        [TestMethod]
        public async Task ExecuteSingleRowOrDefaultAsync_IsNullWhenNothingMatches()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");

            var cmd = Connection.CreateCommand(Path + "/print");
            cmd.AddParameter("comment", _stamp + "-nothing-matches-this", TikCommandParameterFormat.Filter);

            Assert.IsNull(await cmd.ExecuteSingleRowOrDefaultAsync());
        }

        // ── Writes ────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task AsyncCrudRoundTrip_TakesEffectOnTheRouter()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");

            // add
            string comment = _stamp + "-crud";
            var add = Connection.CreateCommand(Path + "/add", TikCommandParameterFormat.NameValue);
            add.AddParameter("chain", "forward");
            add.AddParameter("action", "accept");
            add.AddParameter("src-address", "192.0.2.90");
            add.AddParameter("comment", comment);
            add.AddParameter("disabled", "yes");
            string id = await add.ExecuteScalarAsync();
            Assert.IsFalse(string.IsNullOrEmpty(id), "the async add did not return the new record's .id");
            _createdIds.Push(id);

            // read it back — the assertion that the add really happened, not just that it answered
            var afterAdd = await ReadByCommentAsync(comment);
            Assert.IsNotNull(afterAdd, "the async add returned an .id but the rule is not on the router");
            Assert.AreEqual(id, afterAdd.GetId());

            // set
            var set = Connection.CreateCommand(Path + "/set", TikCommandParameterFormat.NameValue);
            set.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            set.AddParameter("src-address", "192.0.2.91");
            await set.ExecuteNonQueryAsync();

            var afterSet = await ReadByCommentAsync(comment);
            Assert.IsNotNull(afterSet);
            StringAssert.StartsWith(afterSet.GetResponseField("src-address"), "192.0.2.91",
                "the async set answered without error but did not change the record");

            // remove
            var remove = Connection.CreateCommand(Path + "/remove", TikCommandParameterFormat.NameValue);
            remove.AddParameter(TikSpecialProperties.Id, id, TikCommandParameterFormat.NameValue);
            await remove.ExecuteNonQueryAsync();
            _createdIds.Pop();

            Assert.IsNull(await ReadByCommentAsync(comment),
                "the async remove answered without error but the rule is still on the router");
        }

        // ── Cancellation ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task APreCancelledToken_SendsNothing_AndLeavesTheConnectionUsable()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                try
                {
                    await Connection.CreateCommand("/interface/print").ExecuteListAsync(cts.Token);
                    Assert.Fail("a token cancelled before dispatch must throw OperationCanceledException");
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
            }

            // The point of the test: nothing was written, so the very next command must work normally.
            var rows = await Connection.CreateCommand("/interface/print").ExecuteListAsync();
            Assert.IsTrue(rows.Count > 0);
        }

        [TestMethod]
        public async Task CancelInFlight_StopsWaiting_AndTheConnectionStillWorks()
        {
            EnsureCapability(TikConnectionCapability.CancelInFlight, "cancelling a command already on the wire");

            // A ping with an explicit count is a command that takes a known, bounded time — long enough to be
            // cancelled halfway, short enough that the router is free again before the test ends.
            var ping = Connection.CreateCommand("/ping", TikCommandParameterFormat.NameValue);
            ping.AddParameter("address", "127.0.0.1");
            ping.AddParameter("count", "5");

            var stopwatch = Stopwatch.StartNew();
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
            {
                try
                {
                    await ping.ExecuteListAsync(cts.Token);
                    Assert.Fail("the command completed instead of being cancelled — it was expected to run ~5 s");
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
            }
            stopwatch.Stop();
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(4),
                $"cancel returned control after {stopwatch.Elapsed.TotalSeconds:F1} s — a transport declaring "
                + "CancelInFlight must not wait for the command to finish");

            // What actually matters about an in-flight cancel: the connection is still consistent afterwards.
            // On REST this can block until the router finishes the ping it is still running — cancelling frees
            // the caller, not the router (Docs/findings-rest-api.md §12.1).
            string identity = await Connection.CreateCommand("/system/identity/print").ExecuteScalarAsync();
            Assert.IsFalse(string.IsNullOrEmpty(identity),
                "the connection was left unusable by the cancel");
        }

        [TestMethod]
        public async Task WithoutCancelInFlight_TheCancelWaitsForTheAnswer_AndTheConnectionStaysInStep()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "Execute*Async");
            if (Connection.Supports(TikConnectionCapability.CancelInFlight))
                Assert.Inconclusive("this transport cancels for real — covered by CancelInFlight_StopsWaiting");

            // The other half of the cancellation contract, and the half that can only be observed live: on the
            // CLI family the token does NOT cut the read, because the response is an unframed byte stream and
            // abandoning it would leave output for the next command to read as its own. So the call is expected
            // to take as long as the command does — and the assertion that matters is the one after it.
            var ping = Connection.CreateCommand("/ping", TikCommandParameterFormat.NameValue);
            ping.AddParameter("address", "127.0.0.1");
            ping.AddParameter("count", "3");

            var stopwatch = Stopwatch.StartNew();
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            {
                try
                {
                    await ping.ExecuteListAsync(cts.Token);
                    Assert.Fail("the cancel was swallowed — it must surface once the response has been drained");
                }
                catch (OperationCanceledException)
                {
                    // expected: reported at the safe point, not at the moment the token fired
                }
            }
            stopwatch.Stop();
            Assert.IsTrue(stopwatch.Elapsed > TimeSpan.FromSeconds(2),
                $"the call returned after {stopwatch.Elapsed.TotalSeconds:F1} s, i.e. before a 3-count ping can "
                + "have finished — the read was abandoned, and the unread output will be misread as the next "
                + "command's answer");

            // The whole reason for waiting: the channel is back in step, so the next command gets ITS answer.
            string identity = Connection.CreateCommand("/system/identity/print").ExecuteScalar();
            var again = await Connection.CreateCommand("/system/identity/print").ExecuteScalarAsync();
            Assert.AreEqual(identity, again, "the connection was left out of step by the cancel");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<ITikReSentence> ReadByCommentAsync(string comment)
        {
            var cmd = Connection.CreateCommand(Path + "/print");
            cmd.AddParameter("comment", comment, TikCommandParameterFormat.Filter);
            return await cmd.ExecuteSingleRowOrDefaultAsync();
        }
    }
}
