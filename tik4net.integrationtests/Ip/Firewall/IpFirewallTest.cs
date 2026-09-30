using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpFirewallTest : TestBase
    {
        /// <summary>
        /// Every <c>action</c> the router accepts on <c>/ip/firewall/mangle</c> and
        /// <c>/ip/firewall/filter</c> must be readable back through the entity's enum.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An action value the enum does not know is not a missing property — <c>TikEnumMetadata.Parse</c>
        /// throws <c>FormatException</c>, and that fails the <b>whole</b> <c>LoadAll</c>. So one unmapped
        /// action makes the entire menu unreadable for anyone whose router uses it, which is how
        /// <c>mark-routing</c> (shipped with an empty wire value since v1.2.0) and <c>fasttrack-connection</c>
        /// — a rule in the DEFAULT RouterOS firewall — went unnoticed: the lab router did not happen to have
        /// one, so nothing read them.
        /// </para>
        /// <para>
        /// The rows are created over the command API rather than the mapper, because <c>mark-routing</c> and
        /// <c>route</c> need companion fields (<c>new-routing-mark</c>, <c>route-dst</c>) the entity does not
        /// map — and the read path is what is being pinned regardless. They are created <b>disabled</b>: the
        /// suite runs over the very path a firewall rule governs.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void EveryFirewallActionTheRouterAcceptsCanBeReadBack()
        {
            const string marker = "t4n-actionprobe";
            var created = new List<(string Path, string Id)>();

            // Residue from an earlier run that could not reach its own cleanup - a receive timeout kills the
            // connection the cleanup would have used - would otherwise be indistinguishable from this run's
            // rows. Swept first, and the assertions below match on .id rather than on the marker, so a leftover
            // cannot decide the result either way.
            SweepProbeRows("/ip/firewall/mangle", marker);
            SweepProbeRows("/ip/firewall/filter", marker);

            // An action this RouterOS does not have is not part of what "the router accepts": mangle's
            // action=drop is refused with a syntax error on 7.19.6 and accepted on 7.24.4 (measured on both,
            // raw over Telnet). Such an action is left out of the run rather than failing it.
            var expectedMangle = new List<FirewallMangle.ActionType>();
            var absent = new List<string>();

            try
            {
                if (AddRuleIfTheRouterHasTheAction("/ip/firewall/mangle", absent, created,
                        "chain", "prerouting", "action", "drop", "disabled", "yes", "comment", marker))
                    expectedMangle.Add(FirewallMangle.ActionType.Drop);
                if (AddRuleIfTheRouterHasTheAction("/ip/firewall/mangle", absent, created,
                        "chain", "prerouting", "action", "fasttrack-connection", "disabled", "yes", "comment", marker))
                    expectedMangle.Add(FirewallMangle.ActionType.FasttrackConnection);
                if (AddRuleIfTheRouterHasTheAction("/ip/firewall/mangle", absent, created,
                        "chain", "prerouting", "action", "mark-routing", "new-routing-mark", "main",
                        "disabled", "yes", "comment", marker))
                    expectedMangle.Add(FirewallMangle.ActionType.MarkRouting);
                if (AddRuleIfTheRouterHasTheAction("/ip/firewall/mangle", absent, created,
                        "chain", "prerouting", "action", "route", "route-dst", "192.0.2.1",
                        "disabled", "yes", "comment", marker))
                    expectedMangle.Add(FirewallMangle.ActionType.Route);
                bool filterFasttrack = AddRuleIfTheRouterHasTheAction("/ip/firewall/filter", absent, created,
                    "chain", "forward", "action", "fasttrack-connection", "connection-state", "established,related",
                    "disabled", "yes", "comment", marker);
                if (absent.Count > 0)
                    Console.WriteLine("actions this RouterOS does not have, left out: " + string.Join(", ", absent));

                // Filtered to this test's own rows rather than LoadAll. Reading the whole menu was the
                // stronger check, and it cost the test its reliability: on a router carrying a few thousand
                // rules RouterOS intermittently stops part-way through a print (MangleLoadStallProbe), so a
                // full read here goes red for a reason that has nothing to do with the enum. The stronger
                // check lives router-free in FirewallActionVocabularyTests, which compares the enums against
                // the vocabulary the router itself reports.
                var mine = new HashSet<string>(created.Select(c => c.Id));
                var mangle = Connection.LoadList<FirewallMangle>(
                    Connection.CreateParameter("comment", marker))
                    .Where(m => mine.Contains(m.Id)).ToList();
                var filter = Connection.LoadList<FirewallFilter>(
                    Connection.CreateParameter("comment", marker))
                    .Where(f => mine.Contains(f.Id)).ToList();

                CollectionAssert.AreEquivalent(expectedMangle, mangle.Select(m => m.Action.Value).ToList(),
                    "the probe rules must read back as the actions they were created with");

                Assert.IsTrue(!filterFasttrack || filter.Any(f => f.Action == FirewallFilter.ActionType.FasttrackConnection),
                    "the fasttrack-connection filter rule must read back as FasttrackConnection");
            }
            finally
            {
                foreach (var (path, id) in created)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    try
                    {
                        Connection.CreateCommandAndParameters(path + "/remove", TikSpecialProperties.Id, id)
                                  .ExecuteNonQuery();
                    }
                    catch (Exception ex) { Console.WriteLine($"cleanup of {path} {id} failed: {ex.Message}"); }
                }
            }
        }

        /// <summary>
        /// Adds one probe rule, and reports whether this RouterOS has the action at all: a refusal naming the
        /// action (a syntax error at the action's own column, or a trap) is the router saying it does not know
        /// it, which is what this test is asking. Anything else is a real failure and is rethrown.
        /// </summary>
        private bool AddRuleIfTheRouterHasTheAction(string path, List<string> absent,
            List<(string Path, string Id)> created, params string[] nameValuePairs)
        {
            try
            {
                created.Add((path, AddRule(path + "/add", nameValuePairs)));
                return true;
            }
            catch (Exception ex) when (ex is TikNoSuchCommandException || ex is TikCommandTrapException)
            {
                int i = Array.IndexOf(nameValuePairs, "action");
                absent.Add(path + " action=" + (i >= 0 && i + 1 < nameValuePairs.Length ? nameValuePairs[i + 1] : "?")
                           + " (" + ex.Message + ")");
                return false;
            }
        }

        /// <summary>
        /// <c>passthrough</c> is printed on every mangle action it applies to, on every transport — not only on
        /// <c>mark-packet</c>.
        /// </summary>
        /// <remarks>
        /// WinBox declares the field once, in the mark-packet pane of the mangle window's action deck, and lists it in
        /// the other panes (mark-connection, mark-routing, …) as <c>{name:'Passthrough',type:'alias'}</c>. WinBox native
        /// drops a field that belongs to another kind's pane, so a catalog that ignores the aliases drops passthrough
        /// from every mark-connection rule. Created over the command API (the entity does not map
        /// <c>new-connection-mark</c>), disabled.
        /// </remarks>
        [TestMethod]
        public void ManglePassthrough_IsReadOnEveryActionItAppliesTo()
        {
            const string marker = "t4n-passthroughprobe";
            SweepProbeRows("/ip/firewall/mangle", marker);
            var created = new List<string>();
            try
            {
                created.Add(AddRule("/ip/firewall/mangle/add", "chain", "prerouting", "action", "mark-packet",
                    "new-packet-mark", marker, "passthrough", "no", "disabled", "yes", "comment", marker));
                created.Add(AddRule("/ip/firewall/mangle/add", "chain", "prerouting", "action", "mark-connection",
                    "new-connection-mark", marker, "passthrough", "no", "disabled", "yes", "comment", marker));

                foreach (string id in created)
                {
                    var rule = Connection.LoadById<FirewallMangle>(id);
                    Assert.IsNotNull(rule, id);
                    Assert.AreEqual(false, rule.Passthrough.Value, $"{rule.Action}: passthrough ({rule.Passthrough.State})");
                }
            }
            finally
            {
                foreach (string id in created)
                    Connection.CreateCommandAndParameters("/ip/firewall/mangle/remove", TikSpecialProperties.Id, id).ExecuteNonQuery();
            }
        }

        /// <summary>Removes any rule left behind by an earlier run of this test.</summary>
        private void SweepProbeRows(string path, string marker)
        {
            try
            {
                var stale = Connection.CreateCommandAndParameters(path + "/print", "comment", marker)
                                      .ExecuteList()
                                      .Select(row => row.GetId())
                                      .ToList();
                foreach (string id in stale)
                    Connection.CreateCommandAndParameters(path + "/remove", TikSpecialProperties.Id, id)
                              .ExecuteNonQuery();
            }
            catch (Exception ex) { Console.WriteLine($"sweep of {path} failed: {ex.Message}"); }
        }

        /// <summary>Adds one rule over the command API and returns its <c>.id</c>.</summary>
        private string AddRule(string addPath, params string[] nameValuePairs)
            => Connection.CreateCommandAndParameters(addPath, nameValuePairs).ExecuteScalar();

        [TestMethod]
        public void ConnectionList_DirectCall_WillNotFail()
        {
            EnsureRawDialectIsApiSentences("CallCommandSync with API sentence rows");

            string[] command = new string[]
            {
                "/ip/firewall/connection/print",
                "?src-address=192.168.3.103"
            };
            var result = RawConnection.CallCommandSync(command);
        }

        [TestMethod]
        public void ConnectionList_CommandCall_WillNotFail()
        {
            var command = Connection.CreateCommandAndParameters("/ip/firewall/connection/print",
                "src-address", "192.168.3.103");
            var result = command.ExecuteList();
        }

        [TestMethod]
        public void ConnectionList_MapperCall_WillNotFail()
        {
            var result = Connection.LoadList<FirewallConnection>(
                Connection.CreateParameter("src-address", "192.168.3.103"));
        }

        [TestMethod]
        public void FirewalTcpFilter_Issue51_WillNotFail()
        {
            var firewallItem = new FirewallFilter()
            {
                Action = FirewallFilter.ActionType.Drop,
                Chain = "forward",
                Comment = "test-tcp",
                Disabled = true,    // a traffic-path row: created disabled, the suite runs through this chain
                DstAddress = "8.8.8.8",
                DstPort = "53",
                Protocol = "tcp",
                SrcAddress = "1.1.1.1",
                SrcPort = "22",
            };
            SaveTracked(firewallItem);

            Connection.Delete(firewallItem);
        }

        [TestMethod]
        public void FirewalTcpFilterAccept_Issue51_WillNotFail()
        {
            var firewallItem = new FirewallFilter()
            {
                Action = FirewallFilter.ActionType.Accept, //default value
                Chain = "forward",
                Comment = "test-tcp",
                Disabled = true,    // a traffic-path row: created disabled, the suite runs through this chain
                DstAddress = "8.8.8.8",
                DstPort = "53",
                Protocol = "tcp",
                SrcAddress = "1.1.1.1",
                SrcPort = "22",
            };
            SaveTracked(firewallItem);

            Connection.Delete(firewallItem);
        }

        [TestMethod]
        public void FirewalTcpFilterAccept_BytesAndPackets_NotZero()
        {
            // Previously gated on CLI: CLI now uses two-query (detail + stats) merge, so counters
            // (bytes/packets) are available on all transports including Telnet.

            // pre-cleanup: remove leftovers that would absorb traffic before the test rule
            foreach (var leftover in Connection.LoadAll<FirewallFilter>()
                .Where(f => f.Comment == "test-tcp" && f.Chain == "input"))
                Connection.Delete(leftover);

            var firewallItem = new FirewallFilter()
            {
                Action = FirewallFilter.ActionType.Accept, //default value
                Chain = "input",
                Comment = "test-tcp",
            };
            SaveTracked(firewallItem);

            try
            {
                // P2.21: this used to be "read once, sleep 1 s, read again and assert", and that is a race
                // the test lost whenever it ran in the full suite. RouterOS does not start matching a rule
                // against traffic the instant `add` returns (the record's ready flag flips a moment later),
                // and the traffic in the window is only whatever this test itself sends. Standalone that is
                // plenty — the connection is opened fresh, so a TCP handshake plus login crosses the input
                // chain — but in the full suite the connection is already open and two small reads were all
                // the rule ever saw, both of them before it went live. Measured on winboxnative: 0/0, 0/0,
                // then 1 packet / 140 bytes.
                //
                // So drive it rather than sleep on it: each pass sends a read (that read *is* the traffic)
                // and re-checks. Fast on a healthy router, and still a loud failure if the counters truly
                // never move — which is the thing this test exists to catch.
                FirewallFilter tmp;
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (true)
                {
                    Connection.LoadAll<FirewallFilter>();   // traffic through the input chain
                    tmp = Connection.LoadById<FirewallFilter>(firewallItem.Id);
                    if (tmp.Bytes != 0 && tmp.Packets != 0) break;
                    if (DateTime.UtcNow >= deadline)
                    {
                        DumpCounterDiagnostics(firewallItem.Id);
                        break;
                    }
                    System.Threading.Thread.Sleep(250);
                }

                Assert.AreNotEqual(0, tmp.Bytes, "rule never counted any bytes");
                Assert.AreNotEqual(0, tmp.Packets, "rule never counted any packets");
            }
            finally
            {
                Connection.Delete(firewallItem);
            }
        }

        [TestMethod]
        public void FirewallServicePort_LoadAll_WillNotFail()
        {
            Connection.LoadList<FirewalServicePort>();
        }

        [TestMethod]
        public void FirewallFilter_ConnectionState_FlagsRead_WillNotFail()
        {
            // Verifies that a comma-separated connection-state value (e.g. "established,related")
            // is correctly parsed into a [Flags] enum (issue #94 / #79).
            var filter = new FirewallFilter()
            {
                Action = FirewallFilter.ActionType.Accept,
                Chain = "forward",
                Comment = "test-flags-read",
                Disabled = true,    // a traffic-path row: created disabled, the suite runs through this chain
                ConnectionState = FirewallFilter.ConnectionStateType.Established | FirewallFilter.ConnectionStateType.Related,
            };
            SaveTracked(filter);
            try
            {
                var loaded = Connection.LoadById<FirewallFilter>(filter.Id);
                Assert.IsTrue(loaded.ConnectionState.GetValueOrDefault().HasFlag(FirewallFilter.ConnectionStateType.Established));
                Assert.IsTrue(loaded.ConnectionState.GetValueOrDefault().HasFlag(FirewallFilter.ConnectionStateType.Related));
                Assert.IsFalse(loaded.ConnectionState.GetValueOrDefault().HasFlag(FirewallFilter.ConnectionStateType.Invalid));
            }
            finally
            {
                Connection.Delete(filter);
            }
        }

        [TestMethod]
        public void FirewallFilter_ConnectionState_FlagsWrite_WillNotFail()
        {
            // Verifies that a [Flags] enum value is serialized back to comma-separated string (issue #94 / #79).
            var filter = new FirewallFilter()
            {
                Action = FirewallFilter.ActionType.Drop,
                Chain = "forward",
                Comment = "test-flags-write",
                Disabled = true,    // a traffic-path row: created disabled, the suite runs through this chain
                ConnectionState = FirewallFilter.ConnectionStateType.New | FirewallFilter.ConnectionStateType.Invalid,
            };
            SaveTracked(filter);
            try
            {
                var loaded = Connection.LoadById<FirewallFilter>(filter.Id);
                Assert.IsTrue(loaded.ConnectionState.GetValueOrDefault().HasFlag(FirewallFilter.ConnectionStateType.New));
                Assert.IsTrue(loaded.ConnectionState.GetValueOrDefault().HasFlag(FirewallFilter.ConnectionStateType.Invalid));
            }
            finally
            {
                Connection.Delete(filter);
            }
        }

        [TestMethod]
        public void FirewallFilter_ConnectionState_NotNegation_RoundTrips()
        {
            // RouterOS represents a negated connection-state match as a single '!' on the whole value
            // ("!established,related"). WinBox native carries the negation as a separate 'not' flag key, the
            // CLI/API as the literal '!' string — every transport must surface the same '!'-prefixed value.
            // Driven at the raw-command level because '!established' is not a [Flags] enum value (so the
            // FirewallFilter entity / LoadAll cannot be used here).
            const string comment = "test-cs-not";
            RemoveFirewallFilterByComment(comment);

            string id = Connection.CreateCommandAndParameters("/ip/firewall/filter/add",
                "chain", "forward", "action", "accept",
                "connection-state", "!established,related", "comment", comment).ExecuteScalar();
            Assert.IsFalse(string.IsNullOrWhiteSpace(id), "add did not return an id");
            try
            {
                var row = Connection.CreateCommandAndParameters("/ip/firewall/filter/print",
                    TikCommandParameterFormat.Filter, "comment", comment).ExecuteSingleRow();
                string cs = row.GetResponseField("connection-state");

                StringAssert.StartsWith(cs, "!",
                    $"negation '!' prefix lost on transport '{ResolveConnectionType()}' (got '{cs}')");
                StringAssert.Contains(cs, "established");
                StringAssert.Contains(cs, "related");
            }
            finally
            {
                RemoveFirewallFilterByComment(comment);
            }
        }

        /// <summary>
        /// The entity end of the same thing: a negated matcher assigned with <c>TikValue&lt;T&gt;.Not</c> reaches the
        /// router with its <c>!</c> and reads back as <c>IsNegated</c> — an address, an interface, a port list and a
        /// <c>[Flags]</c> set, each of which a transport encodes differently (WinBox native: the <c>not</c> flag).
        /// </summary>
        [TestMethod]
        public void FirewallFilter_NegatedMatchers_RoundTripThroughTheEntity()
        {
            const string comment = "t4n-negated-matchers";
            RemoveFirewallFilterByComment(comment);
            var filter = new FirewallFilter
            {
                Chain = "forward",
                Action = FirewallFilter.ActionType.Accept,
                Disabled = true,
                Comment = comment,
                Protocol = "tcp",
                SrcAddress = TikValue<string>.Not("10.0.0.0/8"),
                InInterface = TikValue<string>.Not("ether1"),
                DstPort = TikValue<string>.Not("22,8291"),
                ConnectionState = TikValue<FirewallFilter.ConnectionStateType?>.Not(
                    FirewallFilter.ConnectionStateType.Established | FirewallFilter.ConnectionStateType.Related),
            };
            try
            {
                SaveTracked(filter);
                var loaded = Connection.LoadById<FirewallFilter>(filter.Id);

                Assert.AreEqual(filter.SrcAddress, loaded.SrcAddress, $"src-address on {ResolveConnectionType()}");
                Assert.AreEqual(filter.InInterface, loaded.InInterface, $"in-interface on {ResolveConnectionType()}");
                Assert.AreEqual(filter.DstPort, loaded.DstPort, $"dst-port on {ResolveConnectionType()}");
                Assert.AreEqual(filter.ConnectionState, loaded.ConnectionState, $"connection-state on {ResolveConnectionType()}");

                loaded.SrcAddress = loaded.SrcAddress.WithoutNegation();
                Connection.Save(loaded);
                var again = Connection.LoadById<FirewallFilter>(filter.Id);
                Assert.IsTrue(again.SrcAddress == "10.0.0.0/8", $"dropping the '!' on {ResolveConnectionType()}: {again.SrcAddress}");
                Assert.IsTrue(again.DstPort.IsNegated, "an untouched negation stays");
            }
            finally
            {
                RemoveFirewallFilterByComment(comment);
            }
        }

        /// <summary>
        /// connection-rate and connection-bytes are low-high ranges, spelled out as the API prints them, and
        /// connection-limit is limit,netmask: the CLI abbreviates the rate (<c>0-100k</c>) and WinBox native keeps
        /// each of the three in two keys.
        /// </summary>
        [TestMethod]
        public void FirewallFilter_ConnectionRanges_RoundTrip()
        {
            const string comment = "t4n-connection-ranges";
            RemoveFirewallFilterByComment(comment);
            var filter = new FirewallFilter
            {
                Chain = "forward",
                Action = FirewallFilter.ActionType.Accept,
                Disabled = true,
                Comment = comment,
                Protocol = "tcp",
                ConnectionRate = TikValue<string>.Not("1500-2000000"),
                ConnectionBytes = "2000000-0",
                ConnectionLimit = TikValue<string>.Not("10,24"),
            };
            try
            {
                SaveTracked(filter);
                var loaded = Connection.LoadById<FirewallFilter>(filter.Id);

                Assert.AreEqual(filter.ConnectionRate, loaded.ConnectionRate, $"connection-rate on {ResolveConnectionType()}");
                Assert.AreEqual(filter.ConnectionBytes, loaded.ConnectionBytes, $"connection-bytes on {ResolveConnectionType()}");
                Assert.AreEqual(filter.ConnectionLimit, loaded.ConnectionLimit, $"connection-limit on {ResolveConnectionType()}");
            }
            finally
            {
                RemoveFirewallFilterByComment(comment);
            }
        }

        // Prints the raw row behind the counters, plus (on WinBox native) how many handlers the .jg catalog
        // supplied. A catalog that did not load leaves the connection on the seed table, where the getall
        // stats bit is never set and the counter fields simply do not arrive — indistinguishable from zeros
        // unless the raw row is inspected.
        private void DumpCounterDiagnostics(string id)
        {
            var native = Connection as tik4net.WinboxNative.WinboxNativeConnection;
            if (native != null)
                Console.WriteLine($"  catalog handlers: {native.CatalogHandlerCount}");
            try
            {
                var row = Connection.CreateCommandAndParameters("/ip/firewall/filter/print",
                    TikCommandParameterFormat.Filter, ".id", id).ExecuteSingleRow();
                Console.WriteLine("  raw row: " + string.Join(", ",
                    row.Words.Select(w => w.Key + "=" + w.Value)));
            }
            catch (Exception ex) { Console.WriteLine("  raw row unavailable: " + ex.Message); }
        }

        // Removes every /ip/firewall/filter rule carrying the given comment via raw commands (no entity
        // mapping — a '!'-negated connection-state cannot be parsed into the FirewallFilter [Flags] enum).
        private void RemoveFirewallFilterByComment(string comment)
        {
            var rows = Connection.CreateCommandAndParameters("/ip/firewall/filter/print",
                TikCommandParameterFormat.Filter, "comment", comment).ExecuteList();
            foreach (var r in rows)
                Connection.CreateCommandAndParameters("/ip/firewall/filter/remove",
                    ".id", r.GetResponseField(".id")).ExecuteNonQuery();
        }
    }
}
