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
            EnsureCommandAvailable("/interface/wireless");
            var list = Connection.LoadAll<InterfaceWireless>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllWirelessRegistrationsWillNotFail()
        {
            EnsureCommandAvailable("/interface/wireless");
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
        public void ReadEthernetInterfaceRXWillNotFail()
        {
            var ethIface = Connection.LoadByName<Interface>( "ether1");
            var rx = ethIface.RxByte;
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

        [TestMethod]
        public void WirelessInterfaceResaveWillNotFail()
        {
            EnsureCommandAvailable("/interface/wireless");
            var iface = Connection.LoadAll<InterfaceWireless>().FirstOrDefault(wlan => wlan.Name == "wlan1");
            if (iface == null)
                Assert.Inconclusive("Interface wlan1 not found on this router.");
            iface.Comment = "test";
            Connection.Save(iface);
        }

        #region LoadListenAsync

        [TestMethod]
        public void LoadListenAsync_DetectsInterfaceChange()
        {
            const string IFACE = "ether1";
            const string TEST_COMMENT = "tik4net-listen-test";

            List<Interface> changes = new List<Interface>();
            Exception listenException = null;

            var listenCmd = Connection.LoadListenAsync<Interface>(
                iface => { lock (changes) changes.Add(iface); },
                onDeletedCallback: null,
                onExceptionCallback: ex => { listenException = ex; });

            try
            {
                Thread.Sleep(500); // ensure listen is running

                // trigger a change — set comment on ether1
                var setCmd = Connection.CreateCommand("/interface/set");
                setCmd.AddParameter(TikSpecialProperties.Id, IFACE);
                setCmd.AddParameter("comment", TEST_COMMENT);
                setCmd.ExecuteNonQuery();

                Thread.Sleep(1500); // wait for !re callback

                Assert.IsNull(listenException, "Unexpected listen error: " + listenException?.Message);
                lock (changes)
                    Assert.IsTrue(changes.Any(i => i.Name == IFACE),
                        "Expected at least one change notification for " + IFACE);
            }
            finally
            {
                listenCmd.CancelAndJoin();

                var cleanCmd = Connection.CreateCommand("/interface/set");
                cleanCmd.AddParameter(TikSpecialProperties.Id, IFACE);
                cleanCmd.AddParameter("comment", "");
                cleanCmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void LoadListenAsync_DetectsDeletedItem()
        {
            const string TEST_IP = "192.0.2.1/32"; // TEST-NET, safe dummy address
            const string TEST_IFACE = "ether1";

            // create a dummy IP address to delete during the test
            var addCmd = Connection.CreateCommandAndParameters("/ip/address/add",
                TikCommandParameterFormat.NameValue,
                "address", TEST_IP,
                "interface", TEST_IFACE);
            string newId = addCmd.ExecuteScalar();

            List<string> deletedIds = new List<string>();
            Exception listenException = null;

            var listenCmd = Connection.LoadListenAsync<Objects.Ip.IpAddress>(
                _ => { },
                onDeletedCallback: id => { lock (deletedIds) deletedIds.Add(id); },
                onExceptionCallback: ex => { listenException = ex; });

            try
            {
                Thread.Sleep(500);

                var removeCmd = Connection.CreateCommandAndParameters("/ip/address/remove",
                    TikCommandParameterFormat.NameValue,
                    TikSpecialProperties.Id, newId);
                removeCmd.ExecuteNonQuery();

                Thread.Sleep(1500);

                Assert.IsNull(listenException, "Unexpected listen error: " + listenException?.Message);
                lock (deletedIds)
                    Assert.IsTrue(deletedIds.Contains(newId),
                        "Expected deleted-item notification for id " + newId);
            }
            finally
            {
                listenCmd.CancelAndJoin();

                // cleanup in case remove failed
                try
                {
                    Connection.CreateCommandAndParameters("/ip/address/remove",
                        TikCommandParameterFormat.NameValue,
                        TikSpecialProperties.Id, newId).ExecuteNonQuery();
                }
                catch { }
            }
        }

        #endregion

        [TestMethod]
        public void ParallelSniffCommandsAreCorrectlyCancelled()
        {
            Connection.DebugEnabled = true;

            var cmdWlan = Connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", "wlan1");
            List<ITikReSentence> responsesWlan = new List<ITikReSentence>();
            cmdWlan.ExecuteAsync(re => responsesWlan.Add(re));

            var cmdEth = Connection.CreateCommandAndParameters("/interface/monitor-traffic", "interface", "ether1");
            List<ITikReSentence> responsesEth = new List<ITikReSentence>();
            cmdEth.ExecuteAsync(re => responsesEth.Add(re));

            Thread.Sleep(1000);
            cmdWlan.CancelAndJoin();
            var cnt = responsesEth.Count;

            Thread.Sleep(1000);
            Assert.IsTrue(cmdEth.IsRunning);
            cmdEth.CancelAndJoin();

            Assert.IsTrue(responsesEth.Count > cnt);
        }
    }
}
