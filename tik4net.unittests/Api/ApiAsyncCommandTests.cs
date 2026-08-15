using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The Task-based command surface on the binary API (job B of the P2.1 design), against a scripted
    /// router.
    /// </summary>
    /// <remarks>
    /// The API is the one transport that can cancel a command that is already running: <c>/cancel tag=N</c>
    /// is part of the protocol, so the router itself stops and the sentence stream stays framed. That makes
    /// the assertion worth writing not "did it throw <see cref="OperationCanceledException"/>" — a client
    /// that simply stopped reading would pass that — but "did the router get told, and does the connection
    /// still work afterwards".
    /// </remarks>
    [TestClass]
    public class ApiAsyncCommandTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        [TestMethod]
        public void ApiDeclaresAsyncCommandsAndCancelInFlight()
        {
            using (var connection = new ApiConnection(false))
            {
                Assert.IsTrue(connection.Supports(TikConnectionCapability.AsyncCommands));
                Assert.IsTrue(connection.Supports(TikConnectionCapability.CancelInFlight),
                    "The API has /cancel tag=N — the only real in-flight cancel in the transport family.");
            }
        }

        [TestMethod]
        public async Task ExecuteListAsync_ReturnsTheRows()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var cmd = server.ReadSentence();
                string tag = ExtractTag(cmd);
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=name=ether1");
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=name=ether2");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = await connection.CreateCommand("/interface/print").ExecuteListAsync();

                CollectionAssert.AreEqual(new[] { "ether1", "ether2" },
                    rows.Select(r => r.GetResponseField("name")).ToArray());
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// An async command is always tagged, even with <see cref="ApiConnection.SendTagWithSyncCommand"/>
        /// left at its default — an untagged command cannot be cancelled, because <c>/cancel</c> addresses a
        /// tag.
        /// </summary>
        [TestMethod]
        public async Task AsyncCommandsAreAlwaysTagged()
        {
            using var server = new FakeRouterServer();
            List<string> command = null;

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                command = server.ReadSentence();
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={ExtractTag(command)}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                Assert.IsFalse(connection.SendTagWithSyncCommand, "precondition: tagging is off by default");

                await connection.CreateCommand("/system/identity/set").ExecuteNonQueryAsync();

                Assert.IsTrue(command.Any(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal)),
                    "The async path must tag its command regardless of SendTagWithSyncCommand.");
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>Level 0: a token that is already cancelled must put nothing on the wire.</summary>
        [TestMethod]
        public async Task PreCancelledToken_WritesNothing()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var next = server.ReadSentence();            // must be /quit, NOT the cancelled command
                Assert.AreEqual("/quit", next[0]);
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                        () => connection.CreateCommand("/interface/print").ExecuteListAsync(cts.Token));
                }

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// Level 2: cancelling a running command sends <c>/cancel tag=N</c>, the router's
        /// <c>!trap interrupted</c> + <c>!done</c> are consumed, and — the part that matters — the very same
        /// connection answers the next command correctly.
        /// </summary>
        [TestMethod]
        public async Task CancellingARunningCommand_AsksTheRouter_AndLeavesTheConnectionUsable()
        {
            using var server = new FakeRouterServer();
            var firstRowSent = new ManualResetEventSlim(false);
            var cancelSeen = new ManualResetEventSlim(false);
            List<string> cancelSentence = null;
            string streamTag = null;

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var streaming = server.ReadSentence();
                streamTag = ExtractTag(streaming);
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={streamTag}", "=seq=0");
                firstRowSent.Set();

                // The client should now ask us to stop.
                cancelSentence = server.ReadSentence();
                cancelSeen.Set();
                server.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={streamTag}", "=category=2", "=message=interrupted");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={streamTag}");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={ExtractTag(cancelSentence)}");

                // …and the connection must still be good for ordinary work.
                var after = server.ReadSentence();
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={ExtractTag(after)}", "=name=still-here");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={ExtractTag(after)}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                using (var cts = new CancellationTokenSource())
                {
                    var running = connection.CreateCommand("/interface/monitor-traffic").ExecuteListAsync(cts.Token);

                    Assert.IsTrue(firstRowSent.Wait(5000));
                    cts.Cancel();

                    await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => running);
                }

                Assert.IsTrue(cancelSeen.Wait(5000), "The router was never asked to cancel.");
                Assert.AreEqual("/cancel", cancelSentence[0]);
                // `=tag=N`, not `=.tag=N`: the tag being cancelled is an ARGUMENT of /cancel, not this
                // sentence's own tag. RouterOS 7.23.2 was measured to accept both, so this assertion is not
                // about what the router tolerates — it pins the spelling to the one ApiCommand.CancelInternal
                // has used since 3.x, so the two cancel paths cannot drift apart.
                Assert.IsTrue(cancelSentence.Any(w => w == $"=tag={streamTag}"),
                    $"/cancel must name the running command's tag; sent: {string.Join(" ", cancelSentence)}");

                var rows = await connection.CreateCommand("/system/identity/print").ExecuteListAsync();
                Assert.AreEqual("still-here", rows.Single().GetResponseField("name"),
                    "A cancelled command must leave the connection framed and usable — that is what makes " +
                    "CancelInFlight a capability rather than a hopeful abandon.");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// Two async commands in flight at once, answered out of order: each awaits its own tag. The sync
        /// surface has had this since tagging existed; the async one must not have quietly lost it.
        /// </summary>
        [TestMethod]
        public async Task ConcurrentAsyncCommands_EachGetTheirOwnRows()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                // Answer each command with its OWN path as the payload, second one first: the assertion is
                // then about which caller received which answer, whatever order the two hit the wire in.
                var cmdA = server.ReadSentence();
                var cmdB = server.ReadSentence();

                foreach (var cmd in new[] { cmdB, cmdA })
                {
                    string tag = ExtractTag(cmd);
                    server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", $"=name=answered-{cmd[0].Trim('/').Replace("/", "-")}");
                    server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");
                }

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var first = connection.CreateCommand("/one/print").ExecuteListAsync();
                var second = connection.CreateCommand("/two/print").ExecuteListAsync();

                var rows1 = await first;
                var rows2 = await second;

                Assert.AreEqual("answered-one-print", rows1.Single().GetResponseField("name"));
                Assert.AreEqual("answered-two-print", rows2.Single().GetResponseField("name"));

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }
    }
}
