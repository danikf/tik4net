using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// When the router ends a binary-API session itself, a command still running on another tag is told the router's
    /// reason. The router announces the end with an untagged <c>!fatal</c> (here the answer to <c>/quit</c>), which no
    /// tagged command waits for, and then closes the socket.
    /// </summary>
    [TestClass]
    public class ApiRouterEndedSessionTest : TestBase
    {
        [DataTestMethod]
        [DataRow(TikConnectionType.Api)]
        [DataRow(TikConnectionType.ApiSsl)]
        public void TheRoutersFatal_ReachesACommandRunningOnAnotherTag(TikConnectionType connectionType)
        {
            using (var connection = LabSetup(connectionType).Create(connectionType))
            {
                // Runs until cancelled, on RouterOS 6 and 7 alike.
                var monitor = connection.CreateCommand("/system/resource/print",
                    connection.CreateParameter("interval", "1"));
                var running = Task.Run(() => monitor.ExecuteListUntilDone(30).ToList());

                Task.Delay(1500).Wait();
                Assert.IsFalse(running.IsCompleted, "the monitor is still running when the session is ended: "
                    + (running.IsFaulted ? running.Exception.InnerException.Message : "it ended"));

                try
                {
                    ((ITikRawSentenceConnection)connection).CallCommandSync("/quit").ToList();
                }
                catch (TikConnectionException)
                {
                    // the /quit's own answer is not what this test is about
                }

                // 4.0 reports a !fatal in a bounded read as IOException("Connection has been closed: <reason>")
                var ex = Assert.ThrowsException<System.IO.IOException>(
                    () => running.GetAwaiter().GetResult());
                StringAssert.Contains(ex.Message, "session terminated on request");
            }
        }
    }
}
