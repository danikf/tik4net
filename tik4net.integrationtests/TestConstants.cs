using System;
using System.Configuration;
using System.Linq;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Centralizes the router-topology assumptions the live-router suite depends on, so the suite can
    /// target a different router by editing App.config (keys below) instead of editing scattered string
    /// literals. Each value falls back to the historical default when the key is absent.
    /// Scope: the high-level <see cref="TestBase"/> suite. The low-level Protocols/Tests/* protocol tests
    /// keep their own literals because they assert specific paths/handlers, not generic topology — with the
    /// exception of <see cref="SecondInterface"/>, which they do take from here: an interface name is
    /// topology whoever needs it, and the protocol fixtures were the largest group of tests silently
    /// depending on a port nobody had declared.
    /// </summary>
    internal static class TestConstants
    {
        /// <summary>Primary wired interface used by most tests (App.config 'testInterface', default ether1).</summary>
        public static string Interface =>
            ConfigurationManager.AppSettings["testInterface"] ?? "ether1";

        /// <summary>
        /// A wired interface that is NOT <see cref="Interface"/>, for the fixtures that must not be built on
        /// the link carrying the connection under test — a bridge port, a VLAN's parent, VRRP, a DHCP server.
        /// </summary>
        /// <remarks>
        /// Discovered off the router rather than declared: the first <c>/interface/ethernet</c> whose name is
        /// not <see cref="Interface"/>. A rebuilt lab does not always name its second port the same — RouterOS
        /// took the name <c>ether3</c> for a re-added NIC whose <c>default-name</c> was <c>ether2</c> — and the
        /// literal it replaces was undeclared topology: nothing said the suite needed a second port at all,
        /// so a router without one failed as a handful of unexplained refusals.
        /// <para>App.config 'testSecondInterface' overrides the discovery, for a lab where the choice matters
        /// (several spare ports, only one of them free). Discovery resolves once per run and falls back to the
        /// historical <c>ether2</c> if the router cannot be reached — a run against a dead router has larger
        /// problems than this value, and reporting them as a topology error would mislead.</para>
        /// </remarks>
        public static string SecondInterface =>
            _secondInterface ?? (_secondInterface = ResolveSecondInterface());

        private static string _secondInterface;

        private static string ResolveSecondInterface()
        {
            var configured = ConfigurationManager.AppSettings["testSecondInterface"];
            if (!string.IsNullOrEmpty(configured))
                return configured;

            try
            {
                using (var api = TestBase.LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api))
                {
                    var found = api.CreateCommand("/interface/ethernet/print").ExecuteList()
                        .Select(row => row.GetResponseFieldOrDefault("name", null))
                        .FirstOrDefault(name => !string.IsNullOrEmpty(name)
                                                && !StringComparer.OrdinalIgnoreCase.Equals(name, Interface));
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }
            catch (Exception)
            {
                // Fall through: see the remarks above.
            }

            return "ether2";
        }

        /// <summary>Wireless interface used by the wireless tests (App.config 'testWirelessInterface', default wlan1).</summary>
        public static string WirelessInterface =>
            ConfigurationManager.AppSettings["testWirelessInterface"] ?? "wlan1";

        /// <summary>Disposable IP+mask added/removed by the CRUD tests (App.config 'testAddress', default 192.168.1.1/24).</summary>
        public static string Address =>
            ConfigurationManager.AppSettings["testAddress"] ?? "192.168.1.1/24";
    }
}
