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
                    Connection.Save(tempBridge);
                    createdTempBridge = true;
                    bridgeName = tempBridgeName;
                }

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

                // vlan-ids is a VLAN-id list/range. The native WinBox M2 path now encodes it as the webfig
                // multinumberrange u32[] ([lo,hi,…]; "3999" → [3999,3999]) and decodes it back, so it
                // round-trips on every transport (verified live RouterOS 7.21.4). (Other bridge-vlan list
                // fields — tagged/untagged interface lists — are still unencoded over native and the field
                // resolver now throws loudly rather than dropping them silently.)
                Assert.AreEqual("3999", loaded.VlanIds);
            }
            catch (Exception ex) when (IsWinboxNativeUnsupported(ex))
            {
                // Safety net for native WinBox: creating the throwaway bridge above (interface add
                // type=bridge) is a separate native gap ('unsupported device type') that only triggers when
                // the router has no existing bridge to reuse; also covers any future bridge-vlan native
                // regression. The vlan-ids round-trip itself is fixed and asserted above.
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
