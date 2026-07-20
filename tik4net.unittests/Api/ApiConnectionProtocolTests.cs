using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    // Loopback fake-router protocol tests for ApiConnection (P1.7 in ARCHITECTUREIMPROVEMENTPLAN.md).
    // A scripted TcpListener (FakeRouterServer) plays the router side of the binary API wire protocol
    // so these run deterministically, router-free, in CI — unlike tik4net.integrationtests, which
    // needs a live device.
    [TestClass]
    public class ApiConnectionProtocolTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        [TestMethod]
        public void Login_NewProtocol_Succeeds()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                var loginSentence = server.ReadSentence();
                Assert.AreEqual("/login", loginSentence[0]);
                Assert.IsTrue(loginSentence.Any(w => w == $"=name={TestUser}"));
                Assert.IsTrue(loginSentence.Any(w => w == $"=password={TestPassword}"));

                server.WriteSentence("!done"); // no =ret= word => new (post-6.43) login protocol, no second round-trip

                server.ReadSentence(); // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                Assert.IsTrue(connection.IsOpened);
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void Login_LegacyProtocol_TwoRoundTrips_Succeeds()
        {
            using var server = new FakeRouterServer();
            const string challengeHash = "aabbccddeeff00112233445566778899";
            string expectedResponse = ApiConnectionHelper.EncodePassword(TestPassword, challengeHash);

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();

                var firstLogin = server.ReadSentence(); // /login =name=admin =password=secret (password ignored pre-6.43)
                Assert.AreEqual("/login", firstLogin[0]);
                server.WriteSentence("!done", $"=ret={challengeHash}");

                var secondLogin = server.ReadSentence(); // /login =name=admin =response=00<md5-hash>
                Assert.AreEqual("/login", secondLogin[0]);
                Assert.IsTrue(secondLogin.Any(w => w == $"=response={expectedResponse}"));
                server.WriteSentence("!done");

                server.ReadSentence(); // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);
                Assert.IsTrue(connection.IsOpened);
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void Login_InvalidCredentials_ThrowsTikConnectionLoginException()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();
                server.WriteSentence("!trap", "=message=invalid user name or password (6)");
                server.WriteSentence("!done");
                try { server.ReadSentence(); } catch (System.IO.IOException) { /* client closes without /quit */ }
            });

            using (var connection = new ApiConnection(false))
            {
                Assert.ThrowsException<TikConnectionLoginException>(
                    () => connection.Open("127.0.0.1", server.Port, TestUser, "wrong-password"));
                Assert.IsFalse(connection.IsOpened);
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void TagMultiplexing_RoutesResponsesToCorrectTagRegardlessOfArrivalOrder()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); // login
                server.WriteSentence("!done");

                var command1 = server.ReadSentence();
                var command2 = server.ReadSentence();
                string tag1 = ExtractTag(command1);
                string tag2 = ExtractTag(command2);

                // Respond to whichever command arrived SECOND first, forcing the connection's reader
                // to shelve a non-matching sentence into its per-tag side channel (_readSentences).
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag2}", $"=name=reply-for-{tag2}");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag2}");
                server.WriteSentence("!re", $"{TikSpecialProperties.Tag}={tag1}", $"=name=reply-for-{tag1}");
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag1}");

                server.ReadSentence(); // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var taskAlpha = Task.Run(() => connection.CallCommandSync(new[] { "/alpha/print", $"{TikSpecialProperties.Tag}=alpha" }).ToList());
                var taskBeta = Task.Run(() => connection.CallCommandSync(new[] { "/beta/print", $"{TikSpecialProperties.Tag}=beta" }).ToList());

                Assert.IsTrue(Task.WaitAll(new Task[] { taskAlpha, taskBeta }, 5000));

                string alphaName = taskAlpha.Result.OfType<ITikReSentence>().Single().GetResponseField("name");
                string betaName = taskBeta.Result.OfType<ITikReSentence>().Single().GetResponseField("name");

                Assert.AreEqual("reply-for-alpha", alphaName);
                Assert.AreEqual("reply-for-beta", betaName);

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void EmptySentence_TreatedAsNoRows()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); // login
                server.WriteSentence("!done");

                server.ReadSentence(); // /foo/print
                server.WriteSentence("!empty"); // RouterOS 7.18+: no matching rows
                server.WriteSentence("!done");

                server.ReadSentence(); // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var result = connection.CreateCommand("/foo/print").ExecuteList();

                Assert.AreEqual(0, result.Count());

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void UnexpectedEof_CallCommandSync_SynthesizesFatalSentence()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); // login
                server.WriteSentence("!done");

                server.ReadSentence(); // /system/reboot
                server.CloseClientConnection(); // router "reboots" mid-response: no !done, just EOF
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                var result = connection.CallCommandSync(new[] { "/system/reboot" }).ToList();

                Assert.AreEqual(1, result.Count);
                Assert.IsInstanceOfType(result[0], typeof(ApiFatalSentence));
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void UnexpectedEof_ExecuteNonQuery_DoesNotThrow()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); // login
                server.WriteSentence("!done");

                server.ReadSentence(); // /system/reboot
                server.CloseClientConnection(); // router "reboots": connection drops instead of responding
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                // ExecuteNonQuery treats a synthesized !fatal (from an unexpected EOF) as success,
                // matching real reboot/shutdown/poweroff commands that close the connection instead of replying.
                connection.CreateCommand("/system/reboot").ExecuteNonQuery();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void FiveByteLengthEncoding_DecodesCorrectly()
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); // login
                server.WriteSentence("!done");

                server.ReadSentence(); // /foo/print

                // Force the rarely-exercised 5-byte control-byte (0xF0) length prefix on both words of
                // the response, even though they're tiny — real RouterOS only uses it for words
                // >= 0x10000000 bytes, but the decode path (ApiConnection.ReadWordLength) is the same.
                server.WriteWordWithFiveByteLength("!done");
                server.WriteWordWithFiveByteLength("=ret=five-byte-length-value");
                server.EndSentence();

                server.ReadSentence(); // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                string value = connection.CreateCommand("/foo/print").ExecuteScalar();

                Assert.AreEqual("five-byte-length-value", value);

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }

        [TestMethod]
        public void TrapClassification_NoSuchItem_ThrowsTikNoSuchItemException()
        {
            RunSingleTrapScenario("no such item", connection =>
            {
                var command = connection.CreateCommand("/ip/address/remove");
                command.AddParameterAndValues(".id", "*1");
                Assert.ThrowsException<TikNoSuchItemException>(() => command.ExecuteNonQuery());
            });
        }

        [TestMethod]
        public void TrapClassification_NoSuchCommand_ThrowsTikNoSuchCommandException()
        {
            RunSingleTrapScenario("no such command", connection =>
                Assert.ThrowsException<TikNoSuchCommandException>(
                    () => connection.CreateCommand("/no/such/command").ExecuteNonQuery()));
        }

        [TestMethod]
        public void TrapClassification_AlreadyHaveSuchItem_ThrowsTikAlreadyHaveSuchItemException()
        {
            RunSingleTrapScenario("already have such address", connection =>
            {
                var command = connection.CreateCommand("/ip/address/add");
                command.AddParameterAndValues("address", "1.2.3.4/24");
                Assert.ThrowsException<TikAlreadyHaveSuchItemException>(() => command.ExecuteNonQuery());
            });
        }

        [TestMethod]
        public void TrapClassification_UnclassifiedMessage_ThrowsGenericTikCommandTrapException()
        {
            RunSingleTrapScenario("some unrelated router error", connection =>
                Assert.ThrowsException<TikCommandTrapException>(
                    () => connection.CreateCommand("/foo/bar").ExecuteNonQuery()));
        }

        private static void RunSingleTrapScenario(string trapMessage, Action<ApiConnection> assertion)
        {
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence(); // login
                server.WriteSentence("!done");

                server.ReadSentence(); // the command under test
                server.WriteSentence("!trap", $"=message={trapMessage}");
                server.WriteSentence("!done");

                server.ReadSentence(); // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, TestUser, TestPassword);

                assertion(connection);

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }
    }
}
