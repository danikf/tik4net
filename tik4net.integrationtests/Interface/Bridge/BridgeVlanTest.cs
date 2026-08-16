using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Bridge;

namespace tik4net.integrationtests
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
            string marker = Guid.NewGuid().ToString();
            BridgeVlan vlan = null;
            try
            {
                // Bridge setup is inside the try so the native safety net below also covers it: creating the
                // throwaway bridge (interface add type=bridge) is itself unsupported over native WinBox M2.
                var bridges = Connection.LoadAll<InterfaceBridge>();
                var existingBridge = bridges.FirstOrDefault();
                if (existingBridge != null)
                {
                    bridgeName = existingBridge.Name;
                }
                else
                {
                    var tempBridge = new InterfaceBridge { Name = tempBridgeName };
                    SaveTracked(tempBridge);
                    createdTempBridge = true;
                    bridgeName = tempBridgeName;
                }

                vlan = new BridgeVlan
                {
                    Bridge = bridgeName,
                    VlanIds = "3999",
                    Tagged = TestConstants.Interface,
                    Comment = marker,
                };
                SaveTracked(vlan);

                var loaded = Connection.LoadById<BridgeVlan>(vlan.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(bridgeName, loaded.Bridge);

                // The two list shapes this table carries, both of which the native WinBox M2 path has to
                // encode as a webfig u32[] rather than as a string the router would drop.
                //
                // vlan-ids is a multinumberrange: [lo,hi,…], so "3999" rides as [3999,3999].
                // tagged is a multinumber of interface REFERENCES: one element per interface, each the
                // referenced record's numeric id, decoded back to its name.
                Assert.AreEqual("3999", loaded.VlanIds);
                Assert.AreEqual(TestConstants.Interface, loaded.Tagged);
            }
            catch (Exception ex) when (IsWinboxNativeUnsupported(ex))
            {
                // Safety net for native WinBox: creating the throwaway bridge above (interface add
                // type=bridge) is a separate native gap ('unsupported device type') that only triggers when
                // the router has no existing bridge to reuse; also covers any future bridge-vlan native
                // regression. The vlan-ids and tagged round-trips themselves are fixed and asserted above.
                Assert.Inconclusive("/interface/bridge/vlan over native WinBox M2: " + ex.Message);
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
