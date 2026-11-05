using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// H7 of the shared-helpers review: the binary API's word framing, end to end through
    /// <see cref="ApiConnection"/> against a fake router. A word on each side of every length-prefix
    /// boundary is written by the client, read back by the fake's own decoder, echoed, and read by the client;
    /// the rows a sentence is announced through (<c>OnWriteRow</c>) come in sentence order, sync and async.
    /// </summary>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiWordFramingTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        // Word lengths (the whole "=v=…" word) on both sides of the 1-, 2-, 3- and 4-byte prefix boundaries.
        private static readonly int[] WordLengths = { 3, 0x7F, 0x80, 0x3FFF, 0x4000, 0x1FFFFF, 0x200000 };

        private static string ValueOfWordLength(int wordLength)
            => new string('x', wordLength - "=v=".Length);

        private static Task RunEchoRouter(FakeRouterServer server, IList<int> receivedLengths)
            => Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                foreach (int unused in WordLengths)
                {
                    var command = server.ReadSentence();
                    string word = command.Single(w => w.StartsWith("=v=", StringComparison.Ordinal));
                    receivedLengths.Add(word.Length);
                    server.WriteSentence("!done", "=ret=" + word.Substring("=v=".Length));
                }

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

        [TestMethod]
        public void EveryLengthPrefixRoundTrips_Sync()
        {
            using var server = new FakeRouterServer();
            var received = new List<int>();
            var serverTask = RunEchoRouter(server, received);

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                foreach (int length in WordLengths)
                {
                    var command = connection.CreateCommand("/foo/add");
                    command.AddParameter("v", ValueOfWordLength(length));
                    Assert.AreEqual(ValueOfWordLength(length), command.ExecuteScalar(), "word length " + length);
                }
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(10000));
            CollectionAssert.AreEqual(WordLengths, received);
        }

        [TestMethod]
        public async Task EveryLengthPrefixRoundTrips_Async()
        {
            using var server = new FakeRouterServer();
            var received = new List<int>();
            var serverTask = RunEchoRouter(server, received);

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                foreach (int length in WordLengths)
                {
                    var command = connection.CreateCommand("/foo/add");
                    command.AddParameter("v", ValueOfWordLength(length));
                    Assert.AreEqual(ValueOfWordLength(length), await command.ExecuteScalarAsync(), "word length " + length);
                }
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(10000));
            CollectionAssert.AreEqual(WordLengths, received);
        }

        private static Task RunOneCommandRouter(FakeRouterServer server, IList<string> command)
            => Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                foreach (string word in server.ReadSentence())
                    command.Add(word);
                server.WriteSentence("!done");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

        [TestMethod]
        public void WrittenRowsAreAnnouncedInSentenceOrder_Sync()
        {
            using var server = new FakeRouterServer();
            var sent = new List<string>();
            var serverTask = RunOneCommandRouter(server, sent);

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                var announced = new List<string>();
                connection.OnWriteRow += (s, e) => announced.Add(e.Word);

                var command = connection.CreateCommand("/ip/address/add");
                command.AddParameter("address", "192.168.4.250/24");
                command.AddParameter("interface", "ether1");
                command.ExecuteNonQuery();

                var announcedForCommand = announced.ToList();
                connection.Close();
                Assert.IsTrue(serverTask.Wait(10000));
                CollectionAssert.AreEqual(sent, announcedForCommand);
            }
        }

        [TestMethod]
        public async Task WrittenRowsAreAnnouncedInSentenceOrder_Async()
        {
            using var server = new FakeRouterServer();
            var sent = new List<string>();
            var serverTask = RunOneCommandRouter(server, sent);

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                var announced = new List<string>();
                connection.OnWriteRow += (s, e) => announced.Add(e.Word);

                var command = connection.CreateCommand("/ip/address/add");
                command.AddParameter("address", "192.168.4.250/24");
                command.AddParameter("interface", "ether1");
                await command.ExecuteNonQueryAsync();

                var announcedForCommand = announced.ToList();
                connection.Close();
                Assert.IsTrue(serverTask.Wait(10000));
                CollectionAssert.AreEqual(sent, announcedForCommand);
            }
        }
    }
}
