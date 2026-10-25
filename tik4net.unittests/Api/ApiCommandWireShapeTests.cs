// ApiCommandWireShapeTests.cs — the sentence each Execute* method of ApiCommand writes, and the answers the
// command-layer review changes on purpose later.
//
// Characterisation tests: they pin today's behaviour, including the asymmetries X3, X4 and X5. A later fix shows
// up here as an assertion changed in its own commit. The transport-neutral command's half of the same method set
// is GenericCommandShapeTests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiCommandWireShapeTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        /// <summary>
        /// Logs in, runs <paramref name="act"/>, answers the one command it sends with <paramref name="reply"/>
        /// and returns that command's words, <c>.tag</c> removed. With <paramref name="connectionSurvives"/>
        /// false the reply ends the session and the client sends no <c>/quit</c>.
        /// </summary>
        private static List<string> Exchange(Action<ApiConnection> act, Action<FakeRouterServer, string> reply,
            bool connectionSurvives = true)
        {
            using var server = new FakeRouterServer();
            List<string> command = null;
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                command = server.ReadSentence();
                while (command[0] == "/console/inspect")     // a filtered print asks for the menu's fields first
                {
                    Done(server, ExtractTag(command));
                    command = server.ReadSentence();
                }
                reply(server, ExtractTag(command));
                if (!connectionSurvives)
                    return;

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                act(connection);
                if (connectionSurvives)
                    connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
            return command.Where(w => !w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal)).ToList();
        }

        private static void Done(FakeRouterServer server, string tag)
            => server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

        private static void OneRowThenDone(FakeRouterServer server, string tag)
        {
            server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=name=a");
            Done(server, tag);
        }

        private static ITikCommand Command(ApiConnection connection, string commandText)
            => connection.CreateCommandAndParameters(commandText, "name", "a", "comment", "b");

        private static void AssertWords(List<string> actual, params string[] expected)
            => CollectionAssert.AreEqual(expected, actual, "sent: " + string.Join(" ", actual));

        // ── What each method writes ───────────────────────────────────────────

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteNonQuery_WritesNameValueWords(bool async)
        {
            var words = Exchange(c =>
            {
                var cmd = Command(c, "/ip/address/set");
                if (async) cmd.ExecuteNonQueryAsync().GetAwaiter().GetResult();
                else cmd.ExecuteNonQuery();
            }, Done);

            AssertWords(words, "/ip/address/set", "=name=a", "=comment=b");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalar_WritesNameValueWords(bool async)
        {
            var words = Exchange(c =>
            {
                var cmd = Command(c, "/system/script/run");
                if (async) cmd.ExecuteScalarAsync().GetAwaiter().GetResult();
                else cmd.ExecuteScalar();
            }, (s, tag) => s.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}", "=ret=x"));

            AssertWords(words, "/system/script/run", "=name=a", "=comment=b");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteScalarOfAField_AddsAProplistWord(bool async)
        {
            var words = Exchange(c =>
            {
                var cmd = Command(c, "/ip/address/print");
                if (async) cmd.ExecuteScalarAsync("name").GetAwaiter().GetResult();
                else cmd.ExecuteScalar("name");
            }, OneRowThenDone);

            AssertWords(words, "/ip/address/print", "=name=a", "=comment=b", "=.proplist=name");
        }

        [DataTestMethod]
        [DataRow("ExecuteSingleRow")]
        [DataRow("ExecuteSingleRowOrDefault")]
        [DataRow("ExecuteList")]
        [DataRow("ExecuteSingleRowAsync")]
        [DataRow("ExecuteSingleRowOrDefaultAsync")]
        [DataRow("ExecuteListAsync")]
        public void TheReads_WriteFilterWords(string method)
        {
            var words = Exchange(c =>
            {
                var cmd = Command(c, "/ip/address/print");
                switch (method)
                {
                    case "ExecuteSingleRow": cmd.ExecuteSingleRow(); break;
                    case "ExecuteSingleRowOrDefault": cmd.ExecuteSingleRowOrDefault(); break;
                    case "ExecuteList": cmd.ExecuteList().ToList(); break;
                    case "ExecuteSingleRowAsync": cmd.ExecuteSingleRowAsync().GetAwaiter().GetResult(); break;
                    case "ExecuteSingleRowOrDefaultAsync": cmd.ExecuteSingleRowOrDefaultAsync().GetAwaiter().GetResult(); break;
                    case "ExecuteListAsync": cmd.ExecuteListAsync().GetAwaiter().GetResult(); break;
                }
            }, OneRowThenDone);

            AssertWords(words, "/ip/address/print", "?name=a", "?comment=b");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ExecuteListWithFields_WritesOneProplistWordPerField(bool async)
        {
            // X5 as it is: two words; the transport-neutral command sends one comma-joined .proplist.
            var words = Exchange(c =>
            {
                var cmd = Command(c, "/ip/address/print");
                if (async) cmd.ExecuteListAsync(new[] { "name", "address" }).GetAwaiter().GetResult();
                else cmd.ExecuteList("name", "address").ToList();
            }, Done);

            AssertWords(words, "/ip/address/print", "?name=a", "?comment=b", "=.proplist=name", "=.proplist=address");
        }

        [TestMethod]
        public void ExecuteListWithFields_RefusesACallersOwnProplist()
        {
            // X5 as it is: the transport-neutral command keeps the caller's .proplist and drops the argument.
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                server.ReadSentence();                       // /quit: the command never left the client
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                var cmd = connection.CreateCommand("/ip/address/print");
                cmd.AddParameter(TikSpecialProperties.Proplist, "comment");

                var ex = Assert.ThrowsException<ArgumentException>(() => cmd.ExecuteList("name").ToList());
                StringAssert.Contains(ex.Message, "already defined");
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void ExecuteWithCallback_WritesNameValueWords()
        {
            var done = new ManualResetEventSlim(false);
            var words = Exchange(c =>
            {
                Command(c, "/interface/listen").ExecuteWithCallback(_ => { }, onDoneCallback: done.Set);
                Assert.IsTrue(done.Wait(5000), "the callback command never ended");
            }, Done);

            AssertWords(words, "/interface/listen", "=name=a", "=comment=b");
        }

        [TestMethod]
        public void ExecuteListUntilDone_WritesNameValueWords()
        {
            var words = Exchange(c => Command(c, "/tool/ping").ExecuteListUntilDone(TimeSpan.FromSeconds(5)).ToList(), Done);

            AssertWords(words, "/tool/ping", "=name=a", "=comment=b");
        }

        // ── Answers the review changes on purpose ─────────────────────────────

        [TestMethod]
        public void ExecuteScalar_OnAMultiFieldRow_ThrowsLinqsInvalidOperation()
        {
            // X4 as it is: LINQ Single over the row's words. The transport-neutral command returns the first
            // non-.id field instead.
            Exchange(c => Assert.ThrowsException<InvalidOperationException>(
                    () => c.CreateCommand("/interface/print").ExecuteScalar()),
                (s, tag) =>
                {
                    s.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag}", "=name=a", "=mtu=1500");
                    Done(s, tag);
                });
        }

        [TestMethod]
        public void ExecuteListUntilDone_Trap_ThrowsTheUnclassifiedTrapException()
        {
            // X3 as it is: "no such item" would be TikNoSuchItemException through TikTrapClassifier.
            Exchange(c =>
            {
                var ex = Assert.ThrowsException<TikCommandTrapException>(
                    () => c.CreateCommand("/tool/traceroute").ExecuteListUntilDone().ToList());
                Assert.AreEqual(typeof(TikCommandTrapException), ex.GetType());
            }, (s, tag) =>
            {
                s.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=message=no such item");
                Done(s, tag);
            });
        }

        [TestMethod]
        public void ExecuteListUntilDone_Fatal_ThrowsIOException()
        {
            // X3 as it is: outside the tik4net hierarchy.
            Exchange(c => Assert.ThrowsException<IOException>(
                    () => c.CreateCommand("/tool/traceroute").ExecuteListUntilDone().ToList()),
                (s, tag) =>
                {
                    s.WriteSentence("!fatal", "=message=router going down");
                    s.CloseClientConnection();
                },
                connectionSurvives: false);
        }

        [TestMethod]
        public void AddParameterAndValues_RefusesAnUnpairedLastName()
        {
            // X7: refused before anything is sent, so no exchange is needed.
            using var connection = new ApiConnection(false);
            var cmd = connection.CreateCommand("/ip/address/print");
            var ex = Assert.ThrowsException<ArgumentException>(() => cmd.AddParameterAndValues("name", "a", "comment"));

            StringAssert.Contains(ex.Message, "'comment'");
            Assert.AreEqual(0, cmd.Parameters.Count);
        }

        [TestMethod]
        public void CreateCommandAndParameters_RefusesAnUnpairedLastName()
        {
            using var connection = new ApiConnection(false);
            var ex = Assert.ThrowsException<ArgumentException>(
                () => connection.CreateCommandAndParameters("/ip/address/print", "name", "a", "comment"));

            StringAssert.Contains(ex.Message, "'comment'");
        }

        [TestMethod]
        public void ExecuteListWithDuration_Trap_ThrowsAbort()
        {
            // X3 as it is: the same router error as the two above, as a third type.
            Exchange(c => Assert.ThrowsException<TikCommandAbortException>(
                    () => c.CreateCommand("/tool/torch").ExecuteListWithDuration(TimeSpan.FromSeconds(30)).ToList()),
                (s, tag) =>
                {
                    s.WriteSentence("!trap", $"{TikSpecialProperties.Tag}={tag}", "=message=no such item");
                    Done(s, tag);
                });
        }
    }
}
