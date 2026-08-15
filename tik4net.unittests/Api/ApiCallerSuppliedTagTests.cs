using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// What happens when the <b>caller</b> supplies the <c>.tag</c> instead of letting the connection
    /// generate one.
    /// </summary>
    /// <remarks>
    /// The binary API is the only transport where <c>.tag</c> is a real wire word (CLI, REST and WinBox
    /// native all drop it as a client-side marker), so it is also the only one where a caller-supplied tag
    /// has to be understood rather than ignored. The two spellings differ by one character and reach the
    /// connection by different routes: the connection writes its own tag as the bare row <c>.tag=N</c>, while
    /// a caller passing it as a command parameter produces <c>=.tag=N</c>.
    /// </remarks>
    [TestClass]
    public class ApiCallerSuppliedTagTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static Task RunFakeRouter(FakeRouterServer server, Action<List<string>> onCommand)
            => Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();               // login
                server.WriteSentence("!done");
                onCommand(server.ReadSentence());
            });

        /// <summary>
        /// A caller-supplied tag must be the tag the answer is awaited on. Getting this wrong does not throw:
        /// the command goes out correctly tagged, the router answers it correctly, and the client waits for a
        /// tag nobody will ever send — a full receive timeout for an answer that arrived immediately.
        /// </summary>
        [TestMethod]
        public void CallerSuppliedTag_IsWhatTheAnswerIsAwaitedOn()
        {
            using var server = new FakeRouterServer();
            var serverTask = RunFakeRouter(server, command =>
            {
                Assert.IsTrue(command.Any(w => w.EndsWith(".tag=77", StringComparison.Ordinal)),
                    $"the caller's tag must reach the router: {string.Join(" ", command)}");
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}=77", "=name=tagged-reply");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}=77");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 2000;    // keep the failure short if the tag is not understood
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = connection.CallCommandSync(new[] { "/interface/print", "=.tag=77" }).ToList();

                Assert.AreEqual("tagged-reply", rows.OfType<ITikReSentence>().Single().GetResponseField("name"));
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        /// <summary>The connection's own spelling (a bare <c>.tag=N</c> row) keeps working.</summary>
        [TestMethod]
        public void ConnectionSpelledTag_StillWorks()
        {
            using var server = new FakeRouterServer();
            var serverTask = RunFakeRouter(server, command =>
            {
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}=88", "=name=bare-tag-reply");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}=88");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 2000;
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var rows = connection.CallCommandSync(new[] { "/interface/print", $"{TikSpecialProperties.Tag}=88" }).ToList();

                Assert.AreEqual("bare-tag-reply", rows.OfType<ITikReSentence>().Single().GetResponseField("name"));
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }
    }
}
