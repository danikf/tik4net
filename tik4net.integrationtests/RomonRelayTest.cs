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
using tik4net.Objects.Tool;
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

        // ── Listen and monitors ───────────────────────────────────────────────
        //
        // Both are polled on a CLI transport: ordinary one-shot commands reissued through the same terminal, so the
        // relay carries them like any other. What is checked is that they see the target, keep the relay on it,
        // and leave it usable when they stop.

        /// <summary>
        /// A listen through the relay reports a row added on the target, and never one added on the agent.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet, "192.0.2.73", "192.0.2.74")]
        [DataRow(TikConnectionType.Ssh, "192.0.2.75", "192.0.2.76")]
        [DataRow(TikConnectionType.MacTelnet, "192.0.2.77", "192.0.2.78")]
        [Timeout(120000)]
        public void Relay_Listen_SeesTheTarget_NotTheAgent(TikConnectionType agentTransport,
            string targetAddress, string agentAddress)
        {
            RequireTargetHost();
            string comment = "tik4net-romon-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var seen = new System.Collections.Concurrent.ConcurrentQueue<FirewallAddressList>();
            Exception listenError = null;

            using (var direct = OpenTargetDirect())
            using (var agent = OpenAgentDirect())
            {
                Sweep(direct);
                Sweep(agent);
                try
                {
                    using (var relay = OpenRelay(agentTransport))
                    {
                        var listen = relay.LoadListenWithCallback<FirewallAddressList>(
                            row => { if (row.Comment == comment) seen.Enqueue(row); },
                            deletedId => { },
                            ex => listenError = ex,
                            relay.CreateParameter("list", TestList, TikCommandParameterFormat.Filter));
                        try
                        {
                            // A listen reports changes against its first snapshot; a row added before that snapshot is
                            // part of it and is never reported. Let the first polls go through the relay first.
                            System.Threading.Thread.Sleep(3000);
                            agent.Save(new FirewallAddressList { List = TestList, Address = agentAddress, Comment = comment });
                            direct.Save(new FirewallAddressList { List = TestList, Address = targetAddress, Comment = comment });

                            var deadline = DateTime.UtcNow.AddSeconds(15);
                            while (!seen.Any(r => r.Address == targetAddress) && listenError == null && DateTime.UtcNow < deadline)
                                System.Threading.Thread.Sleep(250);
                            System.Threading.Thread.Sleep(2000);   // a poll or two more, for a row that should not come
                        }
                        finally
                        {
                            listen.CancelAndJoin();
                        }

                        Assert.IsNull(listenError, listenError?.ToString());
                        Assert.IsTrue(seen.Any(r => r.Address == targetAddress), "the listen did not report the row added on the target");
                        Assert.IsFalse(seen.Any(r => r.Address == agentAddress), "the listen reported a row added on the AGENT");
                        Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true,
                            "after the listen stopped the relay answered from the agent");
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
        /// A callback monitor (ping) through the relay delivers rows, and the relay stays on the target after it.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.MacTelnet)]
        [Timeout(60000)]
        public void Relay_CallbackMonitor_DeliversRows_AndKeepsTheRelay(TikConnectionType agentTransport)
        {
            RequireTargetHost();
            var rows = new System.Collections.Concurrent.ConcurrentQueue<ToolPing>();
            Exception monitorError = null;

            using (var relay = OpenRelay(agentTransport))
            {
                var ping = relay.LoadWithCallback<ToolPing>(
                    row => rows.Enqueue(row),
                    ex => monitorError = ex,
                    relay.CreateParameter("address", TargetHost),
                    relay.CreateParameter("count", "3"));
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (rows.Count < 3 && monitorError == null && DateTime.UtcNow < deadline)
                        System.Threading.Thread.Sleep(250);
                }
                finally
                {
                    ping.CancelAndJoin();
                }

                Assert.IsNull(monitorError, monitorError?.ToString());
                Assert.IsTrue(rows.Count >= 3, "expected three ping rows through the relay, got " + rows.Count);
                Assert.IsTrue(rows.All(r => r.Host == TargetHost));
                Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true,
                    "after the monitor stopped the relay answered from the agent");
            }
        }

        /// <summary>A bounded monitor read synchronously (ping count=2) through the relay returns its rows.</summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet)]
        [DataRow(TikConnectionType.Ssh)]
        [DataRow(TikConnectionType.MacTelnet)]
        public void Relay_SyncMonitor_ReturnsRows_AndKeepsTheRelay(TikConnectionType agentTransport)
        {
            RequireTargetHost();

            using (var relay = OpenRelay(agentTransport))
            {
                var rows = relay.LoadList<ToolPing>(
                    relay.CreateParameter("address", TargetHost),
                    relay.CreateParameter("count", "2")).ToList();

                Assert.AreEqual(2, rows.Count(r => r.Host == TargetHost), "expected two ping rows through the relay");
                Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true,
                    "after the monitor the relay answered from the agent");
            }
        }

        // ── Safe Mode ─────────────────────────────────────────────────────────
        //
        // Take and release are a Ctrl+X in the live terminal, and the terminal is the AGENT's, running
        // /tool romon ssh. Whether the key reaches the target or is taken by the agent's own console is exactly
        // what cannot be seen from the relay, so each test reads /safe-mode on both routers over their own API.

        private static bool SafeModeHeldOn(ITikConnection direct)
            => direct.CreateCommand("/safe-mode/print").ExecuteList().Single()
                .GetResponseField("enabled") == "true";

        // A hold left by an interrupted run blocks the next take; a release from any session clears it.
        private static void ReleaseStaleSafeMode(ITikConnection direct)
        {
            if (SafeModeHeldOn(direct))
                direct.CreateCommand("/safe-mode/release").ExecuteNonQuery();
        }

        /// <summary>
        /// Safe Mode taken through the relay is held on the target, never on the agent; a change made under it and
        /// released stays on the target.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet, "192.0.2.64")]
        [DataRow(TikConnectionType.Ssh, "192.0.2.65")]
        [DataRow(TikConnectionType.MacTelnet, "192.0.2.66")]
        public void Relay_SafeMode_IsTakenAndReleasedOnTheTarget(TikConnectionType agentTransport, string address)
        {
            RequireTargetHost();
            string comment = "tik4net-romon-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            using (var direct = OpenTargetDirect())
            using (var agent = OpenAgentDirect())
            {
                Sweep(direct);
                ReleaseStaleSafeMode(direct);
                ReleaseStaleSafeMode(agent);
                try
                {
                    using (var relay = OpenRelay(agentTransport))
                    {
                        var safeMode = (ITikSafeModeConnection)relay;
                        safeMode.SafeModeTake();
                        Assert.IsTrue(safeMode.SafeModeGet());
                        Assert.IsTrue(SafeModeHeldOn(direct), "Safe Mode taken through the relay is not held on the target");
                        Assert.IsFalse(SafeModeHeldOn(agent), "Safe Mode taken through the relay is held on the AGENT");

                        relay.Save(new FirewallAddressList { List = TestList, Address = address, Comment = comment });
                        safeMode.SafeModeRelease();
                        Assert.IsFalse(SafeModeHeldOn(direct), "the release through the relay did not reach the target");
                        Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true,
                            "after the release the relay answered from the agent");
                    }
                    Assert.AreEqual(1, OurRows(direct, comment).Length, "a released change must stay on the target");
                }
                finally
                {
                    Sweep(direct);
                    ReleaseStaleSafeMode(direct);
                    ReleaseStaleSafeMode(agent);
                }
            }
        }

        /// <summary>
        /// Unroll through the relay discards the change on the target and leaves the relay open on the target.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet, "192.0.2.67")]
        [DataRow(TikConnectionType.Ssh, "192.0.2.68")]
        [DataRow(TikConnectionType.MacTelnet, "192.0.2.69")]
        public void Relay_SafeMode_UnrollDiscardsOnTheTarget(TikConnectionType agentTransport, string address)
        {
            RequireTargetHost();
            string comment = "tik4net-romon-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            using (var direct = OpenTargetDirect())
            using (var agent = OpenAgentDirect())
            {
                Sweep(direct);
                ReleaseStaleSafeMode(direct);
                ReleaseStaleSafeMode(agent);
                try
                {
                    using (var relay = OpenRelay(agentTransport))
                    {
                        var safeMode = (ITikSafeModeConnection)relay;
                        safeMode.SafeModeTake();
                        relay.Save(new FirewallAddressList { List = TestList, Address = address, Comment = comment });
                        Assert.AreEqual(1, OurRows(direct, comment).Length, "the change must be on the target before the unroll");

                        safeMode.SafeModeUnroll();
                        Assert.IsFalse(safeMode.SafeModeGet());
                        Assert.AreEqual(0, OurRows(direct, comment).Length, "the unroll did not discard the change on the target");
                        Assert.IsFalse(SafeModeHeldOn(direct), "Safe Mode is still held on the target after the unroll");
                        Assert.AreEqual(TargetId, relay.LoadSingle<ToolRomon>().CurrentId, true,
                            "after the unroll the relay answered from the agent");
                    }
                }
                finally
                {
                    Sweep(direct);
                    ReleaseStaleSafeMode(direct);
                    ReleaseStaleSafeMode(agent);
                }
            }
        }

        /// <summary>
        /// Closing the relay while Safe Mode is held rolls the change back on the target — the session that held
        /// it is the relayed one, and it ends with the connection.
        /// </summary>
        [DataTestMethod]
        [DataRow(TikConnectionType.Telnet, "192.0.2.70")]
        [DataRow(TikConnectionType.Ssh, "192.0.2.71")]
        [DataRow(TikConnectionType.MacTelnet, "192.0.2.72")]
        [Timeout(120000)]
        public void Relay_SafeMode_CloseWithoutReleaseRollsBackOnTheTarget(TikConnectionType agentTransport, string address)
        {
            RequireTargetHost();
            string comment = "tik4net-romon-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            using (var direct = OpenTargetDirect())
            using (var agent = OpenAgentDirect())
            {
                Sweep(direct);
                ReleaseStaleSafeMode(direct);
                ReleaseStaleSafeMode(agent);
                try
                {
                    using (var relay = OpenRelay(agentTransport))
                    {
                        ((ITikSafeModeConnection)relay).SafeModeTake();
                        relay.Save(new FirewallAddressList { List = TestList, Address = address, Comment = comment });
                        Assert.AreEqual(1, OurRows(direct, comment).Length, "the change must be on the target before the close");
                    }

                    int remaining = 1;
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while ((remaining = OurRows(direct, comment).Length) > 0 && DateTime.UtcNow < deadline)
                        System.Threading.Thread.Sleep(1000);
                    Assert.AreEqual(0, remaining, "closing the relay with Safe Mode held did not roll back the change on the target");
                }
                finally
                {
                    Sweep(direct);
                    ReleaseStaleSafeMode(direct);
                    ReleaseStaleSafeMode(agent);
                }
            }
        }
    }
}
