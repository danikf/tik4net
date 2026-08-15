using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The two bounded streaming reads on the binary API — <c>ExecuteListWithDuration</c> and
    /// <c>ExecuteListUntilDone</c> — against a scripted router (P2.4, F7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both used to wait by sleeping 100 ms at a time and re-checking flags. The tests here are about what
    /// that cost: an end was noticed up to a tick late, and the reason the command ended — the router's
    /// <c>!trap</c> message, or the reason carried by the reader loop's <c>!fatal</c> — was dropped in favour
    /// of the words "Cancelled" and "Connection has been closed".
    /// </para>
    /// <para>
    /// Everything below is deterministic and router-free: the peer is <see cref="FakeRouterServer"/>, so the
    /// paths that only a dying or refusing router produces can be tested at all.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiStreamingReadTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        // ── ExecuteListWithDuration ───────────────────────────────────────────

        /// <summary>
        /// A command that finishes early must return when IT finished, not when the next poll tick noticed.
        /// The duration is a ceiling, and asking for a 30 s ceiling must not cost 30 s — nor 100 ms.
        /// </summary>
        [TestMethod]
        public void ExecuteListWithDuration_EarlyDone_ReturnsWhenTheRouterIsDone()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=address=127.0.0.1");
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=address=127.0.0.2");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                bool wasAborted;
                string abortReason;
                var rows = connection.CreateCommand("/tool/traceroute")
                    .ExecuteListWithDuration(30, out wasAborted, out abortReason).ToList();
                sw.Stop();

                Assert.AreEqual(2, rows.Count);
                Assert.IsFalse(wasAborted, "!done before the duration is a natural end, not an abort.");
                Assert.IsNull(abortReason);
                Assert.IsTrue(sw.ElapsedMilliseconds < 2000,
                    $"Returned after {sw.ElapsedMilliseconds} ms for a command the router had already finished.");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// A <c>!trap</c> must reach the caller as the router's own message. It used to be overwritten by the
        /// literal "Cancelled" one <c>if</c> later in the poll loop — the router said why, and the caller was
        /// told nothing.
        /// </summary>
        [TestMethod]
        public void ExecuteListWithDuration_Trap_AbortReasonIsTheRouterMessage()
        {
            const string RouterMessage = "input does not match any value of interface";

            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=message=" + RouterMessage);
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                bool wasAborted;
                string abortReason;
                connection.CreateCommand("/tool/torch").ExecuteListWithDuration(30, out wasAborted, out abortReason);

                Assert.IsTrue(wasAborted);
                Assert.AreEqual(RouterMessage, abortReason);

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// A connection that dies mid-stream ends the read through the reader loop's <c>!fatal</c>, and the
        /// reason it carries (P2.14) is what the caller is told — not a generic "connection has been closed".
        /// </summary>
        [TestMethod]
        public void ExecuteListWithDuration_ConnectionLost_AbortReasonCarriesTheCause()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=address=127.0.0.1");
                server.CloseClientConnection();              // router vanished mid-stream
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                bool wasAborted;
                string abortReason;
                var rows = connection.CreateCommand("/tool/torch")
                    .ExecuteListWithDuration(30, out wasAborted, out abortReason).ToList();
                sw.Stop();

                Assert.IsTrue(wasAborted);
                Assert.IsNotNull(abortReason);
                Assert.AreNotEqual("Cancelled", abortReason, "A lost connection is not a cancellation.");
                Assert.AreNotEqual("Connection has been closed", abortReason,
                    "The old poll loop read IsOpened and could only say this much; the !fatal knows more.");
                StringAssert.Contains(abortReason, "connection lost",
                    "abortReason must carry the reason the reader loop attached to the !fatal (P2.14).");
                Assert.AreEqual(1, rows.Count, "Rows the router had already sent stay the caller's.");
                Assert.IsTrue(sw.ElapsedMilliseconds < 2000,
                    $"Waited {sw.ElapsedMilliseconds} ms after the connection was already gone.");
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// The duration is still a real ceiling for a command that never ends: it is spent, the command is
        /// cancelled, and what arrived meanwhile is returned without an abort — this is the normal end of a
        /// bounded streaming read (<c>/tool/torch</c>).
        /// </summary>
        [TestMethod]
        public void ExecuteListWithDuration_NeverEnding_SpendsTheDurationThenCancels()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");
                AnswerCancel(server, tag);

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                bool wasAborted;
                string abortReason;
                var rows = connection.CreateCommand("/tool/torch")
                    .ExecuteListWithDuration(1, out wasAborted, out abortReason).ToList();
                sw.Stop();

                Assert.IsFalse(wasAborted);
                Assert.IsNull(abortReason);
                Assert.AreEqual(1, rows.Count);
                Assert.IsTrue(sw.ElapsedMilliseconds >= 900,
                    $"The duration is a budget to spend, not a ceiling to return early from ({sw.ElapsedMilliseconds} ms).");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        // ── ExecuteListUntilDone ──────────────────────────────────────────────

        [TestMethod]
        public void ExecuteListUntilDone_ReturnsWhenDoneArrives()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=address=127.0.0.1");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                var rows = connection.CreateCommand("/tool/traceroute").ExecuteListUntilDone().ToList();
                sw.Stop();

                Assert.AreEqual(1, rows.Count);
                Assert.IsTrue(sw.ElapsedMilliseconds < 2000,
                    $"Returned {sw.ElapsedMilliseconds} ms after a !done that had already arrived.");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void ExecuteListUntilDone_Trap_ThrowsTrapExceptionWithTheRouterMessage()
        {
            const string RouterMessage = "no such command";

            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=message=" + RouterMessage);
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var ex = Assert.ThrowsException<TikCommandTrapException>(
                    () => connection.CreateCommand("/tool/traceroute").ExecuteListUntilDone().ToList());
                StringAssert.Contains(ex.Message, RouterMessage);

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void ExecuteListUntilDone_ConnectionLost_ThrowsIOExceptionCarryingTheCause()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=address=127.0.0.1");
                server.CloseClientConnection();
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var ex = Assert.ThrowsException<IOException>(
                    () => connection.CreateCommand("/tool/traceroute").ExecuteListUntilDone().ToList());
                StringAssert.Contains(ex.Message, "Connection has been closed");
                Assert.IsTrue(ex.Message.Length > "Connection has been closed.".Length,
                    "The reason the reader loop carried must survive into the message, was: " + ex.Message);
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>
        /// The safety timeout still cancels the command on the router and reports the abort — and the
        /// connection is left usable, which is the property that matters after any cancel.
        /// </summary>
        [TestMethod]
        public void ExecuteListUntilDone_Timeout_CancelsAndThrowsAbort()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                string tag = ExtractTag(server.ReadSentence());
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");
                AnswerCancel(server, tag);

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var sw = Stopwatch.StartNew();
                var ex = Assert.ThrowsException<TikCommandAbortException>(
                    () => connection.CreateCommand("/tool/torch").ExecuteListUntilDone(timeoutSec: 1).ToList());
                sw.Stop();

                StringAssert.Contains(ex.Message, "1 second");
                Assert.IsTrue(sw.ElapsedMilliseconds >= 900 && sw.ElapsedMilliseconds < 4000,
                    $"A 1 s timeout took {sw.ElapsedMilliseconds} ms.");

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        // Plays the router's side of /cancel tag=N: the cancelled command gets the interrupted trap plus its
        // own !done, and the /cancel sentence gets a !done on its own tag (which is what the client waits for).
        private static void AnswerCancel(FakeRouterServer server, string cancelledTag)
        {
            string cancelTag = ExtractTag(server.ReadSentence());
            server.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={cancelledTag}", "=category=2", "=message=interrupted");
            server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={cancelledTag}");
            server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={cancelTag}");
        }
    }
}
