using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Interface;
using tik4net.Objects;
using tik4net.Objects.Interface.Bridge;

namespace tik4net.integrationtests
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
        public void AnMstpBridgeWithLocalProxyArpCanBeWrittenAndReadBack()
        {
            // Both values are RouterOS 7 vocabulary the enums did not know, and an unknown enum value fails
            // the WHOLE LoadAll<InterfaceBridge>() (InterfaceBridgeVocabularyTests). This is the live half:
            // each transport translates enum values its own way (the WinBox native codec maps them from the
            // .jg catalog), so a unit test cannot vouch for all of them. The list read is the symptom a user
            // hit; LoadById is the round trip.
            var bridge = new InterfaceBridge
            {
                Name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12),
                ProtocolMode = InterfaceBridge.ProtocolModeModes.Mstp,
                VlanFiltering = true,     // RouterOS: "mstp requires vlan-filtering"
                Arp = InterfaceBridge.ArpMode.LocalProxyArp,
            };
            SaveTracked(bridge);

            var listed = Connection.LoadAll<InterfaceBridge>().Single(b => b.Name == bridge.Name);
            Assert.AreEqual(InterfaceBridge.ProtocolModeModes.Mstp, listed.ProtocolMode);
            Assert.AreEqual(InterfaceBridge.ArpMode.LocalProxyArp, listed.Arp);

            var loaded = Connection.LoadById<InterfaceBridge>(bridge.Id);
            Assert.AreEqual(InterfaceBridge.ProtocolModeModes.Mstp, loaded.ProtocolMode);
            Assert.AreEqual(InterfaceBridge.ArpMode.LocalProxyArp, loaded.Arp);
            Assert.AreEqual(true, loaded.VlanFiltering);
        }

        [TestMethod]
        public void TheRouterOs7BridgeFieldsCanBeWrittenAndReadBack()
        {
            // The fields the 4.0 upgrade added, across the three groups the router only prints while their
            // feature is on (VLAN filtering, IGMP snooping, DHCP snooping), each set to a non-default value so
            // a field that is silently dropped on the way out reads back as the router's default instead.
            var bridge = new InterfaceBridge
            {
                Name = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Comment = "t4n bridge fields",
                VlanFiltering = true,
                Pvid = 7,
                FrameTypes = InterfaceBridge.FrameTypesMode.AdmitOnlyVlanTagged,
                IngressFiltering = false,
                IgmpSnooping = true,
                IgmpVersion = 3,
                MulticastRouter = InterfaceBridge.MulticastRouterMode.Permanent,
                LastMemberQueryCount = 3,
                QueryInterval = "1m",
                DhcpSnooping = true,
                PortCostMode = InterfaceBridge.PortCostModeType.Short,
                TransmitHoldCount = 4,
                FastForward = false,
            };
            SaveTracked(bridge);

            var loaded = Connection.LoadById<InterfaceBridge>(bridge.Id);
            Assert.AreEqual(bridge.Comment, loaded.Comment);
            Assert.AreEqual(true, loaded.VlanFiltering);
            Assert.AreEqual(7, loaded.Pvid);
            Assert.AreEqual(InterfaceBridge.FrameTypesMode.AdmitOnlyVlanTagged, loaded.FrameTypes);
            Assert.AreEqual(false, loaded.IngressFiltering);
            Assert.AreEqual(true, loaded.IgmpSnooping);
            Assert.AreEqual(3, loaded.IgmpVersion);
            Assert.AreEqual(InterfaceBridge.MulticastRouterMode.Permanent, loaded.MulticastRouter);
            Assert.AreEqual(3, loaded.LastMemberQueryCount);
            Assert.AreEqual(TimeSpan.FromMinutes(1), loaded.QueryInterval?.Value);
            Assert.AreEqual(true, loaded.DhcpSnooping);
            Assert.AreEqual(InterfaceBridge.PortCostModeType.Short, loaded.PortCostMode);
            Assert.AreEqual(4, loaded.TransmitHoldCount);
            Assert.AreEqual(false, loaded.FastForward);
            Assert.AreEqual(false, loaded.Disabled);

            loaded.Pvid = 8;
            loaded.IgmpSnooping = false;
            Connection.Save(loaded);
            var updated = Connection.LoadById<InterfaceBridge>(bridge.Id);
            Assert.AreEqual(8, updated.Pvid);
            Assert.AreEqual(false, updated.IgmpSnooping);
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
            SaveTracked(filter);

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
            SaveTracked(nat);

            var loadedNat = Connection.LoadById<BridgeNat>(nat.Id);

            Assert.IsNotNull(loadedNat);
            Assert.AreEqual(nat.Chain, loadedNat.Chain);
            Assert.AreEqual(nat.Action, loadedNat.Action);

            Connection.Delete<BridgeNat>(loadedNat);
        }
    }
}
