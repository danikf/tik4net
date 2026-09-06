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

            try
            {
                created.Add(("/ip/firewall/mangle", AddRule("/ip/firewall/mangle/add",
                    "chain", "prerouting", "action", "drop", "disabled", "yes", "comment", marker)));
                created.Add(("/ip/firewall/mangle", AddRule("/ip/firewall/mangle/add",
                    "chain", "prerouting", "action", "fasttrack-connection", "disabled", "yes", "comment", marker)));
                created.Add(("/ip/firewall/mangle", AddRule("/ip/firewall/mangle/add",
                    "chain", "prerouting", "action", "mark-routing", "new-routing-mark", "main",
                    "disabled", "yes", "comment", marker)));
                created.Add(("/ip/firewall/mangle", AddRule("/ip/firewall/mangle/add",
                    "chain", "prerouting", "action", "route", "route-dst", "192.0.2.1",
                    "disabled", "yes", "comment", marker)));
                created.Add(("/ip/firewall/filter", AddRule("/ip/firewall/filter/add",
                    "chain", "forward", "action", "fasttrack-connection", "connection-state", "established,related",
                    "disabled", "yes", "comment", marker)));

                // The assertion is that these do not throw. A FormatException here names the action the
                // enum is missing, which is the whole diagnosis.
                var mangle = Connection.LoadAll<FirewallMangle>().ToList();
                var filter = Connection.LoadAll<FirewallFilter>().ToList();

                CollectionAssert.AreEquivalent(
                    new[] { FirewallMangle.ActionType.Drop, FirewallMangle.ActionType.FasttrackConnection,
                            FirewallMangle.ActionType.MarkRouting, FirewallMangle.ActionType.Route },
                    mangle.Where(m => m.Comment == marker).Select(m => m.Action).ToList(),
                    "the four probe rules must read back as the actions they were created with");

                Assert.IsTrue(filter.Any(f => f.Comment == marker
                                              && f.Action == FirewallFilter.ActionType.FasttrackConnection),
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
                ConnectionState = FirewallFilter.ConnectionStateType.Established | FirewallFilter.ConnectionStateType.Related,
            };
            SaveTracked(filter);
            try
            {
                var loaded = Connection.LoadById<FirewallFilter>(filter.Id);
                Assert.IsTrue(loaded.ConnectionState.HasFlag(FirewallFilter.ConnectionStateType.Established));
                Assert.IsTrue(loaded.ConnectionState.HasFlag(FirewallFilter.ConnectionStateType.Related));
                Assert.IsFalse(loaded.ConnectionState.HasFlag(FirewallFilter.ConnectionStateType.Invalid));
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
                ConnectionState = FirewallFilter.ConnectionStateType.New | FirewallFilter.ConnectionStateType.Invalid,
            };
            SaveTracked(filter);
            try
            {
                var loaded = Connection.LoadById<FirewallFilter>(filter.Id);
                Assert.IsTrue(loaded.ConnectionState.HasFlag(FirewallFilter.ConnectionStateType.New));
                Assert.IsTrue(loaded.ConnectionState.HasFlag(FirewallFilter.ConnectionStateType.Invalid));
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
