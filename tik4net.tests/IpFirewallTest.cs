using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.tests
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
    }
}
