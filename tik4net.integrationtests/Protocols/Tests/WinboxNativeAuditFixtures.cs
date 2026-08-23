// WinboxNativeAuditFixtures.cs — rows for the tables a stock router leaves empty.
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
    internal sealed class WinboxNativeAuditFixtures : IDisposable
    {
        internal const string NamePrefix = "tik4net-fx-";

        private readonly ITikConnection _api;
        private readonly List<KeyValuePair<string, string>> _created = new List<KeyValuePair<string, string>>();
        private readonly List<string> _skipped = new List<string>();
        private readonly List<string> _leaked = new List<string>();

        internal WinboxNativeAuditFixtures(ITikConnection api) { _api = api; }

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

            Add("/ip/pool", "name", NamePrefix + "pool", "ranges", "10.99.0.10-10.99.0.20");

            Add("/interface/bridge", "name", NamePrefix + "br");
            Add("/interface/bridge/port", "bridge", NamePrefix + "br", "interface", "ether2");
            Add("/interface/bridge/vlan", "bridge", NamePrefix + "br", "vlan-ids", "999");
            Add("/interface/bridge/filter", "chain", "forward", "action", "accept");
            Add("/interface/bridge/nat", "chain", "srcnat", "action", "accept");

            Add("/interface/vlan", "name", NamePrefix + "vlan", "vlan-id", "999", "interface", "ether2");
            Add("/interface/eoip", "name", NamePrefix + "eoip", "remote-address", "10.99.0.2",
                "tunnel-id", "999");
            // Over the EoIP tunnel, not over ether2: a bonding slave cannot already be a bridge port, and
            // this lab has exactly two ethers of which one carries the connection running the audit.
            Add("/interface/bonding", "name", NamePrefix + "bond", "slaves", NamePrefix + "eoip");
            Add("/interface/gre", "name", NamePrefix + "gre", "remote-address", "10.99.0.3");
            Add("/interface/ipip", "name", NamePrefix + "ipip", "remote-address", "10.99.0.4");
            Add("/interface/vrrp", "name", NamePrefix + "vrrp", "interface", "ether2", "vrid", "99");
            Add("/interface/vxlan", "name", NamePrefix + "vxlan", "vni", "999");
            Add("/interface/wireguard", "name", NamePrefix + "wg");
            Add("/interface/wireguard/peers", "interface", NamePrefix + "wg",
                "public-key", "wGVEbHiKQhLBTQxWZqQmQvzFrPMPJVDgIiDzHEQNPmM=",
                "allowed-address", "10.99.1.0/24");

            // Its own list: RouterOS refuses a member of a builtin one ("cannot add to builtin list").
            Add("/interface/list", "name", NamePrefix + "list");
            Add("/interface/list/member", "list", NamePrefix + "list", "interface", "ether2");

            Add("/ip/dns/static", "name", NamePrefix + "host.invalid", "address", "10.99.0.5");
            Add("/ip/firewall/address-list", "list", NamePrefix + "list", "address", "10.99.0.6");
            Add("/ip/firewall/filter", "chain", "forward", "action", "accept");
            Add("/ip/firewall/nat", "chain", "srcnat", "action", "accept");
            // protocol=tcp so the write audit's tcp-flags probe has something to bite on: RouterOS refuses
            // `tcp-flags` on a rule that does not match TCP, and the refusal would read as a transport
            // finding.
            Add("/ip/firewall/mangle", "chain", "forward", "action", "accept", "protocol", "tcp");
            Add("/ip/firewall/raw", "chain", "prerouting", "action", "accept");
            Add("/ip/firewall/layer7-protocol", "name", NamePrefix + "l7", "regexp", "^tik4net$");

            Add("/ip/dhcp-server", "name", NamePrefix + "dhcp", "interface", "ether2",
                "address-pool", NamePrefix + "pool");
            Add("/ip/dhcp-server/network", "address", "10.99.0.0/24", "gateway", "10.99.0.1");
            Add("/ip/dhcp-server/lease", "address", "10.99.0.30", "mac-address", "02:00:00:99:00:01");
            Add("/ip/dhcp-relay", "name", NamePrefix + "relay", "interface", "ether2",
                "dhcp-server", "10.99.0.1");

            Add("/ip/hotspot/ip-binding", "address", "10.99.0.7", "type", "bypassed");
            Add("/ip/hotspot/walled-garden", "dst-host", "tik4net.invalid", "action", "allow");
            Add("/ip/hotspot/walled-garden/ip", "dst-address", "10.99.0.8", "action", "accept");

            Add("/ip/ipsec/peer", "name", NamePrefix + "peer", "address", "10.99.0.9");
            Add("/ip/ipsec/identity", "peer", NamePrefix + "peer", "secret", "tik4net-fixture");

            Add("/ip/proxy/access", "dst-host", "tik4net.invalid", "action", "deny");
            Add("/ip/traffic-flow/target", "dst-address", "10.99.0.11", "port", "2055");
            Add("/ip/upnp/interfaces", "interface", "ether2", "type", "internal");

            Add("/ppp/secret", "name", NamePrefix + "user", "password", "tik4net-fixture");
            Add("/queue/simple", "name", NamePrefix + "queue", "target", "10.99.0.0/24");
            Add("/queue/tree", "name", NamePrefix + "tree", "parent", "global");

            Add("/radius", "service", "login", "address", "10.99.0.12", "secret", "tik4net-fixture");

            Add("/routing/filter/rule", "chain", NamePrefix + "chain", "rule", "accept");
            Add("/routing/rule", "action", "lookup", "table", "main");
            Add("/routing/ospf/instance", "name", NamePrefix + "ospf", "router-id", "10.99.0.13");
            Add("/routing/ospf/area", "name", NamePrefix + "area", "instance", NamePrefix + "ospf",
                "area-id", "0.0.0.99");
            Add("/routing/ospf/interface-template", "interfaces", "ether2", "area", NamePrefix + "area");
            // A connection needs an instance that exists and a local role; RouterOS names both in the
            // trap when they are missing, one at a time.
            Add("/routing/bgp/instance", "name", NamePrefix + "bgpi", "as", "65099",
                "router-id", "10.99.0.13");
            Add("/routing/bgp/connection", "name", NamePrefix + "bgp", "remote.address", "10.99.0.14",
                "as", "65099", "instance", NamePrefix + "bgpi", "local.role", "ebgp");

            Add("/system/scheduler", "name", NamePrefix + "sched", "on-event", ":nothing");
            Add("/system/script", "name", NamePrefix + "script", "source", ":nothing");
            Add("/tool/netwatch", "host", "10.99.0.15");

            Add("/caps-man/channel", "name", NamePrefix + "chan");
            Add("/caps-man/datapath", "name", NamePrefix + "dpath");
            Add("/caps-man/security", "name", NamePrefix + "sec");
            Add("/caps-man/configuration", "name", NamePrefix + "cfg");
            Add("/caps-man/provisioning", "action", "none");
            Add("/caps-man/access-list", "action", "accept");

            Add("/interface/wifi/channel", "name", NamePrefix + "wchan");
            Add("/interface/wifi/datapath", "name", NamePrefix + "wdpath");
            Add("/interface/wifi/security", "name", NamePrefix + "wsec");
            Add("/interface/wifi/configuration", "name", NamePrefix + "wcfg");
            Add("/interface/wifi/provisioning", "action", "none");
            Add("/interface/wifi/access-list", "action", "accept");
        }

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
