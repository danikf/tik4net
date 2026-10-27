// RomonRelayLockSetTest.cs — what RomonRelayTest holds from TestInitialize on, checked without a router.
//
// The relay tests touch two lab routers, and on the MAC layer the agent's MAC-layer lock too. Taking any of them
// in the body (LockedTestBase.LockRouter / LockResource) releases the whole set and takes it again, and another
// leg's test runs in that gap: a CHR2-leg relay test, whose agent is the CHR leg's target, swept the CHR leg's 450
// seeded rows off CHR2 in such a gap and the CHR leg's read then returned none of them. So every lock a relay test
// needs is declared, and this test reads the declared set for every test and data row of the class.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    [SafeInParallelLegs]
    public class RomonRelayLockSetTest : LockedTestBase
    {
        // The tests that hold the agent itself exclusive: they count its terminal sessions, or fill its WinBox slots
        // to make it reap dead relay links (Safe Mode on the target is held until it does).
        private static readonly string[] AgentExclusive =
        {
            "Relay_OpenAsync_CancelledPartWay_LeavesNothingBehind",
            "Relay_SafeMode_IsTakenAndReleasedOnTheTarget",
            "Relay_SafeMode_UnrollDiscardsOnTheTarget",
            "Relay_SafeMode_CloseWithoutReleaseRollsBackOnTheTarget",
        };

        // Opens a relay over every agent transport, the MAC-layer ones included, without a data row to say so.
        private const string AllTransports = "Relay_AfterIdle_StillAnswersFromTheTarget";

        [TestMethod]
        public void EveryRelayTestHoldsWhatItTouches_FromTheStart()
        {
            string agent = LabConfig.Get("host"), target = LabConfig.Get("romonTargetHost");
            // The relays over /tool romon ssh may go to a target of their own (RomonRelayTest.TargetSetting).
            string sshTarget = string.IsNullOrEmpty(LabConfig.Get("romonSshTargetId")) ? null : LabConfig.Get("romonSshTargetHost");
            if (string.IsNullOrWhiteSpace(target))
                Assert.Inconclusive("No romonTargetHost in App.config: RomonRelayTest locks no second router.");

            var problems = new List<string>();
            int checkedRows = 0;
            foreach (var method in typeof(RomonRelayTest).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                                         .Where(m => m.IsDefined(typeof(TestMethodAttribute), true)))
            {
                var rows = method.GetCustomAttributes<DataRowAttribute>().Select(r => r.Data).ToList();
                if (rows.Count == 0)
                    rows.Add(new object[0]);
                foreach (var data in rows)
                {
                    checkedRows++;
                    string name = method.Name + "(" + string.Join(",", data) + ")";
                    var held = LockSetOf(method.Name, data).ToDictionary(s => s.Key, s => s.Exclusive);

                    bool opensMac = method.Name == AllTransports
                                    || data.OfType<TikConnectionType>().Any(t => t == TikConnectionType.MacTelnet
                                                                                 || t == TikConnectionType.WinboxCliMac
                                                                                 || t == TikConnectionType.WinboxNativeMac);
                    bool agentExclusive = AgentExclusive.Contains(method.Name);

                    if (!held.TryGetValue("router@" + target, out bool targetExclusive) || !targetExclusive)
                        problems.Add(name + ": the target is not held exclusive");
                    bool opensSshRelay = method.Name == AllTransports
                                         || data.OfType<TikConnectionType>().Any(t => t == TikConnectionType.Telnet
                                                                                      || t == TikConnectionType.Ssh
                                                                                      || t == TikConnectionType.MacTelnet);
                    if (sshTarget != null && opensSshRelay
                        && (!held.TryGetValue("router@" + sshTarget, out bool sshTargetExclusive) || !sshTargetExclusive))
                        problems.Add(name + ": the SSH relay's target is not held exclusive");
                    if (!held.TryGetValue("router@" + agent, out bool agentHeldExclusive))
                        problems.Add(name + ": the agent is not held");
                    else if (agentExclusive != agentHeldExclusive)
                        problems.Add(name + ": the agent is held " + (agentHeldExclusive ? "exclusive" : "shared")
                                     + ", expected " + (agentExclusive ? "exclusive" : "shared"));
                    bool macHeld = held.ContainsKey("res@" + agent + "@" + TestLockAttribute.MacLayer);
                    if (opensMac != macHeld)
                        problems.Add(name + (opensMac ? ": opens a MAC-layer relay without the MAC-layer lock"
                                                      : ": holds the MAC-layer lock and opens no MAC-layer relay"));
                }
            }

            Console.WriteLine($"{checkedRows} test rows of RomonRelayTest checked");
            Assert.IsTrue(checkedRows > 50, "RomonRelayTest's tests were not found");
            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        // The set LockedTestBase takes in TestInitialize for one test and data row of RomonRelayTest, in the
        // independent leg (which runs over the binary API).
        private static IEnumerable<LockSpec> LockSetOf(string testName, object[] data)
        {
            var test = new RomonRelayTest { TestContext = new DeclaredContext(testName, data) };
            var locks = typeof(LockedTestBase).GetMethod("LocksForThisTest", BindingFlags.Instance | BindingFlags.NonPublic);
            return LockSpec.Normalize((IEnumerable<LockSpec>)locks.Invoke(test, null));
        }

        // Just enough of a TestContext to say which test and row would run.
        private sealed class DeclaredContext : TestContext
        {
            private readonly string _testName;
            private readonly IDictionary _properties = new Hashtable { ["tik.connectionType"] = "Api" };

            public DeclaredContext(string testName, object[] data)
            {
                _testName = testName;
                TestData = data;
            }

            public override string TestName => _testName;
            public override string FullyQualifiedTestClassName => typeof(RomonRelayTest).FullName;
            public override IDictionary Properties => _properties;
            public override void AddResultFile(string fileName) { }
            public override void Write(string message) { }
            public override void Write(string format, params object[] args) { }
            public override void WriteLine(string message) { }
            public override void WriteLine(string format, params object[] args) { }
            public override System.Data.Common.DbConnection DataConnection => null;
            public override System.Data.DataRow DataRow => null;
        }
    }
}
