// WinboxNativeWriteAudit.cs — the half of the contract the path-map audit never measured.
//
// The path-map audit is a READ, all 154 paths of it. Every mapping it checks is exercised in one
// direction only, and the direction it skips is the one where a wrong mapping does damage rather than
// mislead: a read that names a key wrongly reports a wrong value, a WRITE that names a key wrongly
// changes the wrong setting on the router.
//
// The measurement is differential, so it needs no knowledge of how RouterOS normalises a value —
// which is exactly the knowledge a value-comparing test would have to get right, and would get wrong.
// Per field:
//
//   1. write the probe over the API, read the row back over the API   → `expected`
//   2. write the probe over WinBox-native, read the row back over the API → `actual`
//   3. restore
//
// `expected` and `actual` are both the API's own print of the same field after the same requested
// change, so anything RouterOS does to the value on its way in happens to both. What is left is the
// only question worth asking: did the native write land what the API write lands?
//
// Everything is written to a FIXTURE row — a row this suite created and will delete — so a probe that
// goes wrong cannot disturb the router's own configuration. That is why the write audit rides on the
// seeded audit rather than standing alone.

using System;
using System.Collections.Generic;
using System.Linq;
using tik4net;

namespace tik4net.integrationtests
{
    internal sealed class WinboxNativeWriteAudit
    {
        /// <summary>What one probe did.</summary>
        internal enum Outcome
        {
            /// <summary>The native write landed what the API write lands.</summary>
            Ok,
            /// <summary>Both wrote, and the router ended up with different values.</summary>
            Different,
            /// <summary>The native write threw or was refused where the API's went through.</summary>
            Refused,
            /// <summary>The API's own write did not go through, so there is nothing to compare against.</summary>
            NotProbeable,
        }

        internal sealed class Result
        {
            internal string Path = "";
            internal string Field = "";
            internal Outcome Outcome;
            internal string Detail = "";
        }

        // ── the probes ────────────────────────────────────────────────────────
        // One writable field per path, chosen for the TYPE it exercises rather than for importance: the
        // encoder is what is under test, and a table of twenty strings would measure one code path twenty
        // times. Every value is inert on a fixture row.
        //
        // `comment` is deliberately NOT the probe: it is a system key every path shares, so it would
        // measure the same encoder everywhere and report broad coverage of nothing.
        private static readonly Dictionary<string, (string Field, string Value)> Probes =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                // strings
                ["/interface/bridge"]              = ("name", "tik4net-fx-br2"),
                ["/ip/firewall/layer7-protocol"]   = ("regexp", "^tik4net-probe$"),
                ["/system/script"]                 = ("source", ":put 1"),
                // numbers
                ["/interface/vlan"]                = ("vlan-id", "998"),
                ["/interface/bridge/port"]         = ("horizon", "7"),
                ["/routing/ospf/interface-template"] = ("cost", "42"),
                // bools
                ["/ip/firewall/filter"]            = ("log", "yes"),
                ["/interface/wireguard"]           = ("disabled", "yes"),
                // enums
                ["/ip/firewall/nat"]               = ("action", "masquerade"),
                ["/ip/hotspot/ip-binding"]         = ("type", "blocked"),
                ["/interface/bridge/nat"]          = ("chain", "dstnat"),
                // addresses and prefixes
                ["/ip/firewall/address-list"]      = ("address", "10.99.0.77"),
                ["/ip/dns/static"]                 = ("address", "10.99.0.78"),
                ["/ip/ipsec/peer"]                 = ("address", "10.99.0.79"),
                ["/queue/simple"]                  = ("target", "10.99.1.0/24"),
                // durations
                ["/tool/netwatch"]                 = ("interval", "45s"),
                ["/ip/ipsec/profile"]              = ("dpd-interval", "3m"),
                // lists
                ["/ip/pool"]                       = ("ranges", "10.99.0.40-10.99.0.50"),
                // a reference to another table
                ["/ip/dhcp-server"]                = ("address-pool", "static-only"),

                // ── the rest of the seeded paths ──────────────────────────────
                // A bitmask, which is a whole encoder of its own: the value is a set of members joined by
                // commas and RouterOS normalises the ORDER, so the differential comparison is the only way
                // to check one without hard-coding the direction it prints in (see SetPrintedDescending).
                ["/ip/firewall/mangle"]            = ("tcp-flags", "syn,!ack"),
                ["/ip/firewall/raw"]               = ("protocol", "udp"),
                ["/interface/bridge/filter"]       = ("mac-protocol", "arp"),
                ["/ip/hotspot/walled-garden"]      = ("dst-host", "tik4net-probe.invalid"),
                ["/ip/hotspot/walled-garden/ip"]   = ("dst-address", "10.99.0.80"),
                ["/ip/proxy/access"]               = ("dst-host", "tik4net-probe.invalid"),
                ["/ip/traffic-flow/target"]        = ("port", "9995"),
                ["/ip/upnp/interfaces"]            = ("type", "external"),
                ["/ip/dhcp-server/lease"]          = ("address", "10.99.0.31"),
                ["/ip/dhcp-server/network"]        = ("gateway", "10.99.0.2"),
                ["/ip/dhcp-relay"]                 = ("dhcp-server", "10.99.0.3"),
                ["/ip/ipsec/identity"]             = ("secret", "tik4net-probe"),
                ["/ppp/secret"]                    = ("service", "pppoe"),
                ["/queue/tree"]                    = ("priority", "3"),
                ["/radius"]                        = ("timeout", "500ms"),
                ["/routing/filter/rule"]           = ("rule", "reject"),
                ["/routing/rule"]                  = ("action", "drop"),
                ["/routing/ospf/instance"]         = ("router-id", "10.99.0.99"),
                ["/routing/ospf/area"]             = ("area-id", "0.0.0.98"),
                ["/routing/bgp/instance"]          = ("router-id", "10.99.0.97"),
                ["/routing/bgp/connection"]        = ("hold-time", "4m"),
                ["/system/scheduler"]              = ("interval", "1h"),
                ["/interface/bridge/vlan"]         = ("vlan-ids", "997"),
                ["/interface/list"]                = ("name", "tik4net-fx-list2"),
                ["/interface/eoip"]                = ("tunnel-id", "998"),
                ["/interface/gre"]                 = ("remote-address", "10.99.0.83"),
                ["/interface/ipip"]                = ("remote-address", "10.99.0.84"),
                ["/interface/vrrp"]                = ("priority", "77"),
                ["/interface/vxlan"]               = ("vni", "998"),
                ["/interface/wireguard/peers"]     = ("allowed-address", "10.99.2.0/24"),
                ["/interface/bonding"]             = ("mode", "active-backup"),
                ["/caps-man/channel"]              = ("frequency", "2412"),
                ["/caps-man/datapath"]             = ("client-to-client-forwarding", "yes"),
                ["/caps-man/security"]             = ("authentication-types", "wpa2-psk"),
                ["/caps-man/configuration"]        = ("ssid", "tik4net-probe"),
                ["/caps-man/provisioning"]         = ("action", "create-dynamic-enabled"),
                ["/caps-man/access-list"]          = ("action", "reject"),
                ["/interface/wifi/channel"]        = ("frequency", "2412"),
                ["/interface/wifi/datapath"]       = ("client-isolation", "yes"),
                ["/interface/wifi/security"]       = ("authentication-types", "wpa2-psk"),
                ["/interface/wifi/configuration"]  = ("ssid", "tik4net-probe"),
                ["/interface/wifi/access-list"]    = ("action", "reject"),
                ["/interface/list/member"]         = ("interface", "ether1"),
            };

        private readonly ITikConnection _api;
        private readonly ITikConnection _native;
        private readonly List<Result> _results = new List<Result>();

        internal WinboxNativeWriteAudit(ITikConnection api, ITikConnection native)
        {
            _api = api;
            _native = native;
        }

        internal IReadOnlyList<Result> Results => _results;

        internal int Count(Outcome o) => _results.Count(r => r.Outcome == o);

        /// <summary>
        /// Runs every probe for which this run created a row. A path whose row is not ours is skipped
        /// rather than written to: the whole safety of this audit is that it only ever changes a row the
        /// suite made and will delete.
        /// </summary>
        internal void Run(WinboxNativeAuditFixtures fixtures)
        {
            foreach (var probe in Probes)
            {
                string id = fixtures.CreatedIdOn(probe.Key);
                if (id == null) continue;
                _results.Add(RunOne(probe.Key, id, probe.Value.Field, probe.Value.Value));
            }
        }

        private Result RunOne(string path, string id, string field, string probeValue)
        {
            var r = new Result { Path = path, Field = field };

            string original;
            try
            {
                var row = _api.CreateCommand(path + "/print").ExecuteList()
                              .FirstOrDefault(x => x.GetId() == id);
                if (row == null)
                {
                    r.Outcome = Outcome.NotProbeable;
                    r.Detail = "the fixture row is gone";
                    return r;
                }
                original = row.GetResponseFieldOrDefault(field, "");
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.NotProbeable;
                r.Detail = "read: " + ex.Message;
                return r;
            }

            // Native goes FIRST, and nothing is put back between the two writes. `set field=value` is
            // absolute, not relative, so whatever the native write left behind — the probe, a wrong value,
            // or nothing — the API write that follows lands the same thing either way.
            //
            // The order matters for a duller reason too. Restoring in the middle meant writing the ORIGINAL
            // back, and where the original is an unset field that write is `log=""`, which RouterOS refuses.
            // The refusal came from the API call and was reported against the transport standing next to it
            // in the same try block: two of the first sixteen probes were recorded as native refusals when
            // the native write had gone through perfectly.
            string actual;
            try
            {
                Set(_native, path, id, field, probeValue);
                actual = ReadBack(path, id, field);
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.Refused;
                r.Detail = $"native write threw: {ex.GetType().Name}: {ex.Message}";
                TryRestore(path, id, field, original);
                return r;
            }

            string expected;
            try
            {
                Set(_api, path, id, field, probeValue);
                expected = ReadBack(path, id, field);
            }
            catch (Exception ex)
            {
                // The API cannot write it either, so there is nothing to hold the native write against.
                r.Outcome = Outcome.NotProbeable;
                r.Detail = "api write: " + ex.Message;
                TryRestore(path, id, field, original);
                return r;
            }

            TryRestore(path, id, field, original);

            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                r.Outcome = Outcome.Ok;
                r.Detail = $"{field}={probeValue} → '{actual}'";
            }
            else
            {
                r.Outcome = Outcome.Different;
                r.Detail = $"{field}={probeValue}: api write gave '{expected}', native write gave '{actual}'";
            }
            return r;
        }

        private static void Set(ITikConnection conn, string path, string id, string field, string value)
            => conn.CreateCommandAndParameters(path + "/set", ".id", id, field, value).ExecuteNonQuery();

        private string ReadBack(string path, string id, string field)
        {
            var row = _api.CreateCommand(path + "/print").ExecuteList()
                          .FirstOrDefault(x => x.GetId() == id);
            return row == null ? "(row gone)" : row.GetResponseFieldOrDefault(field, "");
        }

        // A restore that throws would mask the finding with its own exception — and where the original was
        // an unset field, `field=""` is a write RouterOS refuses. The row is deleted at the end of the run
        // anyway, so a failed restore costs nothing.
        private void TryRestore(string path, string id, string field, string original)
        {
            try { Set(_api, path, id, field, original); }
            catch (Exception) { }
        }
    }
}
