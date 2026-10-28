using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Api;

namespace tik4net.unittests.Api
{
    /// <summary>
    /// The dispatcher on its own, for the orderings a fake router cannot pin: which ending a waiter sees when the
    /// connection ends more than once (the router's <c>!fatal</c>, then the EOF that follows it).
    /// </summary>
    [TestClass]
    public class ApiSentenceDispatcherTests
    {
        private static ApiFatalSentence Fatal(string reason) => new ApiFatalSentence(new[] { reason });

        [TestMethod]
        public void TheFirstTermination_IsKept_ALaterOneChangesNothing()
        {
            var dispatcher = new ApiSentenceDispatcher();
            dispatcher.TerminateAll(Fatal("router going down"));
            dispatcher.TerminateAll(Fatal("connection lost: TikEofException"));

            var sync = (ApiFatalSentence)dispatcher.Wait("7", 1000);
            var async = (ApiFatalSentence)dispatcher.WaitAsync("8", 1000, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("router going down", sync.Message);
            Assert.AreEqual("router going down", async.Message);
        }

        [TestMethod]
        public void AQueuedReply_OutranksTheTermination()
        {
            var dispatcher = new ApiSentenceDispatcher();
            var done = new ApiDoneSentence(new[] { ".tag=7" });
            dispatcher.Push(done);
            dispatcher.TerminateAll(Fatal("router going down"));

            Assert.AreSame(done, dispatcher.Wait("7", 1000));
            Assert.AreEqual("router going down", ((ApiFatalSentence)dispatcher.Wait("7", 1000)).Message);
        }
    }
}
