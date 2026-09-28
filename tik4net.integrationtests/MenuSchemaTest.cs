using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Ip;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <c>DescribeMenu</c> on every transport: <c>/console/inspect</c> on RouterOS 7, Tab completion on a RouterOS 6
    /// CLI, the <c>.jg</c> catalog on WinBox native — and RouterOS 6 over the API, which has no way to answer.
    /// </summary>
    [TestClass]
    public class MenuSchemaTest : TestBase
    {
        private bool IsNative()
        {
            var type = ResolveConnectionType();
            return type == TikConnectionType.WinboxNative || type == TikConnectionType.WinboxNativeMac;
        }

        private bool IsCli()
        {
            var type = ResolveConnectionType();
            return type == TikConnectionType.Telnet || type == TikConnectionType.Ssh || type == TikConnectionType.MacTelnet
                || type == TikConnectionType.WinboxCli || type == TikConnectionType.WinboxCliMac;
        }

        private TikMenuSchema Describe(string path)
        {
            EnsureCapability(TikConnectionCapability.MenuSchema);
            try
            {
                return Connection.DescribeMenu(path);
            }
            catch (TikNoSuchCommandException) when (GetMikrotikVersion().Major < 7 && !IsCli() && !IsNative())
            {
                Assert.Inconclusive("RouterOS 6 over the API has no /console/inspect and no terminal to Tab on.");
                throw;
            }
        }

        [TestMethod]
        public void TheSourceIsWhatThisTransportAndVersionCanAsk()
        {
            var schema = Describe("/ip/route");

            var expected = IsNative() ? TikMenuSchemaSource.WinboxCatalog
                : GetMikrotikVersion().Major < 7 ? TikMenuSchemaSource.CliCompletion
                : TikMenuSchemaSource.ConsoleInspect;
            Assert.AreEqual(expected, schema.Source, schema.ToString());
            CollectionAssert.IsSubsetOf(new[] { "dst-address", "gateway" }, schema.AddArguments.ToArray(), schema.ToString());
        }

        [TestMethod]
        public void TheRenamedRoutingTableIsTheNameThisVersionTakes()
        {
            var schema = Connection.Supports(TikConnectionCapability.MenuSchema) ? Describe("/ip/route") : null;
            if (schema == null || schema.Source == TikMenuSchemaSource.WinboxCatalog)
                Assert.Inconclusive("WinBox native writes by key; its names are tik4net's, not the router's.");

            string taken = GetMikrotikVersion().Major < 7 ? "routing-mark" : "routing-table";
            string refused = taken == "routing-mark" ? "routing-table" : "routing-mark";
            CollectionAssert.Contains(schema.SetArguments.ToArray(), taken);
            CollectionAssert.DoesNotContain(schema.SetArguments.ToArray(), refused);
        }

        [TestMethod]
        public void AnEnumArgumentListsItsWords()
        {
            var words = Describe("/ip/firewall/filter").ValuesOf("action", "add");

            CollectionAssert.IsSubsetOf(new[] { "accept", "drop", "jump" }, words.ToArray(), string.Join(",", words));
        }

        [TestMethod]
        public void ASingletonHasSetAndNoAdd()
        {
            // The entity overload: on WinBox native the entity's labels name the fields as the API does — the path
            // alone gives the window's own ('identity', 'version').
            EnsureCapability(TikConnectionCapability.MenuSchema);
            if (GetMikrotikVersion().Major < 7 && !IsCli() && !IsNative())
                Assert.Inconclusive("RouterOS 6 over the API has no /console/inspect.");
            var schema = Connection.DescribeMenu<tik4net.Objects.System.SystemIdentity>();

            Assert.IsNull(schema.AddArguments, schema.ToString());
            CollectionAssert.Contains(schema.SetArguments.ToArray(), "name",
                "set: " + string.Join(",", schema.SetArguments) + " / readable: " + string.Join(",", schema.ReadableFields));
        }

        [TestMethod]
        public void TheEntityOverloadDescribesItsMenu()
        {
            EnsureCapability(TikConnectionCapability.MenuSchema);
            if (GetMikrotikVersion().Major < 7 && !IsCli() && !IsNative())
                Assert.Inconclusive("RouterOS 6 over the API has no /console/inspect.");

            Assert.AreEqual("/ip/route", Connection.DescribeMenu<IpRoute>().Path);
        }

        [TestMethod]
        public void AMenuTheRouterDoesNotHave_IsNoSuchCommand()
        {
            EnsureCapability(TikConnectionCapability.MenuSchema);
            try
            {
                Connection.DescribeMenu("/ip/t4n-no-such-menu");
                Assert.Fail("described a menu that does not exist");
            }
            catch (TikNoSuchCommandException)
            {
                // WinBox native raises its subtype TikPathNotMappedException: no window, the router was never asked.
            }
        }
    }
}
