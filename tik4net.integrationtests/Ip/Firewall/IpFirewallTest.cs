using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    [TestClass]
    public class IpFirewallTest : TestBase
    {
        [TestMethod]
        public void ConnectionList_DirectCall_WillNotFail()
        {
            EnsureRawDialectIsApiSentences("CallCommandSync with API sentence rows");

            string[] command = new string[]
            {
                "/ip/firewall/connection/print",
                "?src-address=192.168.3.103"
            };
            var result = Connection.CallCommandSync(command);
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
