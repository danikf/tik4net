// TransportAuditFixtures.cs — rows for the tables a stock router leaves empty.
//
// The path-map audit passes a path it can read. On a freshly provisioned CHR only 61 of its 155 paths
// have any rows at all; the other 94 are compared as 0 rows against 0 rows and reported OK having
// measured nothing — the largest blind spot in the audit by far, and one that reads as agreement.
//
// This puts ONE row in each table that can be written without hardware, so those paths are measured
// against the API like any other, and takes them away again. It is used by the audit inside a
// try/finally: a run that throws still cleans up, because residue left on the router is a defect the
// NEXT run inherits (a name collision, an unexpected .id, a stale count).
//
// A recipe the router refuses is recorded and skipped, never fatal: a table may need a package this
// router does not have, and the point is to measure what CAN be measured. The skip list is written with
// the report, so a recipe that is simply wrong stays visible rather than silently absent.
//
// Everything created is named `tik4net-fx-…` where the table has a name, so an orphan from a killed run
// is recognisable — and SweepOrphans removes those before seeding rather than letting them collide with
// the new row.

using System;
using System.Collections.Generic;
using System.Linq;
using tik4net;

namespace tik4net.integrationtests
{
    internal sealed class TransportAuditFixtures : IDisposable
    {
        internal const string NamePrefix = "tik4net-fx-";

        private readonly ITikConnection _api;
        private readonly List<KeyValuePair<string, string>> _created = new List<KeyValuePair<string, string>>();
        private readonly List<string> _skipped = new List<string>();
        private readonly List<string> _leaked = new List<string>();

        internal TransportAuditFixtures(ITikConnection api) { _api = api; }

        internal int Created => _created.Count;
        internal IReadOnlyList<string> Skipped => _skipped;
        internal IReadOnlyList<string> Leaked => _leaked;

        /// <summary>Paths this run actually put a row into — the audit marks their lines as seeded.</summary>
        internal HashSet<string> SeededPaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The <c>.id</c> of the row this run created on a path, or null. The write audit writes to THESE
        /// and to nothing else — a row the suite made and will delete is the only thing it may disturb.
        /// </summary>
        internal string CreatedIdOn(string path)
        {
            foreach (var kv in _created)
                if (string.Equals(kv.Key, path, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        // ── the recipes ───────────────────────────────────────────────────────
        // Ordered by dependency: a bridge before its port, a pool before the DHCP server handing it out.
        // Removal walks the list backwards for the same reason.
        internal void SeedAll()
        {
            SweepOrphans();
            foreach (var r in Recipes) Add(r.Path, r.NameValues);
        }

        /// <summary>One row per table, as the arguments an <c>add</c> takes.</summary>
        /// <remarks>
        /// Data rather than a sequence of calls, because the write audit REPLAYS them: an <c>add</c> is
        /// measured by making the same row over native and over the API and comparing what the router ended
        /// up with. Order is dependency order — a bridge before its port, a pool before the DHCP server
        /// handing it out — and removal walks it backwards.
        /// <para>A row that sits in a live traffic path — a firewall or bridge rule, a routing rule, a
        /// walled-garden or access-list entry — carries <c>disabled=yes</c>. The audit does not just read
        /// these rows, it REWRITES a field on each and reorders the ordered ones, and a probe row that is
        /// live while that happens is a rule the router is actually enforcing against the connection running
        /// the audit. Disabled costs the measurement nothing: every comparison here is differential, so both
        /// sides see the same row.</para>
        /// </remarks>
        internal sealed class Recipe
        {
            internal string Path;
            internal string[] NameValues;
            internal Recipe(string path, params string[] nameValues) { Path = path; NameValues = nameValues; }
        }

        internal static readonly Recipe[] Recipes =
        {
            new Recipe("/ip/pool", "name", NamePrefix + "pool", "ranges", "10.99.0.10-10.99.0.20"),
            new Recipe("/interface/bridge", "name", NamePrefix + "br"),
            new Recipe("/interface/bridge/port", "bridge", NamePrefix + "br", "interface", "ether2"),
            new Recipe("/interface/bridge/vlan", "bridge", NamePrefix + "br", "vlan-ids", "999"),
            new Recipe("/interface/bridge/filter", "chain", "forward", "action", "accept", "disabled", "yes"),
            new Recipe("/interface/bridge/nat", "chain", "srcnat", "action", "accept", "disabled", "yes"),
            new Recipe("/interface/vlan", "name", NamePrefix + "vlan", "vlan-id", "999", "interface", "ether2"),
            new Recipe("/interface/eoip", "name", NamePrefix + "eoip", "remote-address", "10.99.0.2", "tunnel-id", "999"),
            // Over the EoIP tunnel, not over ether2: a bonding slave cannot already be a bridge port, and
            // this lab has exactly two ethers of which one carries the connection running the audit.
            new Recipe("/interface/bonding", "name", NamePrefix + "bond", "slaves", NamePrefix + "eoip"),
            new Recipe("/interface/gre", "name", NamePrefix + "gre", "remote-address", "10.99.0.3"),
            new Recipe("/interface/ipip", "name", NamePrefix + "ipip", "remote-address", "10.99.0.4"),
            new Recipe("/interface/vrrp", "name", NamePrefix + "vrrp", "interface", "ether2", "vrid", "99"),
            new Recipe("/interface/vxlan", "name", NamePrefix + "vxlan", "vni", "999"),
            new Recipe("/interface/wireguard", "name", NamePrefix + "wg"),
            new Recipe("/interface/wireguard/peers", "interface", NamePrefix + "wg", "public-key", "wGVEbHiKQhLBTQxWZqQmQvzFrPMPJVDgIiDzHEQNPmM=", "allowed-address", "10.99.1.0/24"),
            // Its own list: RouterOS refuses a member of a builtin one ("cannot add to builtin list").
            new Recipe("/interface/list", "name", NamePrefix + "list"),
            new Recipe("/interface/list/member", "list", NamePrefix + "list", "interface", "ether2"),
            new Recipe("/ip/dns/static", "name", NamePrefix + "host.invalid", "address", "10.99.0.5"),
            new Recipe("/ip/firewall/address-list", "list", NamePrefix + "list", "address", "10.99.0.6"),
            new Recipe("/ip/firewall/filter", "chain", "forward", "action", "accept", "disabled", "yes"),
            new Recipe("/ip/firewall/nat", "chain", "srcnat", "action", "accept", "disabled", "yes"),
            // protocol=tcp so the write audit's tcp-flags probe has something to bite on: RouterOS refuses
            // `tcp-flags` on a rule that does not match TCP, and the refusal would read as a transport
            // finding.
            new Recipe("/ip/firewall/mangle", "chain", "forward", "action", "accept", "protocol", "tcp", "disabled", "yes"),
            new Recipe("/ip/firewall/raw", "chain", "prerouting", "action", "accept", "disabled", "yes"),
            new Recipe("/ip/firewall/layer7-protocol", "name", NamePrefix + "l7", "regexp", "^tik4net$"),
            new Recipe("/ip/dhcp-server", "name", NamePrefix + "dhcp", "interface", "ether2", "address-pool", NamePrefix + "pool"),
            new Recipe("/ip/dhcp-server/network", "address", "10.99.0.0/24", "gateway", "10.99.0.1"),
            new Recipe("/ip/dhcp-server/lease", "address", "10.99.0.30", "mac-address", "02:00:00:99:00:01"),
            new Recipe("/ip/dhcp-relay", "name", NamePrefix + "relay", "interface", "ether2", "dhcp-server", "10.99.0.1"),
            new Recipe("/ip/hotspot/ip-binding", "address", "10.99.0.7", "type", "bypassed"),
            new Recipe("/ip/hotspot/walled-garden", "dst-host", "tik4net.invalid", "action", "allow", "disabled", "yes"),
            new Recipe("/ip/hotspot/walled-garden/ip", "dst-address", "10.99.0.8", "action", "accept", "disabled", "yes"),
            new Recipe("/ip/ipsec/peer", "name", NamePrefix + "peer", "address", "10.99.0.9"),
            new Recipe("/ip/ipsec/identity", "peer", NamePrefix + "peer", "secret", "tik4net-fixture"),
            new Recipe("/ip/proxy/access", "dst-host", "tik4net.invalid", "action", "deny", "disabled", "yes"),
            new Recipe("/ip/traffic-flow/target", "dst-address", "10.99.0.11", "port", "2055"),
            new Recipe("/ip/upnp/interfaces", "interface", "ether2", "type", "internal"),
            new Recipe("/ppp/secret", "name", NamePrefix + "user", "password", "tik4net-fixture"),
            new Recipe("/queue/simple", "name", NamePrefix + "queue", "target", "10.99.0.0/24"),
            new Recipe("/queue/tree", "name", NamePrefix + "tree", "parent", "global"),
            new Recipe("/radius", "service", "login", "address", "10.99.0.12", "secret", "tik4net-fixture"),
            new Recipe("/routing/filter/rule", "chain", NamePrefix + "chain", "rule", "accept", "disabled", "yes"),
            new Recipe("/routing/rule", "action", "lookup", "table", "main", "disabled", "yes"),
            new Recipe("/routing/ospf/instance", "name", NamePrefix + "ospf", "router-id", "10.99.0.13"),
            new Recipe("/routing/ospf/area", "name", NamePrefix + "area", "instance", NamePrefix + "ospf", "area-id", "0.0.0.99"),
            new Recipe("/routing/ospf/interface-template", "interfaces", "ether2", "area", NamePrefix + "area"),
            // A connection needs an instance that exists and a local role; RouterOS names both in the
            // trap when they are missing, one at a time.
            new Recipe("/routing/bgp/instance", "name", NamePrefix + "bgpi", "as", "65099", "router-id", "10.99.0.13"),
            new Recipe("/routing/bgp/connection", "name", NamePrefix + "bgp", "remote.address", "10.99.0.14", "as", "65099", "instance", NamePrefix + "bgpi", "local.role", "ebgp"),
            new Recipe("/system/scheduler", "name", NamePrefix + "sched", "on-event", ":nothing"),
            new Recipe("/system/script", "name", NamePrefix + "script", "source", ":nothing"),
            new Recipe("/tool/netwatch", "host", "10.99.0.15"),
            new Recipe("/caps-man/channel", "name", NamePrefix + "chan"),
            new Recipe("/caps-man/datapath", "name", NamePrefix + "dpath"),
            new Recipe("/caps-man/security", "name", NamePrefix + "sec"),
            new Recipe("/caps-man/configuration", "name", NamePrefix + "cfg"),
            new Recipe("/caps-man/provisioning", "action", "none", "disabled", "yes"),
            new Recipe("/caps-man/access-list", "action", "accept", "disabled", "yes"),
            new Recipe("/interface/wifi/channel", "name", NamePrefix + "wchan"),
            new Recipe("/interface/wifi/datapath", "name", NamePrefix + "wdpath"),
            new Recipe("/interface/wifi/security", "name", NamePrefix + "wsec"),
            new Recipe("/interface/wifi/configuration", "name", NamePrefix + "wcfg"),
            new Recipe("/interface/wifi/provisioning", "action", "none", "disabled", "yes"),
            new Recipe("/interface/wifi/access-list", "action", "accept", "disabled", "yes"),
        };

        // ── mechanics ─────────────────────────────────────────────────────────

        private void Add(string path, params string[] nameValues)
        {
            try
            {
                string id = _api.CreateCommandAndParameters(path + "/add", nameValues).ExecuteScalar();
                if (string.IsNullOrEmpty(id))
                {
                    // An add that answers nothing leaves a row this cannot delete by id. Better to leave
                    // the path unmeasured than to leave something behind that nothing will remove.
                    _skipped.Add($"{path}\tadd returned no .id");
                    return;
                }
                _created.Add(new KeyValuePair<string, string>(path, id));
                SeededPaths.Add(path);
            }
            catch (Exception ex)
            {
                _skipped.Add($"{path}\t{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes what a KILLED earlier run left behind, before seeding. Only rows this class names, and
        /// only on the tables it writes: an orphan is what makes the next run fail on a collision, and
        /// deleting it by hand only resets the clock until the run after that.
        /// </summary>
        private void SweepOrphans()
        {
            foreach (string path in new[]
                     {
                         "/interface/bridge/port", "/interface/bridge/vlan", "/ip/dhcp-server",
                         "/interface/wireguard/peers", "/routing/ospf/interface-template",
                         "/routing/ospf/area", "/routing/ospf/instance", "/ip/ipsec/identity",
                         "/ip/pool", "/interface/bridge", "/interface/vlan", "/interface/bonding",
                         "/interface/eoip", "/interface/gre", "/interface/ipip", "/interface/vrrp",
                         "/interface/vxlan", "/interface/wireguard", "/ip/firewall/layer7-protocol",
                         "/ip/ipsec/peer", "/ppp/secret", "/queue/simple", "/queue/tree",
                         "/system/scheduler", "/system/script", "/ip/dhcp-relay",
                         "/routing/bgp/connection", "/routing/bgp/instance", "/interface/list",
                         "/caps-man/channel", "/caps-man/datapath",
                         "/caps-man/security", "/caps-man/configuration", "/interface/wifi/channel",
                         "/interface/wifi/datapath", "/interface/wifi/security",
                         "/interface/wifi/configuration",
                     })
            {
                try
                {
                    var orphans = _api.CreateCommand(path + "/print").ExecuteList()
                        .Where(r => (r.GetResponseFieldOrDefault("name", "") ?? "")
                                    .StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var row in orphans)
                        _api.CreateCommandAndParameters(path + "/remove", ".id", row.GetId()).ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // A table this router does not have cannot be holding an orphan.
                }
            }
        }

        public void Dispose()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                var row = _created[i];
                try
                {
                    _api.CreateCommandAndParameters(row.Key + "/remove", ".id", row.Value).ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _leaked.Add($"{row.Key} {row.Value}\t{ex.GetType().Name}: {ex.Message}");
                }
            }
            _created.Clear();
        }
    }
}
