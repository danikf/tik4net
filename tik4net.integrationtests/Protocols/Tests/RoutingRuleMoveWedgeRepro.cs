// RoutingRuleMoveWedgeRepro.cs — the minimal reproduction of an OPEN defect: a /routing/rule move,
// issued over a CLI transport immediately after both rows were added over a DIFFERENT connection,
// stops RouterOS 7.24's routing process answering its management interface.
//
// This exists because the full audit reproduces it too (TransportWriteAudit, see
// RoutingRuleMoveWedgesTheRoutingProcess) but costs seven minutes and 62 seeded rows to get there.
// Everything here is the audit's move probe for one path and nothing else.
//
// IT WEDGES THE ROUTER. From the failing call on, every /routing/* menu and /ip/route times out on
// every transport and in the router's own shell; only a reboot clears it. Forwarding and every other
// menu keep working, so the router is reachable the whole time — which is why the diagnosis afterwards
// runs over MAC-Telnet (L2, and a separate code path from the wedged one) rather than over IP.
//
// [Ignore]d, and --filter will NOT run it: MSTest applies [Ignore] before the filter. Comment the
// attribute out to run it, then put it back.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.integrationtests
{
    [TestClass]
    public class RoutingRuleMoveWedgeRepro
    {
        private const string Path = "/routing/rule";

        /// <summary>
        /// Milliseconds to let the two fresh rows settle before the move. <c>0</c> reproduces.
        /// </summary>
        /// <remarks>
        /// The one factor still standing after everything else was ruled out: in the router's own log the
        /// two adds and the move all carry the SAME SECOND, and a move against rules that have merely
        /// existed for a while is clean. Set this from TIK4NET_WEDGE_SETTLE_MS to measure where the boundary
        /// is. Measured on a healthy router: 3 s, 1 s (three times), 500 ms and 200 ms are all clean;
        /// 100 ms wedges. The audit waits 1 s, roughly five times the boundary.
        /// </remarks>
        private static int SettleMs
        {
            get
            {
                int s2;
                return int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_WEDGE_SETTLE_MS"), out s2) ? s2 : 0;
            }
        }

        [Ignore("Reproduces an open defect by WEDGING the lab router's routing process — it needs a "
            + "reboot afterwards. Comment the attribute out to run it deliberately.")]
        [TestMethod]
        public void MoveOverCliAfterAddsOverApiWedgesTheRoutingProcess()
        {
            var probeType = TikConnectionType.Telnet;
            string firstId = null, secondId = null;
            var log = new List<string>();

            using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
            using (var probe = TestBase.LabSetup(probeType).Create(probeType))
            {
                try
                {
                    // A run that wedges cannot tear itself down — the removes go to the stuck menu — so its
                    // two rows survive the reboot into the next run. Sweep them here rather than leaving
                    // them to be deleted by hand: this test's residue is expected, not exceptional.
                    Sweep(api, log);

                    // Disabled, so the rows cannot affect anything the router forwards — the wedge is not
                    // about what the rules DO.
                    firstId = Add(api, "m1");
                    secondId = Add(api, "m2");
                    log.Add("added " + firstId + ", " + secondId);

                    var order = api.CreateCommand(Path + "/print").ExecuteList()
                                   .Select(x => x.GetId()).ToList();
                    log.Add("order before: " + string.Join(",", order));

                    int settle = SettleMs;
                    if (settle > 0)
                    {
                        System.Threading.Thread.Sleep(settle);
                        log.Add("settled " + settle + "ms");
                    }

                    // The move goes out over the probe transport, on a different connection from the one
                    // that made the rows, with only the print above in between. The move itself RETURNS —
                    // the router logs it as applied — and it is the read after it that never answers.
                    probe.CreateCommandAndParameters(Path + "/move",
                        "numbers", secondId, "destination", firstId).ExecuteNonQuery();
                    log.Add("move returned");

                    order = api.CreateCommand(Path + "/print").ExecuteList()
                               .Select(x => x.GetId()).ToList();
                    log.Add("order after: " + string.Join(",", order));
                }
                catch (Exception ex)
                {
                    Assert.Inconclusive("REPRODUCED — " + ex.GetType().Name + ": " + ex.Message
                        + Environment.NewLine + string.Join(Environment.NewLine, log)
                        + Environment.NewLine
                        + "The router is still reachable: diagnose over MAC-Telnet, which does not go "
                        + "through the wedged path. Read /log first, then reboot.");
                }
                finally
                {
                    TryRemove(api, secondId);
                    TryRemove(api, firstId);
                }
            }

            Assert.Inconclusive("Did not reproduce this time." + Environment.NewLine
                + string.Join(Environment.NewLine, log));
        }

        private static void Sweep(ITikConnection conn, List<string> log)
        {
            var stale = conn.CreateCommand(Path + "/print").ExecuteList()
                            .Where(x => (x.GetResponseFieldOrDefault("comment", "")).StartsWith("tik4net-wedge-"))
                            .Select(x => x.GetId()).ToList();
            foreach (string id in stale) TryRemove(conn, id);
            if (stale.Count > 0) log.Add("swept " + stale.Count + " row(s) left by an earlier wedged run");
        }

        private static string Add(ITikConnection conn, string tag)
            => conn.CreateCommandAndParameters(Path + "/add",
                   "action", "lookup", "table", "main", "disabled", "yes",
                   "comment", "tik4net-wedge-" + tag).ExecuteScalar();

        private static void TryRemove(ITikConnection conn, string id)
        {
            if (id == null) return;
            try { conn.CreateCommandAndParameters(Path + "/remove", ".id", id).ExecuteNonQuery(); }
            catch (Exception) { }
        }
    }
}
