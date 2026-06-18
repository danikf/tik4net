using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Bridge;

namespace tik4net.tests
{
    [TestClass]
    public class BridgeVlanTest : TestBase
    {
        // -----------------------------------------------------------------------
        // List
        // -----------------------------------------------------------------------

        [TestMethod]
        public void ListBridgeVlansWillNotFail()
        {
            EnsureCommandAvailable("/interface/bridge/vlan");
            var list = Connection.LoadAll<BridgeVlan>();
            Assert.IsNotNull(list);
        }

        // -----------------------------------------------------------------------
        // Add / reload / delete  (requires at least one bridge on the router)
        // -----------------------------------------------------------------------

        [TestMethod]
        public void AddBridgeVlanWillNotFail()
        {
            EnsureCommandAvailable("/interface/bridge/vlan");

            // We need a bridge to attach the VLAN entry to.
            // Try to find an existing bridge first; create a throwaway one if none exists.
            string bridgeName = null;
            bool createdTempBridge = false;
            string tempBridgeName = "tik4net-vlan-test-br";

            var bridges = Connection.LoadAll<InterfaceBridge>();
            var existingBridge = bridges.FirstOrDefault();
            if (existingBridge != null)
            {
                bridgeName = existingBridge.Name;
            }
            else
            {
                // Create a throwaway bridge
                var tempBridge = new InterfaceBridge { Name = tempBridgeName };
                Connection.Save(tempBridge);
                createdTempBridge = true;
                bridgeName = tempBridgeName;
            }

            string marker = Guid.NewGuid().ToString();
            BridgeVlan vlan = null;
            try
            {
                vlan = new BridgeVlan
                {
                    Bridge = bridgeName,
                    VlanIds = "3999",
                    Comment = marker,
                };
                Connection.Save(vlan);

                var loaded = Connection.LoadById<BridgeVlan>(vlan.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(bridgeName, loaded.Bridge);
                Assert.AreEqual("3999", loaded.VlanIds);
            }
            finally
            {
                // Always clean up the VLAN entry
                if (vlan != null && vlan.Id != null)
                {
                    try { Connection.Delete(vlan); } catch { /* best effort */ }
                }
                // Remove throwaway bridge if we created one
                if (createdTempBridge)
                {
                    try
                    {
                        var tempBr = Connection.LoadAll<InterfaceBridge>()
                            .FirstOrDefault(b => b.Name == tempBridgeName);
                        if (tempBr != null)
                            Connection.Delete(tempBr);
                    }
                    catch { /* best effort */ }
                }
            }
        }
    }
}
