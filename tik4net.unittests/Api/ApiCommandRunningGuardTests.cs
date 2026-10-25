// ApiCommandRunningGuardTests.cs — the binary API's half of the one-run-at-a-time rule.
//
// ApiCommand and TikGenericCommand share one running flag (TikCommandBase). GenericCommandRunningGuardTests pins the
// ten generic transports; this pins the API: while a callback command runs, a second execute and a change of what
// the command is are refused, and the command can run again once the router has ended it.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiCommandRunningGuardTests
    {
        private const string AlreadyRunning = "Command is already running.";

        [TestMethod]
        public void ASecondExecuteWhileACallbackCommandRuns_IsRefused()
        {
            using var server = new FakeRouterServer();
            var listenReceived = new ManualResetEventSlim(false);
            var endListen = new ManualResetEventSlim(false);
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                server.ReadSentence();                       // login
                server.WriteSentence("!done");

                server.ReadSentence();                       // /interface/listen
                listenReceived.Set();
                Assert.IsTrue(endListen.Wait(5000));
                server.WriteSentence("!done");               // the router ends it

                server.ReadSentence();                       // the same command, run again
                server.WriteSentence("!done");

                server.ReadSentence();                       // /quit
                server.WriteSentence("!fatal", "=message=session terminated on request");
            });

            using (var connection = new ApiConnection(false))
            {
                connection.Open("127.0.0.1", server.Port, "admin", "");
                var cmd = connection.CreateCommand("/interface/listen");
                var done = new ManualResetEventSlim(false);
                cmd.ExecuteWithCallback(_ => { }, onDoneCallback: done.Set);
                Assert.IsTrue(listenReceived.Wait(5000), "the callback command never reached the router");

                Assert.IsTrue(cmd.IsRunning);
                var ex = Assert.ThrowsException<InvalidOperationException>(() => cmd.ExecuteList().ToList());
                Assert.AreEqual(AlreadyRunning, ex.Message);
                ex = Assert.ThrowsException<InvalidOperationException>(() => cmd.ExecuteWithCallback(_ => { }));
                Assert.AreEqual(AlreadyRunning, ex.Message);
                ex = Assert.ThrowsException<InvalidOperationException>(() => cmd.CommandText = "/ip/address/print");
                Assert.AreEqual(AlreadyRunning, ex.Message);
                Assert.AreEqual("/interface/listen", cmd.CommandText);

                endListen.Set();
                Assert.IsTrue(done.Wait(5000), "the router's !done never ended the command");
                Assert.IsTrue(SpinWait.SpinUntil(() => !cmd.IsRunning, 5000));

                cmd.ExecuteNonQuery();                       // free again
                Assert.IsFalse(cmd.IsRunning);

                connection.Close();
            }

            Assert.IsTrue(serverTask.Wait(5000));
        }
    }
}
