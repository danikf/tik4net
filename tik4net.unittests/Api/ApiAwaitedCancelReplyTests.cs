using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The <c>/cancel</c> an awaited command sends is answered by the router, and that answer must not stay in the
    /// dispatcher.
    /// </summary>
    /// <remarks>
    /// A cancelled <c>Execute*Async</c> writes <c>/cancel =tag=A .tag=C</c> and goes on reading tag A until its
    /// <c>!done</c>. Nobody reads tag C, so the router's <c>!done .tag=C</c> was queued for good: one entry leaked per
    /// cancel, and the next receive timeout on the connection read it as <i>"the router answered and the reply was not
    /// paired"</i> — the diagnosis of a pairing fault that is not there.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]   // a fake peer answers within a receive timeout
    public class ApiAwaitedCancelReplyTests
    {
        [TestMethod]
        public async Task TheCancelOfAnAwaitedCommand_LeavesNoUnclaimedReply()
        {
            using var server = new FakeRouterServer { EchoTags = false };
            var serverTask = Task.Run(() =>
            {
                server.AcceptClient();
                var login = server.ReadSentence();
                server.WriteSentence("!done", login.First(w => w.StartsWith(".tag=", StringComparison.Ordinal)));
                var cmd = server.ReadSentence();
                string tagA = cmd.First(w => w.StartsWith(".tag=", StringComparison.Ordinal));
                var cancel = server.ReadSentence();                    // /cancel =tag=A .tag=C
                string tagC = cancel.First(w => w.StartsWith(".tag=", StringComparison.Ordinal));
                server.WriteSentence("!trap", "=category=2", "=message=interrupted", tagA);
                server.WriteSentence("!done", tagA);
                server.WriteSentence("!done", tagC);                   // the router answers the /cancel too
                try { server.ReadSentence(); } catch { }               // the next command — left unanswered
                Thread.Sleep(3000);
            });

            using var connection = new ApiConnection(false);
            connection.ReceiveTimeout = TimeSpan.FromSeconds(1);
            connection.Open("127.0.0.1", server.Port, "admin", "");
            using (var cts = new CancellationTokenSource(300))
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => connection.CreateCommand("/interface/listen").ExecuteListAsync(cts.Token));
            Thread.Sleep(200);   // the /cancel's own !done arrives after the cancelled command has returned

            var ex = Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                () => connection.CreateCommand("/system/identity/print").ExecuteList().ToList());
            StringAssert.Contains(ex.Message, "no unclaimed sentences",
                "the /cancel's own !done is held for a tag nobody waits on, so the next timeout blames a pairing fault: "
                + ex.Message);
        }
    }
}
