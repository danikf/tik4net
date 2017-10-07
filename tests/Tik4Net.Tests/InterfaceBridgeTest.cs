using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tik4Net.Objects.Interface;
using Tik4Net.Objects;
using Tik4Net.Objects.Interface.Bridge;

namespace Tik4Net.Tests
{
    [TestClass]
    public class InterfaceBridgeTest: TestBase
    {
        [TestMethod]
        public void ListAllBridgesWillNotFail()
        {
            var list = Connection.LoadAll<InterfaceBridge>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void ListAllBridgeFiltersWillNotFail()
        {
            var list = Connection.LoadAll<BridgeFilter>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddBridgeFilterWillNotFail()
        {
            string name = Guid.NewGuid().ToString();
            var filter = new BridgeFilter()
            {
                Chain = BridgeFirewallChainType.Forward,
                Comment = name,
                Action = BridgeFilter.ActionType.Accept,
            };
            Connection.Save(filter);

            var loadedFilter = Connection.LoadById<BridgeFilter>(filter.Id);

            Assert.IsNotNull(loadedFilter);
            Assert.AreEqual(filter.Chain, loadedFilter.Chain);
            Assert.AreEqual(filter.Action, loadedFilter.Action);

            Connection.Delete<BridgeFilter>(loadedFilter);
        }


        [TestMethod]
        public void AddBridgeNatWillNotFail()
        {
            string name = Guid.NewGuid().ToString();
            var nat = new BridgeNat()
            {
                Chain = BridgeFirewallChainType.Forward,
                Comment = name,
                Action = BridgeNat.ActionType.Accept,
            };
            Connection.Save(nat);

            var loadedNat = Connection.LoadById<BridgeNat>(nat.Id);

            Assert.IsNotNull(loadedNat);
            Assert.AreEqual(nat.Chain, loadedNat.Chain);
            Assert.AreEqual(nat.Action, loadedNat.Action);

            Connection.Delete<BridgeNat>(loadedNat);
        }
    }
}
