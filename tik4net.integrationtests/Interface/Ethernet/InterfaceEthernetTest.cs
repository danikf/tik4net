using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Ethernet;
using System.Configuration;

namespace tik4net.integrationtests
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



        /// <summary>
        /// G3.3: <c>auto-negotiation</c> must be the SETTING on every transport.
        /// </summary>
        /// <remarks>
        /// The WinBox ethernet window carries two fields the .jg labels 'Auto Negotiation': the writable
        /// setting (name:'autoneg', b3f3) and the link's read-only negotiation status on the Status tab
        /// (u44d — incomplete/done/failed/not available). The status took the label, so on a CHR's virtual
        /// NIC WinboxNative reported <c>not-available</c> — which parses as <c>false</c> — where the API
        /// says <c>true</c>. Not a transport disagreement: the same question answered from the wrong field.
        /// Compared against the binary API rather than against a hard-coded expectation, because the
        /// SETTING is whatever this router happens to have.
        /// </remarks>
        [TestMethod]
        public void EthernetAutoNegotiationIsTheSettingNotTheLinkState()
        {
            var viaTransport = Connection.LoadAll<InterfaceEthernet>().ToList();
            Assert.IsTrue(viaTransport.Count > 0, "the router reported no ethernet interfaces");

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadAll<InterfaceEthernet>().ToList();

                foreach (var api in viaApi)
                {
                    var mine = viaTransport.FirstOrDefault(e => e.Name == api.Name);
                    if (mine == null) continue;   // a row-count difference is not what this test measures
                    Assert.AreEqual(api.AutoNegotiation, mine.AutoNegotiation,
                        "auto-negotiation on " + api.Name + ": the transport under test read the link's "
                        + "negotiation STATUS where the API reports the setting");
                }
            }
        }


        /// <summary>
        /// The other half of G3.3: the name must resolve to the WRITABLE field, not just decode from the
        /// right one. Toggled and verified through the binary API, because a write that lands on the
        /// read-only status field is one the router accepts, answers, and ignores (see G4).
        /// </summary>
        /// <remarks>
        /// The status field the alias moves aside is <c>ro:1</c> in the .jg; the setting (b3f3) is not.
        /// The lab CHR has one ethernet interface and every transport talks over it, so the toggle was
        /// measured by hand first: <c>auto-negotiation=no</c> keeps the link up on a Hyper-V synthetic NIC
        /// (running stays true, the router just starts reporting a fixed speed). The original value is put
        /// back in a finally, over the API, so a failure here cannot leave the lab on the wrong setting.
        /// </remarks>
        [TestMethod]
        public void EthernetAutoNegotiationResolvesForAWrite()
        {
            var eth = Connection.LoadAll<InterfaceEthernet>().FirstOrDefault(e => e.Name == TestConstants.Interface);
            if (eth == null) Assert.Inconclusive("the router has no " + TestConstants.Interface);

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var apiBefore = apiConnection.LoadAll<InterfaceEthernet>().Single(e => e.Name == TestConstants.Interface);
                bool original = apiBefore.AutoNegotiation ?? true;
                bool flipped = !original;

                try
                {
                    var cmd = Connection.CreateCommandAndParameters("/interface/ethernet/set",
                        TikSpecialProperties.Id, eth.Id,
                        "auto-negotiation", flipped ? "yes" : "no");
                    cmd.ExecuteNonQuery();

                    var apiAfter = apiConnection.LoadAll<InterfaceEthernet>().Single(e => e.Name == TestConstants.Interface);
                    Assert.AreEqual(flipped, apiAfter.AutoNegotiation,
                        "the transport under test wrote auto-negotiation and the router did not change it — "
                        + "the name resolved to a field that is not the setting");
                }
                finally
                {
                    var restore = apiConnection.CreateCommandAndParameters("/interface/ethernet/set",
                        TikSpecialProperties.Id, eth.Id,
                        "auto-negotiation", original ? "yes" : "no");
                    restore.ExecuteNonQuery();
                }
            }
        }

        [TestMethod]
        public void EthernetMonitorForEth1WillNotFail()
        {
            string INTERFACE_NAME = TestConstants.Interface;
            var result = EthernetMonitor.GetSnapshot(Connection, INTERFACE_NAME);

            Assert.IsNotNull(result);
            Assert.AreEqual(result.Name, INTERFACE_NAME);
        }
    }
}
