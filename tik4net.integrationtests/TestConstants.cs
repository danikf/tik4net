using System.Configuration;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Centralizes the router-topology assumptions the live-router suite depends on, so the suite can
    /// target a different router by editing App.config (keys below) instead of editing scattered string
    /// literals. Each value falls back to the historical default when the key is absent.
    /// Scope: the high-level <see cref="TestBase"/> suite. The low-level Protocols/Tests/* protocol tests
    /// keep their own literals because they assert specific paths/handlers, not generic topology.
    /// </summary>
    internal static class TestConstants
    {
        /// <summary>Primary wired interface used by most tests (App.config 'testInterface', default ether1).</summary>
        public static string Interface =>
            ConfigurationManager.AppSettings["testInterface"] ?? "ether1";

        /// <summary>Wireless interface used by the wireless tests (App.config 'testWirelessInterface', default wlan1).</summary>
        public static string WirelessInterface =>
            ConfigurationManager.AppSettings["testWirelessInterface"] ?? "wlan1";

        /// <summary>Disposable IP+mask added/removed by the CRUD tests (App.config 'testAddress', default 192.168.1.1/24).</summary>
        public static string Address =>
            ConfigurationManager.AppSettings["testAddress"] ?? "192.168.1.1/24";
    }
}
