// ApiBoundedReadEndingTests.cs — how the binary API's bounded reads end badly (command-layer D10): one rule for all
// three harness users. A router error is the classified trap (TikTrapClassifier), a lost connection is
// TikCommandFatalException; where a method reports either as TikCommandAbortException, the classified trap or the
// fatal is its InnerException.
//
// The trap answers are the router's own, recorded live (RouterOS 7.24.5, read-only):
//   /interface/ethernet/monitor =numbers=*FFFF =once=  →  !trap =category=4 =message=no such item (4), !empty, !done
//   /tool/ping … =interface=no-such-interface          →  !trap =message=input does not match any value of interface, !done
// A lost connection cannot be produced read-only; it is scripted as a !fatal addressed to the command followed by the
// socket closing (the reason as a bare word, as the live /quit answer shows), and as a socket closed without one.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiBoundedReadEndingTests
    {
        private const string NoSuchItem = "no such item (4)";
        private const string NoMatch = "input does not match any value of interface";

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        private static void NoSuchItemTrap(FakeRouterServer s, string tag)
        {
            s.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");
            s.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=category=4", "=message=" + NoSuchItem);
            s.WriteSentence("!empty", $"{TikSpecialProperties.Tag}={tag}");
            s.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");
        }

        private static void GenericTrap(FakeRouterServer s, string tag)
        {
            s.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=message=" + NoMatch);
            s.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");
        }

        private static void FatalThenClose(FakeRouterServer s, string tag)
        {
            s.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");
            s.WriteSentence("!fatal", "router going down", $"{TikSpecialProperties.Tag}={tag}");
            s.CloseClientConnection();
        }

        private static void CloseWithoutFatal(FakeRouterServer s, string tag)
        {
            s.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=rx=1");
            s.CloseClientConnection();
        }

        /// <summary>Logs in, runs <paramref name="act"/> and answers its one command with <paramref name="reply"/>.</summary>
        private static async Task ExchangeAsync(Func<ApiConnection, Task> act, Action<FakeRouterServer, string> reply,
            bool connectionSurvives)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");
                server.EchoTags = false;                     // every reply below names its tag, or has none (!fatal)

                reply(server, ExtractTag(server.ReadSentence()));
                if (!connectionSurvives)
                    return;

                while (true)
                {
                    var next = server.ReadSentence();
                    if (next[0] == "/cancel")                // a stream ended by a trap may still cancel its command
                    {
                        server.WriteSentence("!done", TikSpecialProperties.Tag + "=" + ExtractTag(next));
                        continue;
                    }
                    server.WriteSentence("!fatal", "session terminated on request",   // /quit
                        TikSpecialProperties.Tag + "=" + ExtractTag(next));
                    return;
                }
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "");
                await act(connection).ConfigureAwait(false);
                if (connectionSurvives)
                    connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        private static void Exchange(Action<ApiConnection> act, Action<FakeRouterServer, string> reply, bool connectionSurvives)
            => ExchangeAsync(c => { act(c); return Task.CompletedTask; }, reply, connectionSurvives).GetAwaiter().GetResult();

        // ── ExecuteListUntilDone ──────────────────────────────────────────────

        [TestMethod]
        public void UntilDone_NoSuchItem_IsTheClassifiedTrap()
        {
            Exchange(c =>
            {
                var ex = Assert.ThrowsException<TikNoSuchItemException>(
                    () => c.CreateCommand("/interface/ethernet/monitor").ExecuteListUntilDone(TimeSpan.FromSeconds(10)).ToList());
                Assert.AreEqual(NoSuchItem, ex.Message);
                Assert.AreEqual("4", ex.Code);
            }, NoSuchItemTrap, connectionSurvives: true);
        }

        [TestMethod]
        public void UntilDone_AnUnclassifiedTrap_IsTheBaseTrap()
        {
            Exchange(c =>
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(
                    () => c.CreateCommand("/tool/ping").ExecuteListUntilDone(TimeSpan.FromSeconds(10)).ToList());
                Assert.AreEqual(typeof(TikCommandTrapException), ex.GetType());
                Assert.AreEqual(NoMatch, ex.Message);
            }, GenericTrap, connectionSurvives: true);
        }

        [TestMethod]
        public void UntilDone_AFatal_IsTikCommandFatalException()
        {
            Exchange(c =>
            {
                var ex = Assert.ThrowsException<TikCommandFatalException>(
                    () => c.CreateCommand("/tool/ping").ExecuteListUntilDone(TimeSpan.FromSeconds(10)).ToList());
                StringAssert.StartsWith(ex.Message, "Connection has been closed: router going down");
            }, FatalThenClose, connectionSurvives: false);
        }

        [TestMethod]
        public void UntilDone_ASocketClosedWithoutAFatal_IsTikCommandFatalException()
        {
            Exchange(c => Assert.ThrowsException<TikCommandFatalException>(
                    () => c.CreateCommand("/tool/ping").ExecuteListUntilDone(TimeSpan.FromSeconds(10)).ToList()),
                CloseWithoutFatal, connectionSurvives: false);
        }

        // ── ExecuteListWithDuration ───────────────────────────────────────────

        [TestMethod]
        public void WithDuration_Trap_IsAnAbortCarryingTheClassifiedTrap()
        {
            Exchange(c =>
            {
                var ex = Assert.ThrowsException<TikCommandAbortException>(
                    () => c.CreateCommand("/interface/ethernet/monitor").ExecuteListWithDuration(TimeSpan.FromSeconds(30)).ToList());
                Assert.AreEqual(NoSuchItem, ex.Message);
                Assert.IsInstanceOfType(ex.InnerException, typeof(TikNoSuchItemException));
            }, NoSuchItemTrap, connectionSurvives: true);
        }

        [TestMethod]
        public void WithDuration_LostConnection_IsAnAbortCarryingTheFatal()
        {
            Exchange(c =>
            {
                var ex = Assert.ThrowsException<TikCommandAbortException>(
                    () => c.CreateCommand("/tool/torch").ExecuteListWithDuration(TimeSpan.FromSeconds(30)).ToList());
                StringAssert.StartsWith(ex.Message, "router going down");
                Assert.IsInstanceOfType(ex.InnerException, typeof(TikCommandFatalException));
            }, FatalThenClose, connectionSurvives: false);
        }

        [TestMethod]
        public void WithDuration_OutOverload_StillReportsWithoutThrowing()
        {
            Exchange(c =>
            {
                var rows = c.CreateCommand("/interface/ethernet/monitor")
                    .ExecuteListWithDuration(TimeSpan.FromSeconds(30), out bool aborted, out string reason).ToList();
                Assert.IsTrue(aborted);
                Assert.AreEqual(NoSuchItem, reason);
                Assert.AreEqual(1, rows.Count);
            }, NoSuchItemTrap, connectionSurvives: true);
        }

#if NET8_0_OR_GREATER
        // ── The stream ────────────────────────────────────────────────────────

        private static async Task<TikCommandAbortException> StreamFails(ApiConnection c, string path, bool untilDone)
        {
            var cmd = c.CreateCommand(path);
            return await Assert.ThrowsExceptionAsync<TikCommandAbortException>(async () =>
            {
                var stream = untilDone
                    ? cmd.ExecuteListUntilDoneAsync(TimeSpan.FromSeconds(20))
                    : cmd.ExecuteListWithDurationAsync(TimeSpan.FromSeconds(30));
                await foreach (var unused in stream)
                {
                }
            });
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task Stream_Trap_IsAnAbortCarryingTheClassifiedTrap(bool untilDone)
        {
            await ExchangeAsync(async c =>
            {
                var ex = await StreamFails(c, "/interface/ethernet/monitor", untilDone);
                Assert.AreEqual(NoSuchItem, ex.Message);
                Assert.IsInstanceOfType(ex.InnerException, typeof(TikNoSuchItemException));
            }, NoSuchItemTrap, connectionSurvives: true);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task Stream_LostConnection_IsAnAbortCarryingTheFatal(bool untilDone)
        {
            await ExchangeAsync(async c =>
            {
                var ex = await StreamFails(c, "/tool/torch", untilDone);
                StringAssert.StartsWith(ex.Message, "router going down");
                Assert.IsInstanceOfType(ex.InnerException, typeof(TikCommandFatalException));
            }, FatalThenClose, connectionSurvives: false);
        }
#endif
    }
}
