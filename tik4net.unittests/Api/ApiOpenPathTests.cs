using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// <see cref="ApiConnection.Open(string,int,string,string)"/> and
    /// <see cref="ApiConnection.OpenAsync(string,int,string,string)"/> are one implementation since P2.5 —
    /// the async one, with the synchronous entry point blocking on it. These tests pin the property that
    /// makes that safe to keep: <b>the two put exactly the same bytes on the wire</b>.
    /// </summary>
    /// <remarks>
    /// They used to be two copies, and they had already drifted: the synchronous one negotiated TLS with
    /// <c>SslProtocols.None</c> and translated a handshake failure into <c>TikConnectionSSLErrorException</c>,
    /// the asynchronous one did neither. Nothing failed — the same ApiSsl connection simply reported a
    /// certificate problem differently depending on which method had opened it.
    /// <para>
    /// The login sentence is the part worth asserting on rather than the TLS options, because it is the part
    /// a test can see, and because unifying the two nearly changed it: the async command surface tags every
    /// command unconditionally (a tag is what <c>/cancel</c> addresses), and routing login through it made
    /// login tagged where it had never been. That is not a crash — against a real RouterOS it works — which
    /// is exactly why it needs a test. Login is not cancellable and has nothing to gain from a tag, and
    /// pre-6.43 routers speak a different login protocol that no test here can reach.
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiOpenPathTests
    {
        private const string TestUser = "admin";
        private const string TestPassword = "secret";

        /// <summary>Opens a connection against a scripted router and returns the login sentence it sent.</summary>
        private static List<string> CaptureLoginSentence(Func<ApiConnection, string, int, Task> open)
        {
            using var server = new FakeRouterServer();
            List<string> login = null;

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                login = server.ReadSentence();
                server.WriteSentence("!done");           // 6.43+ login: one sentence, no challenge
                server.ReadSentence();                   // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 5000;        // keep a mis-addressed reply short, not 30 s
                open(connection, "127.0.0.1", server.Port).GetAwaiter().GetResult();
                Assert.IsTrue(connection.IsOpened);
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
            return login;
        }

        [TestMethod]
        public void SyncAndAsyncOpen_SendTheSameLoginSentence()
        {
            var fromSync = CaptureLoginSentence((c, host, port) =>
            {
                c.Open(host, port, TestUser, TestPassword);
                return Task.CompletedTask;
            });
            var fromAsync = CaptureLoginSentence((c, host, port) => c.OpenAsync(host, port, TestUser, TestPassword));

            CollectionAssert.AreEqual(fromSync, fromAsync,
                "Open and OpenAsync must be one implementation. Sync sent [" + string.Join(" ", fromSync)
                + "], async sent [" + string.Join(" ", fromAsync) + "].");
        }

        /// <summary>
        /// And the sentence they agree on is the untagged one. Asserted separately from the equality above:
        /// were both paths to start tagging login, the two would still match each other while the wire form
        /// had changed for every existing consumer.
        /// </summary>
        [TestMethod]
        public void OpenAsync_DoesNotTagTheLogin()
        {
            var login = CaptureLoginSentence((c, host, port) => c.OpenAsync(host, port, TestUser, TestPassword));

            Assert.AreEqual("/login", login[0]);
            Assert.IsFalse(login.Any(w => w.Contains(TikSpecialProperties.Tag + "=")),
                "login must stay untagged unless SendTagWithSyncCommand asks for it; sent: "
                + string.Join(" ", login));
        }

        /// <summary>
        /// The connection's tagging policy still reaches the login when the caller does ask for it — so the
        /// rule above is "follow SendTagWithSyncCommand", not "never tag", which is what the synchronous path
        /// has always done.
        /// </summary>
        [TestMethod]
        public void SendTagWithSyncCommand_StillTagsTheLogin()
        {
            using var server = new FakeRouterServer();
            List<string> login = null;

            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                login = server.ReadSentence();
                string tag = login.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                                  .Substring((TikSpecialProperties.Tag + "=").Length);
                server.WriteSentence("!done", $"{TikSpecialProperties.Tag}={tag}");

                // With tagging on, /quit carries a tag too and its answer is awaited on that tag — an
                // untagged !fatal here would hang the close, not the login this test is about.
                var quit = server.ReadSentence();
                string quitTag = quit.FirstOrDefault(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal));
                server.WriteSentence("!fatal", quitTag ?? "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.ReceiveTimeout = 5000;
                connection.SendTagWithSyncCommand = true;
                connection.OpenAsync("127.0.0.1", server.Port, TestUser, TestPassword).GetAwaiter().GetResult();
                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
            Assert.IsTrue(login.Any(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal)),
                "with SendTagWithSyncCommand the login should carry a tag: " + string.Join(" ", login));
        }
    }
}
