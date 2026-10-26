// ApiCallbackEndingTests.cs — ExecuteWithCallback's onDone on the binary API fires only when the command ends
// without an error: it finished (!done), or the caller's Cancel() stopped it (the interrupted trap is not an error).
// After a router error (!trap, then !done) or a lost connection (!fatal) only errorCallback fires (command-layer X16,
// D21).

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiCallbackEndingTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        /// <summary>Time for a second callback to arrive after the command has ended, if one were coming.</summary>
        private const int Settle = 300;

        private static string ExtractTag(IEnumerable<string> words)
            => words.Single(w => w.StartsWith(TikSpecialProperties.Tag + "=", StringComparison.Ordinal))
                    .Substring((TikSpecialProperties.Tag + "=").Length);

        private static string Tag(string tag) => $"{TikSpecialProperties.Tag}={tag}";

        private sealed class Endings
        {
            public int Rows;
            public readonly List<string> Errors = new List<string>();
            public int Done;
            public readonly ManualResetEventSlim Ended = new ManualResetEventSlim(false);
        }

        /// <summary>
        /// Logs in, starts <c>/interface/listen</c> with the three callbacks, lets <paramref name="script"/> answer it
        /// (it gets the command's tag), and waits until a callback has said the command ended and the command no
        /// longer runs. <paramref name="whileRunning"/> runs on the client once the script has sent its first row.
        /// </summary>
        private static Endings Run(Action<FakeRouterServer, string> script, Action<ITikCommand>? whileRunning = null,
            ManualResetEventSlim? firstRow = null)
        {
            var e = new Endings();
            using var server = new FakeRouterServer();
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                var command = server.ReadSentence();         // /interface/listen
                script(server, ExtractTag(command));

                try
                {
                    server.ReadSentence();                   // /quit, sent by Close()
                    server.WriteSentence("!fatal", "=message=session terminated on request");
                }
                catch (Exception)
                {
                    // the script closed the connection itself
                }
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "secret");
                var cmd = connection.CreateCommand("/interface/listen");
                cmd.ExecuteWithCallback(
                    row => Interlocked.Increment(ref e.Rows),
                    trap => { lock (e.Errors) e.Errors.Add(trap.Message); e.Ended.Set(); },
                    () => { Interlocked.Increment(ref e.Done); e.Ended.Set(); });

                if (whileRunning != null)
                {
                    Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref e.Rows) > 0, Wait), "precondition: a row arrived");
                    whileRunning(cmd);
                }

                Assert.IsTrue(e.Ended.Wait(Wait), "no callback said the command ended");
                Assert.IsTrue(SpinWait.SpinUntil(() => !cmd.IsRunning, Wait), "the command still runs");
                Thread.Sleep(Settle);
            }

            serverTask.Wait(Wait);
            return e;
        }

        [TestMethod]
        public void RowsThenDone_CallsOnDone()
        {
            var e = Run((s, t) =>
            {
                s.WriteSentence("!re", Tag(t), "=name=ether1");
                s.WriteSentence("!done", Tag(t));
            });
            Assert.AreEqual(1, e.Rows);
            Assert.AreEqual(0, e.Errors.Count);
            Assert.AreEqual(1, e.Done, "a command that finished ends in onDone");
        }

        [TestMethod]
        public void TrapThenDone_CallsTheErrorCallbackOnly()
        {
            var e = Run((s, t) =>
            {
                s.WriteSentence("!trap", Tag(t), "=message=no such command prefix");
                s.WriteSentence("!done", Tag(t));
            });
            CollectionAssert.AreEqual(new[] { "no such command prefix" }, e.Errors);
            Assert.AreEqual(0, e.Done, "onDone is not called after an error (D21)");
        }

        [TestMethod]
        public void LostConnection_CallsTheErrorCallbackOnly()
        {
            var e = Run((s, t) =>
            {
                s.WriteSentence("!re", Tag(t), "=name=ether1");
                s.WriteSentence("!fatal", "=message=connection lost");
                s.CloseClientConnection();
            });
            Assert.AreEqual(1, e.Errors.Count, "the lost connection is reported");
            Assert.AreEqual(0, e.Done, "onDone is not called after a lost connection");
        }

        [TestMethod]
        public void CancelledByTheCaller_CallsOnDone()
        {
            var e = Run((s, t) =>
            {
                s.WriteSentence("!re", Tag(t), "=name=ether1");
                var cancel = s.ReadSentence();               // /cancel =tag=<t> .tag=c_<t>
                string cancelTag = ExtractTag(cancel);
                // What a router answers: the cancelled command ends with the interrupted trap and its !done,
                // and the /cancel itself with its own !done.
                s.WriteSentence("!trap", Tag(t), "=category=2", "=message=interrupted");
                s.WriteSentence("!done", Tag(t));
                s.WriteSentence("!done", Tag(cancelTag));
            }, cmd => cmd.Cancel());
            Assert.AreEqual(0, e.Errors.Count, "the caller's own cancel is not an error");
            Assert.AreEqual(1, e.Done, "a cancelled command ends in onDone");
        }
    }
}
