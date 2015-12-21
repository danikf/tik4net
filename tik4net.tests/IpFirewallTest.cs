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
    }
}
