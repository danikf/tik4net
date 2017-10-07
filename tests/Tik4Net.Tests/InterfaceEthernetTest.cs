using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tik4Net.Objects;
using Tik4Net.Objects.Interface;

namespace Tik4Net.Tests
{
    [TestClass]
    public class InterfaceEthernetTest: TestBase
    {
        [TestMethod]
        public void ListAllInterfaceEthernetWillNotFail()
        {
            var list = Connection.LoadAll<InterfaceEthernet>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void EthernetFlowControlSetWillNotFail()
        {
            var list = Connection.LoadAll<InterfaceEthernet>();
            Assert.IsNotNull(list);
            Assert.IsTrue(list.Count() > 0);

            var eth = list.First();

            var originalFlowControlAuto = eth.FlowControlAuto;

            eth.FlowControlAuto = InterfaceEthernet.YesNoOptions.Yes;
            Connection.Save(eth);

            eth.FlowControlAuto = InterfaceEthernet.YesNoOptions.No;
            Connection.Save(eth);

            eth.FlowControlAuto = originalFlowControlAuto;
            Connection.Save(eth);
        }

    }
}
