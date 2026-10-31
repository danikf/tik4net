using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// A field an entity declares read-only stays one on this router: its menu's grammar offers it on neither
    /// <c>add</c> nor <c>set</c> (WinBox review F17). Category <c>AnyRouter</c> because a field may be writable on one
    /// version only, as <c>/system/ntp/client mode</c> is. A menu this router lacks has nothing to say.
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.AnyRouter)]
    public class EntityReadOnlyFieldTest : TestBase
    {
        [SafeInParallelLegs]
        [DataTestMethod]
        [DataRow("/interface/vlan", "mac-address")]
        [DataRow("/interface/bridge/port", "external-fdb")]
        [DataRow("/ip/dhcp-server/lease", "src-mac-address")]
        [DataRow("/ip/dhcp-server/option", "raw-value")]
        [DataRow("/ip/firewall/service-port", "name")]
        [DataRow("/routing/bgp/peer", "remote-id")]
        public void TheGrammarOffersTheFieldOnNoWriteVerb(string path, string field)
        {
            if (ResolveConnectionType() != TikConnectionType.Api)
                Assert.Inconclusive("the router's grammar does not depend on the transport: the API leg asks it");

            // RouterOS 6 has no /console/inspect: its grammar comes from Tab completion on a terminal.
            using (var grammar = GetMikrotikVersion().Major < 7 ? LabSetup(TikConnectionType.Telnet).Create(TikConnectionType.Telnet) : null)
            {
                TikMenuSchema schema;
                try { schema = (grammar ?? Connection).DescribeMenu(path); }
                catch (TikNoSuchCommandException) { Assert.Inconclusive($"{path}: this router has no such menu"); return; }
                if (schema.SetArguments == null && schema.AddArguments == null)
                    Assert.Inconclusive($"{path}: no grammar for this menu");

                var offered = new HashSet<string>(schema.SetArguments ?? new string[0], StringComparer.Ordinal);
                offered.UnionWith(schema.AddArguments ?? new string[0]);
                Assert.IsFalse(offered.Contains(field), $"{path} offers {field}: {string.Join(" ", offered.OrderBy(n => n))}");
            }
        }

        // The grammar's word put to the router itself: a write on a row of the test's own, on the binary API.
        [TestMethod]
        public void TheRouterRefusesTheWrite()
        {
            if (ResolveConnectionType() != TikConnectionType.Api)
                Assert.Inconclusive("the router's answer does not depend on the transport: the API leg asks it");

            string tag = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string vlan = Connection.CreateCommandAndParameters("/interface/vlan/add", "name", tag + "v", "interface", "ether2",
                "vlan-id", "3977", "disabled", "yes").ExecuteScalar();
            string bridge = Connection.CreateCommandAndParameters("/interface/bridge/add", "name", tag + "b").ExecuteScalar();
            string port = Connection.CreateCommandAndParameters("/interface/bridge/port/add", "bridge", tag + "b", "interface", tag + "v",
                "disabled", "yes").ExecuteScalar();
            string option = Connection.CreateCommandAndParameters("/ip/dhcp-server/option/add", "name", tag + "o", "code", "224",
                "value", "'x'").ExecuteScalar();
            string lease = Connection.CreateCommandAndParameters("/ip/dhcp-server/lease/add", "address", "10.250.77.7",
                "mac-address", "AA:BB:CC:DD:EE:FF", "disabled", "yes").ExecuteScalar();
            try
            {
                AssertRefused("/interface/vlan/set", vlan,"mac-address", "AA:BB:CC:DD:EE:FF");
                AssertRefused("/interface/bridge/port/set", port, "external-fdb", "yes");
                AssertRefused("/ip/dhcp-server/option/set", option, "raw-value", "0x78");
                AssertRefused("/ip/dhcp-server/lease/set", lease, "src-mac-address", "AA:BB:CC:DD:EE:FF");
            }
            finally
            {
                Connection.CreateCommandAndParameters("/ip/dhcp-server/lease/remove", ".id", lease).ExecuteNonQuery();
                Connection.CreateCommandAndParameters("/ip/dhcp-server/option/remove", ".id", option).ExecuteNonQuery();
                Connection.CreateCommandAndParameters("/interface/bridge/port/remove", ".id", port).ExecuteNonQuery();
                Connection.CreateCommandAndParameters("/interface/bridge/remove", ".id", bridge).ExecuteNonQuery();
                Connection.CreateCommandAndParameters("/interface/vlan/remove", ".id", vlan).ExecuteNonQuery();
            }
        }

        private void AssertRefused(string command, string id, string field, string value)
        {
            var ex = Assert.ThrowsException<TikCommandTrapException>(
                () => Connection.CreateCommandAndParameters(command, ".id", id, field, value).ExecuteNonQuery(),
                $"{command} {field} was accepted");
            StringAssert.Contains(ex.Message, "unknown parameter", $"{command} {field}");
        }
    }
}
