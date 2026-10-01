using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Best-effort purge of test residue on the router, run once at assembly init (see
    /// <see cref="TestAssemblyInit"/>) over a dedicated <b>binary-API</b> connection — deliberately NOT the
    /// transport under test.
    /// <para>
    /// The CLI transports are the ones that leave residue in the first place: a CLI <c>add</c> can answer
    /// without the new <c>.id</c> (mikrotik-tests skill, gotcha A), so the row is created on the router but
    /// the O/R mapper never learns its id and cannot delete it; a killed run leaves everything its teardown
    /// had not reached yet. Those orphans then collide with the <i>next</i> run — an <c>add</c> fails with
    /// "already have interface with name test-eoip" / "ether1 already in test-bond" /
    /// "Multiple initiator peers for the same address" against a record the current run never created, which
    /// reads as a transport bug. A broken CLI session also cannot reliably clean up after itself, which is
    /// why this always runs over the API regardless of <c>tik.connectionType</c>.
    /// </para>
    /// <para>
    /// <see cref="TestBase.SaveTracked{T}"/> already guarantees per-test teardown on the happy and failing
    /// paths; this is the belt-and-braces sweep for what escaped it (a killed process, a CLI add that never
    /// yielded an id and left nothing to match on). It only ever removes rows the suite itself stamps — a
    /// distinctive name/comment marker (<c>t4n</c>, <c>test-</c>, <c>TEST…</c>, <c>tik4net-</c>) or a bare
    /// GUID comment — so a real configuration row is never touched. It targets the collision-prone menus
    /// (unique name/address); menus whose leftovers cannot collide (e.g. duplicate <c>/system/logging</c>
    /// rules) are intentionally skipped.
    /// </para>
    /// </summary>
    internal static class RouterOrphanCleaner
    {
        // Collision-prone menus, ordered so a referencing row is removed before the row it references
        // (ipsec identity/policy before peer/proposal; list members before the list; bridge vlans before the
        // bridge). A remove that still fails on a dependency is swallowed and simply retried-by-cascade when
        // its parent goes.
        private static readonly string[] Paths =
        {
            "/ip/ipsec/identity",
            "/ip/ipsec/policy",
            "/ip/ipsec/peer",
            "/ip/ipsec/proposal",
            "/interface/list/member",
            "/interface/list",
            "/interface/bonding",
            "/interface/veth",   // after /interface/bonding: the bond references it as its slave
            "/interface/eoip",
            "/interface/l2tp-client",
            "/interface/wifi/security",
            "/interface/wifi/channel",
            "/interface/bridge/vlan",
            "/interface/bridge",
            "/ip/hotspot/user",
            "/ip/hotspot/profile",
            "/caps-man/datapath",
            "/ip/firewall/layer7-protocol",
            "/routing/ospf/instance",
            "/routing/table",
            "/system/scheduler",
            "/snmp/community",
            "/tool/graphing/interface",
            "/tool/traffic-generator/packet-template",
            "/ip/pool",
        };

        /// <summary>
        /// Takes this run's lease (<see cref="RunLease"/>) and removes the test-marked rows across <see cref="Paths"/>
        /// that no running test can own. Silent and non-fatal: if the API cannot be reached, or a menu/row cannot be
        /// removed, it is skipped — the goal is to clear conflicts, never to fail the run before it starts.
        /// </summary>
        /// <remarks>
        /// A row carrying a run tag (<see cref="TestNames.Unique"/>) is removed when that run has ended. A row
        /// without one cannot be attributed, so it is removed only when no other run against this router is alive —
        /// with legs running in parallel, the other legs' fixed-name rows are live test state, and deleting them
        /// mid-test is exactly the collision this sweep exists to prevent. The first leg to start sweeps them; the
        /// sweep and the lease are taken under one lock, so two legs starting together cannot both think they are
        /// alone.
        /// </remarks>
        internal static void PurgeTestResidue(string leg)
        {
            string host = LabConfig.Get("host");
            using (FileLock.Acquire(LockSpec.Resource(host, "orphan-sweep"), "orphan sweep of " + leg))
            {
                RunLease.SweepDeadLeases();
                bool alone = RunLease.OtherLiveRuns(host).Count == 0;
                try
                {
                    int removed = Sweep(row => IsTestResidue(row) && OwnerHasEnded(row, alone), null);
                    if (removed > 0)
                        Console.WriteLine($"[RouterOrphanCleaner] swept {removed} orphaned test row(s) before the run"
                                          + (alone ? "." : " (tagged rows of ended runs only: other runs are live)."));
                }
                finally
                {
                    RunLease.Take(leg, host);
                }
            }
        }

        /// <summary>
        /// Removes the rows this run created under its own tag and did not delete, and names each one: a row left
        /// behind is a defect of the test that created it, which is where the fix belongs.
        /// </summary>
        internal static void PurgeOwnResidue()
        {
            var leftovers = new List<string>();
            int removed = Sweep(row => RunTagOf(row) == RunLease.Tag, leftovers);
            if (removed > 0)
                Console.WriteLine($"[RouterOrphanCleaner] this run left {removed} row(s) behind — fix the test that "
                                  + "created them: " + string.Join(", ", leftovers));
        }

        private static bool OwnerHasEnded(ITikReSentence row, bool alone)
        {
            string tag = RunTagOf(row);
            return tag == null ? alone : !RunLease.IsAlive(tag);
        }

        private static string RunTagOf(ITikReSentence row)
        {
            foreach (string field in new[] { "name", "comment" })
            {
                var match = RunLease.TagPattern.Match(row.GetResponseFieldOrDefault(field, ""));
                if (match.Success)
                    return match.Groups[1].Value;
            }
            return null;
        }

        private static int Sweep(Func<ITikReSentence, bool> remove, List<string> removedNames)
        {
            string host = LabConfig.Get("host");
            string user = LabConfig.Get("user");
            string pass = LabConfig.Get("pass") ?? "";

            ITikConnection conn;
            try
            {
                conn = ConnectionFactory.CreateConnection(TikConnectionType.Api);
                conn.Open(host, user, pass);
            }
            catch
            {
                // No API reachable — the transport under test will surface the real connection error itself.
                return 0;
            }

            int removed = 0;
            using (conn)
            {
                foreach (string path in Paths)
                    removed += PurgePath(conn, path, remove, removedNames);
            }
            return removed;
        }

        private static int PurgePath(ITikConnection conn, string path, Func<ITikReSentence, bool> remove,
                                     List<string> removedNames)
        {
            List<ITikReSentence> rows;
            try
            {
                rows = conn.CreateCommand(path + "/print").ExecuteList()
                    .Where(remove)
                    .Where(s => !string.IsNullOrEmpty(s.GetResponseFieldOrDefault(".id", null)))
                    .ToList();
            }
            catch
            {
                // Path absent (package not installed) or not printable on this router — skip.
                return 0;
            }

            int removed = 0;
            foreach (var row in rows)
            {
                try
                {
                    conn.CreateCommandAndParameters(path + "/remove", ".id", row.GetResponseField(".id")).ExecuteNonQuery();
                    removed++;
                    removedNames?.Add(path + " " + (row.GetResponseFieldOrDefault("name", null)
                                                    ?? row.GetResponseFieldOrDefault("comment", "")));
                }
                catch
                {
                    // Still referenced by a row we clean later, or already gone — best effort.
                }
            }
            return removed;
        }

        private static bool IsTestResidue(ITikReSentence row)
        {
            string name = row.GetResponseFieldOrDefault("name", "");
            string comment = row.GetResponseFieldOrDefault("comment", "");
            return MatchesMarker(name) || MatchesMarker(comment) || IsGuid(comment);
        }

        private static bool MatchesMarker(string value) => TestNames.IsTestRowName(value);

        // The O/R mapper tests stamp a bare GUID comment precisely so a leftover is attributable, which also
        // covers the name-less menus (interface-list member, bridge vlan, graphing) that have nothing else to
        // match on.
        private static bool IsGuid(string value)
            => !string.IsNullOrEmpty(value) && Guid.TryParse(value, out _);
    }
}
