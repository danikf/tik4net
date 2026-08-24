// TransportWriteAudit.cs — the half of the contract the path-map audit never measured.
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
//   2. write the probe over the transport under test, read the row back over the API → `actual`
//   3. restore
//
// `expected` and `actual` are both the API's own print of the same field after the same requested
// change, so anything RouterOS does to the value on its way in happens to both. What is left is the
// only question worth asking: did that transport's write land what the API write lands?
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
    internal sealed class TransportWriteAudit
    {
        /// <summary>What one probe did.</summary>
        internal enum Outcome
        {
            /// <summary>The probed write landed what the API write lands.</summary>
            Ok,
            /// <summary>Both wrote, and the router ended up with different values.</summary>
            Different,
            /// <summary>The probed write threw or was refused where the API's went through.</summary>
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
        private readonly ITikConnection _probe;
        // What to call the transport under test in a finding. It is a label, not a decision: nothing here
        // branches on which transport it is, and a probe that needed to would not be differential any more.
        private readonly string _probeName;
        private readonly List<Result> _results = new List<Result>();

        internal TransportWriteAudit(ITikConnection api, ITikConnection probe, string probeName)
        {
            _api = api;
            _probe = probe;
            _probeName = probeName;
        }

        internal IReadOnlyList<Result> Results => _results;

        internal int Count(Outcome o) => _results.Count(r => r.Outcome == o);

        /// <summary>
        /// Runs every probe for which this run created a row. A path whose row is not ours is skipped
        /// rather than written to: the whole safety of this audit is that it only ever changes a row the
        /// suite made and will delete.
        /// </summary>
        internal void Run(TransportAuditFixtures fixtures)
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
                Set(_probe, path, id, field, probeValue);
                actual = ReadBack(path, id, field);
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.Refused;
                r.Detail = $"{_probeName} write threw: {ex.GetType().Name}: {ex.Message}";
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
                r.Detail = $"{field}={probeValue}: api write gave '{expected}', {_probeName} write gave '{actual}'";
            }
            return r;
        }

        // ── add and remove ────────────────────────────────────────────────────

        /// <summary>
        /// The other two verbs, measured the same differential way: make the SAME row over native and over
        /// the API, and compare what the router ended up with field by field. Then take each away again —
        /// the native row over NATIVE, which is what measures <c>remove</c>.
        /// </summary>
        /// <remarks>
        /// Only the recipes that already SEEDED successfully are replayed. A recipe this router refuses is
        /// a statement about the router, and running it twice more would say it twice more.
        /// <para>The two rows differ in one thing by construction — their name, since a table that has one
        /// will not take a duplicate. That field, and <c>.id</c>, are the only ones excluded.</para>
        /// </remarks>
        internal void RunAddsAndRemoves(TransportAuditFixtures fixtures)
        {
            foreach (var recipe in TransportAuditFixtures.Recipes)
            {
                if (fixtures.CreatedIdOn(recipe.Path) == null) continue;
                _results.Add(RunAddRemove(recipe));
            }
        }

        private Result RunAddRemove(TransportAuditFixtures.Recipe recipe)
        {
            var r = new Result { Path = recipe.Path, Field = "(add)" };
            string[] probeArgs = Rename(recipe.NameValues, "ax");
            string[] apiArgs = Rename(recipe.NameValues, "bx");

            string probeId = null, apiId = null;
            string probeFailure = null;
            try
            {
                probeId = _probe.CreateCommandAndParameters(recipe.Path + "/add", probeArgs).ExecuteScalar();
            }
            catch (Exception ex)
            {
                probeFailure = $"{ex.GetType().Name}: {ex.Message}";
            }

            // The API's add is attempted whatever happened, because it is what says which kind of failure
            // the native one was. A table that already holds the fixture row and keys on something the probe
            // cannot vary — an address, an interface, a bridge port — refuses BOTH, and refusing both is the
            // router talking about the row, not about the transport. Guessing that from the trap TEXT
            // ('device already added', 'instance already has area', 'Multiple initiator peers') would be a
            // list of English phrases pretending to be a rule.
            try
            {
                apiId = _api.CreateCommandAndParameters(recipe.Path + "/add", apiArgs).ExecuteScalar();
            }
            catch (Exception ex)
            {
                Cleanup(recipe.Path, probeId, viaProbe: false);
                r.Outcome = Outcome.NotProbeable;
                r.Detail = probeFailure == null
                    ? "api add: " + ex.Message
                    : "both transports were refused, so the router is refusing the ROW: " + ex.Message;
                return r;
            }

            if (probeFailure != null)
            {
                Cleanup(recipe.Path, apiId, viaProbe: false);
                r.Outcome = Outcome.Refused;
                r.Detail = _probeName + " add threw where the API's went through: " + probeFailure;
                return r;
            }

            string diff = CompareRows(recipe, probeId, apiId);

            // The native row goes away over NATIVE — that is the remove probe. The API's goes over the API.
            string removeProblem = Cleanup(recipe.Path, probeId, viaProbe: true);
            Cleanup(recipe.Path, apiId, viaProbe: false);

            if (removeProblem != null)
            {
                r.Field = "(remove)";
                r.Outcome = Outcome.Refused;
                r.Detail = removeProblem;
                return r;
            }
            if (diff != null)
            {
                r.Outcome = Outcome.Different;
                r.Detail = diff;
                return r;
            }
            r.Outcome = Outcome.Ok;
            r.Detail = "add and remove agree with the API's";
            return r;
        }

        /// <summary>
        /// The recipe with only the row's OWN name changed, so the probe row and the fixture row can share
        /// a table. A value that merely REFERS to a fixture row — a bridge port's bridge, an OSPF area's
        /// instance — is left exactly as it is: renaming it would point the probe at a row that does not
        /// exist, and the router's refusal would read as a finding about the transport.
        /// </summary>
        private static string[] Rename(string[] nameValues, string tag)
        {
            var copy = (string[])nameValues.Clone();
            for (int i = 0; i + 1 < copy.Length; i += 2)
            {
                if (!string.Equals(copy[i], "name", StringComparison.OrdinalIgnoreCase)) continue;
                if (copy[i + 1] != null && copy[i + 1].StartsWith(TransportAuditFixtures.NamePrefix,
                                                                 StringComparison.OrdinalIgnoreCase))
                    copy[i + 1] = "tik4net-" + tag + "-"
                                + copy[i + 1].Substring(TransportAuditFixtures.NamePrefix.Length);
            }
            return copy;
        }

        /// <summary>The first field the two rows disagree on, or null when they agree.</summary>
        private string CompareRows(TransportAuditFixtures.Recipe recipe, string probeId, string apiId)
        {
            string path = recipe.Path;
            List<ITikReSentence> rows;
            try { rows = _api.CreateCommand(path + "/print").ExecuteList().ToList(); }
            catch (Exception ex) { return "read-back: " + ex.Message; }

            var mine = rows.FirstOrDefault(x => x.GetId() == probeId);
            var theirs = rows.FirstOrDefault(x => x.GetId() == apiId);
            if (mine == null) return "the row the " + _probeName + " add reported is not in the table";
            if (theirs == null) return "the row the API add reported is not in the table";

            // Only the fields the RECIPE asked for. Everything else on a fresh row is the router's own
            // business and much of it cannot agree by construction: a bridge is born with a random MAC, a
            // wireguard interface with a random listen-port, a firewall rule with its own counters and an
            // `invalid` flag that depends on what else is in the chain. Comparing those measured the
            // router's imagination.
            for (int i = 0; i + 1 < recipe.NameValues.Length; i += 2)
            {
                string field = recipe.NameValues[i];
                if (string.Equals(field, "name", StringComparison.OrdinalIgnoreCase)) continue;
                string theirsValue = theirs.GetResponseFieldOrDefault(field, "(absent)");
                string oursValue = mine.GetResponseFieldOrDefault(field, "(absent)");
                if (!string.Equals(theirsValue, oursValue, StringComparison.Ordinal))
                    return $"{field}: api add gave '{theirsValue}', {_probeName} add gave '{oursValue}'";
            }
            return null;
        }

        // Returns null when the row is gone, or what went wrong.
        private string Cleanup(string path, string id, bool viaProbe)
        {
            if (id == null) return null;
            var conn = viaProbe ? _probe : _api;
            try
            {
                conn.CreateCommandAndParameters(path + "/remove", ".id", id).ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                string why = $"{(viaProbe ? _probeName : "api")} remove threw: {ex.GetType().Name}: {ex.Message}";
                if (viaProbe) { TryRemoveOverApi(path, id); return why; }
                return why;
            }
            // Accepted is not the same as done — a set-singleton answers status 0 and changes nothing.
            try
            {
                if (_api.CreateCommand(path + "/print").ExecuteList().Any(x => x.GetId() == id))
                {
                    if (viaProbe) { TryRemoveOverApi(path, id); return _probeName + " remove was accepted and the row is still there"; }
                    return "api remove was accepted and the row is still there";
                }
            }
            catch (Exception) { }
            return null;
        }

        private void TryRemoveOverApi(string path, string id)
        {
            try { _api.CreateCommandAndParameters(path + "/remove", ".id", id).ExecuteNonQuery(); }
            catch (Exception) { }
        }

        private static void Set(ITikConnection conn, string path, string id, string field, string value)
            => conn.CreateCommandAndParameters(path + "/set", ".id", id, field, value).ExecuteNonQuery();

        private string ReadBack(string path, string id, string field)
        {
            var row = _api.CreateCommand(path + "/print").ExecuteList()
                          .FirstOrDefault(x => x.GetId() == id);
            return row == null ? "(row gone)" : row.GetResponseFieldOrDefault(field, "");
        }

        // ── enable / disable ──────────────────────────────────────────────────

        /// <summary>
        /// The toggle verbs, on every fixture row that has a <c>disabled</c> field.
        /// </summary>
        /// <remarks>
        /// <see cref="VerbMatrixTest"/> already covers these per TRANSPORT, but on one path. What it cannot
        /// see is that native does not send an <c>enable</c> at all: it writes the <c>disabled</c> field,
        /// whose M2 key is per window. So the verb is proven on <c>/ip/firewall/filter</c> and unproven on
        /// the other sixty tables, each of which resolves that field through its own catalog entry — and a
        /// window that spells the flag the other way round would silently toggle the wrong direction.
        /// </remarks>
        internal void RunToggles(TransportAuditFixtures fixtures)
        {
            foreach (var recipe in TransportAuditFixtures.Recipes)
            {
                string id = fixtures.CreatedIdOn(recipe.Path);
                if (id == null) continue;

                var row = ReadRow(recipe.Path, id);
                if (row == null || row.GetResponseFieldOrDefault("disabled", null) == null) continue;

                string original = row.GetResponseFieldOrDefault("disabled", "false");
                _results.Add(RunToggle(recipe.Path, id, "disable"));
                _results.Add(RunToggle(recipe.Path, id, "enable"));
                TryVerb(_api, recipe.Path, id, original == "true" ? "disable" : "enable");
            }
        }

        // Native first, then the API, for the reason the set probe does it: both verbs name an absolute
        // state rather than a relative one, so the API's call lands the same thing whatever native left.
        private Result RunToggle(string path, string id, string verb)
        {
            var r = new Result { Path = path, Field = "(" + verb + ")" };

            string actual;
            try
            {
                Verb(_probe, path, id, verb);
                actual = ReadBack(path, id, "disabled");
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.Refused;
                r.Detail = _probeName + " " + verb + " threw: " + ex.GetType().Name + ": " + ex.Message;
                return r;
            }

            string expected;
            try
            {
                Verb(_api, path, id, verb);
                expected = ReadBack(path, id, "disabled");
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.NotProbeable;
                r.Detail = "api " + verb + ": " + ex.Message;
                return r;
            }

            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                r.Outcome = Outcome.Ok;
                r.Detail = verb + " → disabled='" + actual + "'";
            }
            else
            {
                r.Outcome = Outcome.Different;
                r.Detail = verb + ": api gave disabled='" + expected + "', " + _probeName + " gave '" + actual + "'";
            }
            return r;
        }

        // ── unset ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>unset</c> on the same field each path's probe entry writes.
        /// </summary>
        /// <remarks>
        /// Native has no unset operation either — it translates the verb into a write of the field's empty
        /// or default form, and what "empty" means is a question about the FIELD's type, not about the verb:
        /// a string's is a zero-length string, a number's is a catalog-declared sentinel, an enum's is a
        /// member. The verb therefore has one implementation and as many correct answers as there are types,
        /// which is exactly the shape that cannot be measured on a single path.
        /// <para>The field is set to the probe value first, over the API, so there is always something to
        /// unset — unsetting a field that is already unset is a measurement that cannot fail.</para>
        /// </remarks>
        internal void RunUnsets(TransportAuditFixtures fixtures)
        {
            foreach (var probe in Probes)
            {
                string id = fixtures.CreatedIdOn(probe.Key);
                if (id == null) continue;
                _results.Add(RunUnset(probe.Key, id, probe.Value.Field, probe.Value.Value));
            }
        }

        private Result RunUnset(string path, string id, string field, string probeValue)
        {
            var r = new Result { Path = path, Field = "(unset " + field + ")" };

            var row = ReadRow(path, id);
            if (row == null)
            {
                r.Outcome = Outcome.NotProbeable;
                r.Detail = "the fixture row is gone";
                return r;
            }
            string original = row.GetResponseFieldOrDefault(field, "");

            string actual = null;
            string probeFailure = null;
            try
            {
                Set(_api, path, id, field, probeValue);
                Unset(_probe, path, id, field);
                actual = ReadBack(path, id, field);
            }
            catch (Exception ex)
            {
                // Not a finding yet. Most of these fields are MANDATORY, and the router refuses to clear one
                // whichever transport asks — so the API's attempt below is what says which kind of refusal
                // this was. Returning here would have filed 'can not set empty name' against native when
                // the API cannot clear a bridge's name either; the add probe learnt this first.
                probeFailure = ex.GetType().Name + ": " + ex.Message;
            }

            string expected;
            try
            {
                Set(_api, path, id, field, probeValue);
                Unset(_api, path, id, field);
                expected = ReadBack(path, id, field);
            }
            catch (Exception ex)
            {
                // The router will not unset this field over the API either — a mandatory field, mostly. The
                // native attempt above is then held against nothing, which is what NotProbeable means.
                r.Outcome = Outcome.NotProbeable;
                r.Detail = probeFailure == null
                    ? "api unset: " + ex.Message
                    : "both transports were refused, so the router is refusing to CLEAR the field: " + ex.Message;
                TryRestore(path, id, field, original);
                return r;
            }

            TryRestore(path, id, field, original);

            if (probeFailure != null)
            {
                r.Outcome = Outcome.Refused;
                r.Detail = _probeName + " unset threw where the API's went through: " + probeFailure;
                return r;
            }

            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                r.Outcome = Outcome.Ok;
                r.Detail = "unset " + field + " → '" + actual + "'";
            }
            else
            {
                r.Outcome = Outcome.Different;
                r.Detail = "unset " + field + ": api left '" + expected + "', " + _probeName + " left '" + actual + "'";
            }
            return r;
        }

        // ── move ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The ordered tables, where a row's POSITION is part of its meaning.
        /// </summary>
        /// <remarks>
        /// Measured on two rows this probe makes and deletes, rather than on the fixture row: <c>move</c>
        /// has no absolute form, so a differential needs the same starting order twice, and the cheapest way
        /// to have the same starting order twice is to build it twice. Nothing the router came with is
        /// touched, which matters more here than elsewhere — a moved firewall rule changes what the router
        /// does even though every field on it still reads correct.
        /// </remarks>
        internal void RunMoves(TransportAuditFixtures fixtures)
        {
            foreach (var recipe in TransportAuditFixtures.Recipes)
            {
                if (!OrderedPaths.Contains(recipe.Path)) continue;
                if (fixtures.CreatedIdOn(recipe.Path) == null) continue;
                _results.Add(RunMove(recipe));
            }
        }

        /// <summary>
        /// Tables RouterOS evaluates in order. Listed rather than detected: every table has a row ORDER, and
        /// nothing in a print says whether the router reads it top-down or treats it as a set — so a
        /// detected list would silently measure the wrong tables.
        /// </summary>
        private static readonly HashSet<string> OrderedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "/ip/firewall/filter", "/ip/firewall/nat", "/ip/firewall/mangle", "/ip/firewall/raw",
                "/interface/bridge/filter", "/interface/bridge/nat",
                "/ip/hotspot/walled-garden", "/ip/hotspot/walled-garden/ip", "/ip/proxy/access",
                // "/routing/rule" — see RoutingRuleMoveWedgesTheRouter below.
                "/routing/filter/rule",
                "/caps-man/provisioning", "/caps-man/access-list",
                "/interface/wifi/provisioning", "/interface/wifi/access-list",
            };

        /// <summary>
        /// Why <c>/routing/rule</c> is not in <see cref="OrderedPaths"/>.
        /// </summary>
        /// <remarks>
        /// Moving a routing rule over a CLI transport, in this probe, stops RouterOS 7.24's routing process
        /// answering ANYTHING — <c>/routing/*</c> and <c>/ip/route</c> time out on every transport, the
        /// fixtures cannot be torn down, and the router needs a reboot. Reproduced three times, each stopping
        /// at exactly this line and taking the rest of the run with it.
        /// <para>It is not the command: the same <c>numbers=</c>/<c>destination=</c> move typed at the same
        /// router by hand goes through and leaves it healthy, and the move over the API and over WinBox
        /// native is clean. What the probe adds is doing it immediately after building the two rows, which
        /// is as far as the diagnosis got — the next step costs a reboot per attempt.</para>
        /// <para>So this is an excuse for the INSTRUMENT, not a statement about the transport: an audit that
        /// takes the lab down cannot be run, and everything after the wedge is unmeasured anyway. The other
        /// fourteen ordered tables still cover the verb.</para>
        /// </remarks>
        private const string RoutingRuleMoveWedgesTheRouter = "/routing/rule";

        private Result RunMove(TransportAuditFixtures.Recipe recipe)
        {
            var r = new Result { Path = recipe.Path, Field = "(move)" };

            string problem;
            string probeOrder = MeasureMove(recipe, true, out problem);
            if (problem != null)
            {
                r.Outcome = probeOrder == null ? Outcome.NotProbeable : Outcome.Refused;
                r.Detail = problem;
                return r;
            }

            string apiOrder = MeasureMove(recipe, false, out problem);
            if (problem != null)
            {
                r.Outcome = Outcome.NotProbeable;
                r.Detail = "api side: " + problem;
                return r;
            }

            if (string.Equals(probeOrder, apiOrder, StringComparison.Ordinal))
            {
                r.Outcome = Outcome.Ok;
                r.Detail = "move put the row where the API's move puts it (" + apiOrder + ")";
            }
            else
            {
                r.Outcome = Outcome.Different;
                r.Detail = "move: api gave '" + apiOrder + "', " + _probeName + " gave '" + probeOrder + "'";
            }
            return r;
        }

        /// <summary>
        /// Builds two fresh rows, moves the second in front of the first over one transport, and returns the
        /// resulting order as "12" or "21". <paramref name="problem"/> is null on success; the rows are
        /// always removed.
        /// </summary>
        private string MeasureMove(TransportAuditFixtures.Recipe recipe, bool viaProbe, out string problem)
        {
            string path = recipe.Path;
            string firstId = null, secondId = null;
            problem = null;
            try
            {
                firstId = _api.CreateCommandAndParameters(path + "/add", Rename(recipe.NameValues, "m1"))
                              .ExecuteScalar();
                secondId = _api.CreateCommandAndParameters(path + "/add", Rename(recipe.NameValues, "m2"))
                               .ExecuteScalar();

                // Both adds land at the end, so the pair starts as [… first, second]. Moving `second` to
                // destination `first` must reverse exactly those two.
                string before = OrderOf(path, firstId, secondId);
                if (before != "12")
                {
                    problem = "the two probe rows did not land in the order they were added (" + before + ")";
                    return null;
                }

                var conn = viaProbe ? _probe : _api;
                conn.CreateCommandAndParameters(path + "/move", "numbers", secondId, "destination", firstId)
                    .ExecuteNonQuery();

                return OrderOf(path, firstId, secondId);
            }
            catch (Exception ex)
            {
                problem = (viaProbe ? _probeName : "api") + " move: " + ex.GetType().Name + ": " + ex.Message;
                // A throw AFTER both rows existed is the transport refusing the move; a throw before that is
                // the router refusing the ROW, and there is nothing to hold native against.
                return (firstId != null && secondId != null) ? "" : null;
            }
            finally
            {
                Cleanup(path, secondId, false);
                Cleanup(path, firstId, false);
            }
        }

        /// <summary>The two rows' relative position, as "12" or "21".</summary>
        private string OrderOf(string path, string firstId, string secondId)
        {
            var ids = _api.CreateCommand(path + "/print").ExecuteList().Select(x => x.GetId()).ToList();
            int a = ids.IndexOf(firstId), b = ids.IndexOf(secondId);
            if (a < 0 || b < 0) return "a probe row is not in the table";
            return a < b ? "12" : "21";
        }

        private ITikReSentence ReadRow(string path, string id)
        {
            try
            {
                return _api.CreateCommand(path + "/print").ExecuteList().FirstOrDefault(x => x.GetId() == id);
            }
            catch (Exception) { return null; }
        }

        private static void Verb(ITikConnection conn, string path, string id, string verb)
            => conn.CreateCommandAndParameters(path + "/" + verb, ".id", id).ExecuteNonQuery();

        private static void TryVerb(ITikConnection conn, string path, string id, string verb)
        {
            try { Verb(conn, path, id, verb); } catch (Exception) { }
        }

        // unset names its target in the pseudo-parameter `value-name`, the binary API's own spelling.
        private static void Unset(ITikConnection conn, string path, string id, string field)
            => conn.CreateCommandAndParameters(path + "/unset", ".id", id, "value-name", field).ExecuteNonQuery();

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
