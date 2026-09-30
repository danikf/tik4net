using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// A CLI read that times out leaves the rest of the router's answer on its way; the session is brought back in step
    /// (Ctrl-C, then a fence the echo cannot contain) rather than closed, and the next command reads its own answer.
    /// </summary>
    [TestClass]
    public class CliResyncTest : TestBase
    {
        private bool IsCli()
        {
            var type = ResolveConnectionType();
            return type == TikConnectionType.Telnet || type == TikConnectionType.Ssh || type == TikConnectionType.MacTelnet
                || type == TikConnectionType.WinboxCli || type == TikConnectionType.WinboxCliMac;
        }

        [TestMethod]
        public void AfterAReceiveTimeout_TheNextCommandReadsItsOwnAnswer()
        {
            if (!IsCli())
                Assert.Inconclusive("A terminal transport's behaviour: the others frame their replies.");
            string identity = Connection.CreateCommand("/system/identity/print").ExecuteScalar();

            // A connection of its own: the terminal clients take ReceiveTimeout when they open.
            var type = ResolveConnectionType();
            var setup = LabSetup(type);
            setup.ReceiveTimeout = TimeSpan.FromSeconds(1.5);
            using (var connection = setup.Create(type))
            {
                // Output before the stall and after it: either could be read by the next command if the session
                // were left out of step.
                Assert.ThrowsException<TikConnectionReceiveTimeoutException>(
                    () => ((ITikRawSentenceConnection)connection).CallCommandSync(":put t4n-early; :delay 5s; :put t4n-late").ToList());

                Assert.IsTrue(connection.IsOpened, "the session was closed instead of brought back in step");
                Assert.AreEqual(identity, connection.CreateCommand("/system/identity/print").ExecuteScalar(),
                    "the command after the timeout read someone else's output");
                Assert.AreEqual(identity, connection.CreateCommand("/system/identity/print").ExecuteScalar());
            }
        }
    }
}
