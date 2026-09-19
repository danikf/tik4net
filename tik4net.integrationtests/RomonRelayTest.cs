// RomonRelayTest.cs — a router reached THROUGH another one over RoMON, written to and read back.
//
// Two lab routers: the agent is the router the whole suite talks to (host/user/pass/routerMac in App.config),
// the target is the second one (romonTarget* in App.config). Every test opens the target through the agent over
// each transport that relays (Telnet, SSH, MAC-Telnet to the agent; /tool romon ssh beyond it) — whatever
// transport the run selects, like MacOnlyAddressingTest, so the relay stays covered by every run.
//
// What the relay must never do is answer from the agent: a prompt shape cannot tell the two routers apart, so a
// relay that silently fell back would run every later command on the wrong router. Each test therefore checks
// the other side too — a write through the relay is looked for on the target over its own binary-API
// connection, and must be absent from the agent.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.System;
using tik4net.Objects.Tool.Romon;

namespace tik4net.integrationtests
{
    [TestClass]
    public class RomonRelayTest
    {
        // Rows this class creates carry this list name, so a sweep can find what an interrupted run left.
        private const string TestList = "tik4net-romon";

        private static string TargetId => ConfigurationManager.AppSettings["romonTargetId"];
        private static string TargetHost => ConfigurationManager.AppSettings["romonTargetHost"];
        private static string TargetUser => ConfigurationManager.AppSettings["romonTargetUser"];
        private static string TargetPass => ConfigurationManager.AppSettings["romonTargetPass"] ?? "";

        private static string AgentHost => ConfigurationManager.AppSettings["host"];
        private static string AgentUser => ConfigurationManager.AppSettings["user"];
        private static string AgentPass => ConfigurationManager.AppSettings["pass"] ?? "";
        private static string AgentMac => ConfigurationManager.AppSettings["routerMac"];

        private static void RequireTarget()
        {
            if (string.IsNullOrEmpty(TargetId))
                Assert.Inconclusive("No romonTargetId in App.config — there is no second router to relay to.");
        }

        private static void RequireTargetHost()
        {
            RequireTarget();
            if (string.IsNullOrEmpty(TargetHost))
                Assert.Inconclusive("No romonTargetHost in App.config — nothing to check a relayed write against.");
        }

        // The agent over the MAC layer is named by its MAC as well, so no MNDP lookup is needed.
        private static ITikConnection OpenRelay(TikConnectionType agentTransport)
        {
            var agentAddress = agentTransport == TikConnectionType.MacTelnet && !string.IsNullOrEmpty(AgentMac)
                ? TikRouterAddress.FromHostAndMac(AgentHost, AgentMac)
                : TikRouterAddress.FromHost(AgentHost);
            var setup = new TikConnectionSetup(TikRouterAddress.FromRomonId(TargetId), TargetUser, TargetPass)
            {
                RomonAgentSetup = new TikRomonAgentSetup(agentAddress, AgentUser, AgentPass),
            };
            return setup.Create(agentTransport);
        }

        private static ITikConnection OpenTargetDirect()
            => new TikConnectionSetup(TargetHost, TargetUser, TargetPass).Create(TikConnectionType.Api);

        private static ITikConnection OpenAgentDirect()
            => new TikConnectionSetup(AgentHost, AgentUser, AgentPass).Create(TikConnectionType.Api);

        private static FirewallAddressList[] OurRows(ITikConnection connection, string comment)
            => connection.LoadList<FirewallAddressList>(
                    connection.CreateParameter("list", TestList, TikCommandParameterFormat.Filter))
                .Where(r => r.Comment == comment)
                .ToArray();

        // What an interrupted run left, on either router. Only rows on the test list are touched.
        private static void Sweep(ITikConnection connection)
        {
            foreach (var row in connection.LoadList<FirewallAddressList>(
                         connection.CreateParameter("list", TestList, TikCommandParameterFormat.Filter)).ToList())
                connection.Delete(row);
        }

        /// <summary>
        /// The relay reaches the target and says so: the connection info names it, the target answers its own
        /// RoMON id, and its identity is the one the target gives over a direct connection — not the agent's.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.MacTelnet)]
        public void Relay_ReachesTheTarget_NotTheAgent(TikConnectionType agentTransport)
        {
            RequireTargetHost();

            string targetIdentity, agentIdentity;
            using (var direct = OpenTargetDirect())
                targetIdentity = direct.LoadSingle<SystemIdentity>().Name;
            using (var agent = OpenAgentDirect())
                agentIdentity = agent.LoadSingle<SystemIdentity>().Name;
            Assert.AreNotEqual(agentIdentity, targetIdentity,
                "the two lab routers share an identity, so this test could not tell them apart — rename one");

            using (var relay = OpenRelay(agentTransport))
            {
                var info = relay.GetRomonConnectionInfo();
                Assert.IsNotNull(info, "a relayed connection must describe its relay");
                Assert.AreEqual(TargetId, info.Target.RomonId, true);
                Assert.AreEqual(agentTransport, info.Agent.ConnectionType);

                Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true);
                Assert.AreEqual(targetIdentity, relay.LoadSingle<SystemIdentity>().Name);
            }
        }

        /// <summary>
        /// Create, update and delete through the relay, each step checked on the target over its own API
        /// connection — and the row must never appear on the agent.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet, "192.0.2.61")]
        [DataRow(TikConnectionType.Ssh, "192.0.2.62")]
        [DataRow(TikConnectionType.MacTelnet, "192.0.2.63")]
        public void Relay_CreateUpdateDelete_LandOnTheTarget_AndNeverOnTheAgent(TikConnectionType agentTransport,
            string address)
        {
            RequireTargetHost();
            string comment = "tik4net-romon-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            using (var direct = OpenTargetDirect())
            using (var agent = OpenAgentDirect())
            {
                Sweep(direct);
                Sweep(agent);
                try
                {
                    using (var relay = OpenRelay(agentTransport))
                    {
                        var row = new FirewallAddressList { List = TestList, Address = address, Comment = comment };
                        relay.Save(row);
                        Assert.IsFalse(string.IsNullOrEmpty(row.Id), "the relayed add must return the new row's id");

                        var onTarget = OurRows(direct, comment);
                        Assert.AreEqual(1, onTarget.Length, "the relayed add must create exactly one row on the target");
                        Assert.AreEqual(row.Id, onTarget[0].Id);
                        Assert.AreEqual(address, onTarget[0].Address);
                        Assert.IsFalse(onTarget[0].Disabled == true);
                        Assert.AreEqual(0, OurRows(agent, comment).Length, "the relayed add ran on the AGENT");

                        row.Disabled = true;
                        relay.Save(row);
                        Assert.IsTrue(OurRows(direct, comment).Single().Disabled == true,
                            "the relayed set must reach the target");

                        relay.Delete(row);
                        Assert.AreEqual(0, OurRows(direct, comment).Length, "the relayed remove must reach the target");
                    }
                }
                finally
                {
                    Sweep(direct);
                    Sweep(agent);
                }
            }
        }

        /// <summary>
        /// The target ends the session (here: <c>/quit</c> typed on it). The command that ended it reports that the
        /// relay ended, the connection is closed, and nothing after it may answer from the agent.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.MacTelnet)]
        public void Relay_WhenTheTargetEndsTheSession_TheAgentNeverAnswers(TikConnectionType agentTransport)
        {
            RequireTarget();

            using (var relay = OpenRelay(agentTransport))
            {
                Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true);

                var ended = Assert.ThrowsException<TikRomonRelayEndedException>(
                    () => ((ITikRawSentenceConnection)relay).CallCommandSync("/quit").ToList());
                Assert.IsTrue(ended.CommandMayHaveRun, "/quit was sent, and it is what ended the relay");

                string after;
                try { after = relay.LoadSingle<ToolRomon>().CurrentId; }
                catch (Exception ex) when (!(ex is AssertFailedException)) { return; }   // failing is right
                Assert.AreEqual(TargetId, after, true, "a command after the relay ended answered from the agent");
            }
        }
    }
}
