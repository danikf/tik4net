using System;
using System.Collections.Generic;
using System.Configuration;
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
        // Prefixes the suite stamps on every object it creates (on name or comment). On the dedicated test
        // router nothing else carries these, so a prefix match is a safe "this is ours".
        private static readonly string[] TestMarkers =
        {
            "t4n", "test-", "TEST", "tik4net-", "User for TEST",
        };

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
        };

        /// <summary>
        /// Opens an API connection and removes every test-marked row across <see cref="Paths"/>. Silent and
        /// non-fatal: if the API cannot be reached, or a menu/row cannot be removed, it is skipped — the goal
        /// is to clear conflicts, never to fail the run before it starts.
        /// </summary>
        internal static void PurgeTestResidue()
        {
            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            ITikConnection conn;
            try
            {
                conn = ConnectionFactory.CreateConnection(TikConnectionType.Api);
                conn.Open(host, user, pass);
            }
            catch
            {
                // No API reachable — the transport under test will surface the real connection error itself.
                return;
            }

            int removed = 0;
            using (conn)
            {
                foreach (string path in Paths)
                    removed += PurgePath(conn, path);
            }

            if (removed > 0)
                Console.WriteLine($"[RouterOrphanCleaner] swept {removed} orphaned test row(s) before the run.");
        }

        private static int PurgePath(ITikConnection conn, string path)
        {
            List<string> ids;
            try
            {
                ids = conn.CreateCommand(path + "/print").ExecuteList()
                    .Where(IsTestResidue)
                    .Select(s => s.GetResponseFieldOrDefault(".id", null))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }
            catch
            {
                // Path absent (package not installed) or not printable on this router — skip.
                return 0;
            }

            int removed = 0;
            foreach (string id in ids)
            {
                try
                {
                    conn.CreateCommandAndParameters(path + "/remove", ".id", id).ExecuteNonQuery();
                    removed++;
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

        private static bool MatchesMarker(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            foreach (string marker in TestMarkers)
                if (value.StartsWith(marker, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // The O/R mapper tests stamp a bare GUID comment precisely so a leftover is attributable, which also
        // covers the name-less menus (interface-list member, bridge vlan, graphing) that have nothing else to
        // match on.
        private static bool IsGuid(string value)
            => !string.IsNullOrEmpty(value) && Guid.TryParse(value, out _);
    }
}
