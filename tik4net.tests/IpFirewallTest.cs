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

    }
}
