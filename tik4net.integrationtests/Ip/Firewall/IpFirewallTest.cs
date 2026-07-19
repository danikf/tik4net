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
            Connection.Save(firewallItem);

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
            Connection.Save(firewallItem);

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
            Connection.Save(firewallItem);
            var tmp = Connection.LoadById<FirewallFilter>(firewallItem.Id); //generate traffic
            System.Threading.Thread.Sleep(1000);

            try
            {
                tmp = Connection.LoadById<FirewallFilter>(firewallItem.Id);
                Assert.AreNotEqual(tmp.Bytes, 0);
                Assert.AreNotEqual(tmp.Packets, 0);
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
            Connection.Save(filter);
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
            Connection.Save(filter);
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
