using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Ethernet;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceEthernetTest : TestBase
    {
        [TestMethod]
        public void ListAllInterfaceEthernetWillNotFail()
        {
            var list = Connection.LoadAll<InterfaceEthernet>();
            Assert.IsNotNull(list);
        }

        //[TestMethod]
        //public void EthernetFlowControlSetWillNotFail()
        //{
        //    var list = Connection.LoadAll<InterfaceEthernet>();
        //    Assert.IsNotNull(list);
        //    Assert.IsTrue(list.Count() > 0);

        //    var eth = list.First();

        //    var originalFlowControlAuto = eth.FlowControlAuto;

        //    eth.FlowControlAuto = InterfaceEthernet.YesNoOptions.Yes;
        //    Connection.Save(eth);

        //    eth.FlowControlAuto = InterfaceEthernet.YesNoOptions.No;
        //    Connection.Save(eth);

        //    eth.FlowControlAuto = originalFlowControlAuto;
        //    Connection.Save(eth);
        //}


        [TestMethod]
        public void EthernetMonitorForEth1WillNotFail()
        {
            const string INTERFACE_NAME = "ether1";
            var result = EthernetMonitor.GetSnapshot(Connection, INTERFACE_NAME);

            Assert.IsNotNull(result);
            Assert.AreEqual(result.Name, INTERFACE_NAME);
        }
    }
}
