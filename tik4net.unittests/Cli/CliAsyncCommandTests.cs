using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The Task-based command surface on the CLI family (P2.2 step 3), driven against a scripted terminal
    /// instead of a router.
    /// </summary>
    /// <remarks>
    /// What needs a test rather than a review is the <b>cancellation contract</b>, because every part of it is
    /// invisible from the outside. A terminal answers with an unframed byte stream, so the library must not
    /// hand the caller's token to the read: doing so would abort mid-response and leave output that the
    /// <i>next</i> command reads as its own — which does not throw, it returns the wrong answer. The assertions
    /// below therefore check <i>which</i> token the transport was given and whether the response was drained,
    /// not just that <c>OperationCanceledException</c> came out; a naive implementation that forwards the token
    /// passes every test that only looks at the exception.
    /// </remarks>
    [TestClass]
    public class CliAsyncCommandTests
    {
        // ── The terminal double ───────────────────────────────────────────────

        /// <summary>
        /// A <see cref="CliConnectionBase"/> whose transport is a scripted delegate: it records the command
        /// text and the token it was handed, and answers from <see cref="Reply"/>.
        /// </summary>
        private sealed class ScriptedCliConnection : CliConnectionBase
        {
            public readonly List<string> Sent = new List<string>();
            public readonly List<CancellationToken> SeenTokens = new List<CancellationToken>();
            public string Reply = string.Empty;
            public bool Closed { get; private set; }

            /// <summary>Raised before the reply is produced — the test's hook for "the command is in flight".</summary>
            public Func<CancellationToken, Task> BeforeReply;

            protected override string TransportName => "Scripted";

            public void OpenScripted()
                => OpenWith(_ => Task.FromResult(0), SendAsync,
                    (raw, ct) => Task.FromResult(string.Empty),
                    () => Closed = true);

            private async Task<string> SendAsync(string cliText, CancellationToken ct)
            {
                Sent.Add(cliText);
                SeenTokens.Add(ct);
                if (BeforeReply != null)
                    await BeforeReply(ct).ConfigureAwait(false);
                return Reply;
            }

            public override void Open(string host, string user, string password) => OpenScripted();
            public override void Open(string host, int port, string user, string password) => OpenScripted();
            public override Task OpenAsync(string host, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
            public override Task OpenAsync(string host, int port, string user, string password, CancellationToken cancellationToken = default) { OpenScripted(); return Task.FromResult(0); }
        }

        private static ScriptedCliConnection OpenConnection(string reply = "")
        {
            var conn = new ScriptedCliConnection { Reply = reply };
            conn.OpenScripted();
            return conn;
        }

        // ── The surface exists on every CLI transport ─────────────────────────

        [TestMethod]
        public void CliDeclaresAsyncCommands_ButNotCancelInFlight()
        {
            using (var conn = OpenConnection())
            {
                Assert.IsTrue(conn.Supports(TikConnectionCapability.AsyncCommands),
                    "the CLI transports await their socket — the Task-based surface is real, not a Task.Run façade");
                Assert.IsFalse(conn.Supports(TikConnectionCapability.CancelInFlight),
                    "a terminal byte stream has no resync point, so an in-flight cancel can never be offered here");
            }
        }

        [TestMethod]
        public async Task ExecuteListAsync_ReadsThroughTheAsyncPath()
        {
            using (var conn = OpenConnection(".id=*1;name=ether1"))
            {
                var rows = await conn.CreateCommand("/interface/print").ExecuteListAsync();

                Assert.AreEqual(1, rows.Count);
                Assert.AreEqual("ether1", rows[0].GetResponseField("name"));
                Assert.AreEqual(1, conn.Sent.Count);
                StringAssert.Contains(conn.Sent[0], "/interface print");
            }
        }

        [TestMethod]
        public async Task AsyncWrites_BuildTheSameCliTextAsTheSyncOnes()
        {
            // The sync hooks now block on the async ones, so there is exactly one command builder in play.
            // Pinning the text keeps that true: a second, drifting build path is what this collapses.
            using (var async = OpenConnection("*7"))
            using (var sync = OpenConnection("*7"))
            {
                var addAsync = async.CreateCommand("/ip/address/add", TikCommandParameterFormat.NameValue);
                addAsync.AddParameter("address", "192.0.2.1/24");
                Assert.AreEqual("*7", await addAsync.ExecuteScalarAsync());

                var addSync = sync.CreateCommand("/ip/address/add", TikCommandParameterFormat.NameValue);
                addSync.AddParameter("address", "192.0.2.1/24");
                Assert.AreEqual("*7", addSync.ExecuteScalar());

                CollectionAssert.AreEqual(sync.Sent, async.Sent);
            }
        }

        // ── Level 0 — cancelled before dispatch ───────────────────────────────

        [TestMethod]
        public async Task APreCancelledToken_WritesNothing()
        {
            using (var conn = OpenConnection(".id=*1"))
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await AssertCancelled(() => conn.CreateCommand("/interface/print").ExecuteListAsync(cts.Token));

                Assert.AreEqual(0, conn.Sent.Count, "a token cancelled before dispatch must not put bytes on the wire");
                Assert.IsTrue(conn.IsOpened, "nothing was sent, so there was nothing to resynchronize");
            }
        }

        // ── Cooperative (default) — the response is drained first ─────────────

        [TestMethod]
        public async Task Cooperative_DoesNotHandTheCallersTokenToTheRead()
        {
            using (var conn = OpenConnection(".id=*1"))
            using (var cts = new CancellationTokenSource())
            {
                await conn.CreateCommand("/interface/print").ExecuteListAsync(cts.Token);

                Assert.AreEqual(1, conn.SeenTokens.Count);
                Assert.IsFalse(conn.SeenTokens[0].CanBeCanceled,
                    "the transport read was handed a cancellable token — it could then abort mid-response and "
                    + "leave output for the next command to read as its own");
            }
        }

        [TestMethod]
        public async Task Cooperative_CancelInFlight_IsDeferredUntilTheResponseIsDrained()
        {
            using (var conn = OpenConnection(".id=*1;name=ether1"))
            using (var cts = new CancellationTokenSource())
            {
                bool drained = false;
                conn.BeforeReply = async ct =>
                {
                    cts.Cancel();                                   // fires while the command is on the wire
                    // Deliberately honours the token it was GIVEN: an implementation that forwarded the
                    // caller's token would abort here and never set `drained` — which is the whole failure
                    // this test exists to catch.
                    await Task.Delay(20, ct).ConfigureAwait(false);
                    drained = true;                                 // the router finished answering
                };

                await AssertCancelled(() => conn.CreateCommand("/interface/print").ExecuteListAsync(cts.Token));

                Assert.IsTrue(drained, "the read was abandoned instead of being drained to the end of the response");
                Assert.IsTrue(conn.IsOpened, "a cooperative cancel must leave the connection usable");
                Assert.IsFalse(conn.Closed);

                // The point of draining: the next command gets its own answer.
                conn.BeforeReply = null;
                var rows = await conn.CreateCommand("/interface/print").ExecuteListAsync();
                Assert.AreEqual("ether1", rows[0].GetResponseField("name"));
            }
        }

        // ── AbandonAndClose — opt in to losing the connection ─────────────────

        [TestMethod]
        public async Task AbandonAndClose_CutsTheReadAndClosesTheConnection()
        {
            using (var conn = OpenConnection(".id=*1"))
            using (var cts = new CancellationTokenSource())
            {
                conn.CancellationMode = TikCancellationMode.AbandonAndClose;
                conn.BeforeReply = async ct =>
                {
                    cts.Cancel();
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);  // the router never answers
                };

                await AssertCancelled(() => conn.CreateCommand("/interface/print").ExecuteListAsync(cts.Token));

                Assert.IsTrue(conn.SeenTokens[0].CanBeCanceled,
                    "AbandonAndClose is precisely the mode that gives the read the caller's token");
                Assert.IsTrue(conn.Closed, "the unread response cannot be resynchronized, so the session must be closed");
                Assert.IsFalse(conn.IsOpened);

                // Not a silent skip: the connection says it is gone rather than answering from a stale channel.
                Assert.ThrowsException<TikConnectionNotOpenException>(
                    () => conn.CreateCommand("/interface/print").ExecuteList());
            }
        }

        [TestMethod]
        public void CooperativeIsTheDefault()
        {
            using (var conn = OpenConnection())
                Assert.AreEqual(TikCancellationMode.Cooperative, conn.CancellationMode,
                    "the safe mode must be the one a caller gets without knowing the flag exists");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static async Task AssertCancelled(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Assert.Fail("expected OperationCanceledException");
        }
    }
}
