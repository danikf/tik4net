using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The pump of a run that has already ended must not touch the next run of the same command object.
    /// </summary>
    /// <remarks>
    /// A bounded read (<c>ExecuteListUntilDone</c>, <c>ExecuteListWithDuration</c>) ends on a <c>!trap</c> at once, and
    /// the caller is free to run the command again — but the first run's pump thread is still waiting for that trap's own
    /// <c>!done</c>. When it arrives after the second run has started, the first pump's clean-up used to clear the
    /// command's running state and its tag: the second run went on running on the router while <c>IsRunning</c> said
    /// <c>false</c>, and <c>Cancel()</c> did nothing. The running state belongs to the run, so only the run that set it
    /// may clear it.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiTrappedRunLateDoneTests
    {
        [TestMethod]
        public void TheTrappedRunsLateDone_DoesNotEndTheNextRun()
        {
            using var server = new FakeRouterServer { EchoTags = false };
            var secondRunArrived = new ManualResetEventSlim(false);
            var cancelArrived = new ManualResetEventSlim(false);
            string cancelledTag = null;
            string tagB = null;
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                var login = server.ReadSentence();
                server.WriteSentence("!done", login.First(w => w.StartsWith(".tag=", StringComparison.Ordinal)));
                var first = server.ReadSentence();
                string tagA = first.First(w => w.StartsWith(".tag=", StringComparison.Ordinal));
                server.WriteSentence("!trap", "=message=boom", tagA);   // the router's error…
                var second = server.ReadSentence();                    // …the caller runs the command again…
                tagB = second.First(w => w.StartsWith(".tag=", StringComparison.Ordinal));
                secondRunArrived.Set();
                server.WriteSentence("!done", tagA);                   // …and only now the first run's !done
                var cancel = server.ReadSentence();                    // the caller cancels the second run
                cancelledTag = cancel.FirstOrDefault(w => w.StartsWith("=tag=", StringComparison.Ordinal));
                string cancelTag = cancel.First(w => w.StartsWith(".tag=", StringComparison.Ordinal));
                cancelArrived.Set();
                server.WriteSentence("!trap", "=category=2", "=message=interrupted", tagB);
                server.WriteSentence("!done", tagB);
                server.WriteSentence("!done", cancelTag);
                try { server.ReadSentence(); } catch { }
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(5);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            var cmd = connection.CreateCommand("/interface/listen");

            Assert.ThrowsException<TikCommandTrapException>(() => cmd.ExecuteListUntilDone(TimeSpan.FromSeconds(5)));
            var done = new ManualResetEventSlim(false);
            cmd.ExecuteWithCallback(_ => { }, _ => { }, () => done.Set());
            Assert.IsTrue(secondRunArrived.Wait(5000));
            Thread.Sleep(300);   // let the first run's !done reach its pump

            Assert.IsTrue(cmd.IsRunning,
                "the second run is still running on the router, but the first run's pump cleared the command's "
                + "running state when the first run's !done arrived");

            cmd.Cancel();
            Assert.IsTrue(cancelArrived.Wait(5000), "Cancel() sent nothing: the second run's tag had been cleared");
            Assert.AreEqual("=tag=" + tagB.Substring(".tag=".Length), cancelledTag, "the cancel named the wrong run");
            Assert.IsTrue(done.Wait(5000), "the second run never ended after its cancel");
            Assert.IsFalse(cmd.IsRunning);
        }
    }
}
