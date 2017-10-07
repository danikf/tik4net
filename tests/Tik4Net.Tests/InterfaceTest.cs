using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using System.Collections.Generic;
using System.Threading;
using tik4net.Objects.Interface.Wireless;
using System.Linq;

namespace tik4net.tests
{
    [TestClass]
    public class InterfaceTest: TestBase
    {
        [TestMethod]
        public void ListAllInterfaceWillNotFail()
        {
            var list = Connection.LoadAll<Interface>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllWirelessInterfaceWillNotFail()
        {
            var list = Connection.LoadAll<InterfaceWireless>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllWirelessRegistrationsWillNotFail()
        {
            var list = Connection.LoadAll<WirelessRegistrationTable>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllEthernetWillNotFail()
        {
            var list = Connection.LoadAll<InterfaceEthernet>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void FilteredUntypedListOfInterfacesWillNotFail()
        {
            var cmd = Connection.CreateCommandAndParameters(@"/interface/print
                            ?type=ether
                            ?type=wlan
                            ?#|");
            var list = cmd.ExecuteList();

            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void FilteredTypedListOfInterfacesWillNotFail()
        {
            var cmd = Connection.CreateCommandAndParameters(@"/interface/print
                            ?type=ether
                            ?type=wlan
                            ?#|");
            var list = cmd.LoadList<Interface>();

            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void FilteredTypedAsyncListOfInterfacesWillNotFail()
        {
            var cmd = Connection.CreateCommandAndParameters(@"/interface/print
                            ?type=ether
                            ?type=wlan
                            ?#|");
            var list = new List<Interface>();
            cmd.LoadAsync<Interface>(i=>list.Add(i));
            Thread.Sleep(1000);
            cmd.CancelAndJoin();

            Assert.IsNotNull(list);
            Assert.IsTrue(list.Count > 0);
        }

        [TestMethod]
        public void UpdateCommentOnEth1WillNotFail()
        {
            var list = Connection.LoadAll<Interface>();
            Assert.IsNotNull(list);

            var eth = list.Where(iface => iface.DefaultName == "ether1").Single();
            eth.Comment = "My comment";
            Connection.Save(eth);
        }

        [TestMethod]
        public void UpdateCommentOnEth1_2_WillNotFail()
        {
            var cmd = Connection.CreateCommand("/interface/set");
            cmd.AddParameter(TikSpecialProperties.Id, "ether1");
            cmd.AddParameter("comment", "My next comment");
            cmd.ExecuteNonQuery();
        }

        [TestMethod]
        public void InterfaceTraficAsync_WillNotFail()
        {
            var cmd = Connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", "ether1");
            List<ITikReSentence> responses = new List<ITikReSentence>();
            cmd.ExecuteAsync(re => responses.Add(re));
            Thread.Sleep(5 * 1000);

            Assert.IsTrue(responses.Count > 0);
            cmd.CancelAndJoin(2 * 1000);
        }
    }
}
