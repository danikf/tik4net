// RouterPrerequisiteTest.cs — verifies that the test router has all necessary services
// enabled before the rest of the test suite runs. Uses the API (port 8728) — always available.
//
// Run this first to understand why other tests might fail.
// To fix: enable the missing services on the router manually or via the API.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;

namespace tik4net.tests
{
    [TestClass]
    public class RouterPrerequisiteTest
    {
        private static string Host => ConfigurationManager.AppSettings["host"];
        private static string User => ConfigurationManager.AppSettings["user"];
        private static string Pass => ConfigurationManager.AppSettings["pass"] ?? "";

        // ── Service checks ─────────────────────────────────────────────────────

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_ApiService_IsEnabled()
        {
            // If this test passes, the API connection works. Baseline.
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                Assert.IsTrue(conn.IsOpened, "API connection should be open");
                var ver = conn.CreateCommand("/system/resource/print").ExecuteScalar();
                Console.WriteLine("Router version: " + ver);
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_TelnetService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var services = conn.CreateCommand("/ip/service/print").ExecuteList();
                var telnet = services.FirstOrDefault(s =>
                    string.Equals(s.GetResponseFieldOrDefault("name", ""), "telnet",
                        StringComparison.OrdinalIgnoreCase));

                if (telnet == null)
                {
                    Assert.Inconclusive("Telnet service entry not found in /ip/service — cannot verify.");
                    return;
                }

                bool disabled = string.Equals(
                    telnet.GetResponseFieldOrDefault("disabled", "false"), "true",
                    StringComparison.OrdinalIgnoreCase);

                Console.WriteLine($"Telnet service: disabled={disabled}");

                if (disabled)
                    Assert.Fail(
                        "Telnet service is DISABLED on the router. " +
                        "Enable it with: /ip/service set telnet disabled=no");
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_MacTelnetServer_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                try
                {
                    var rows = conn.CreateCommand("/tool/mac-server/print").ExecuteList().ToList();
                    if (rows.Count == 0)
                    {
                        Assert.Inconclusive("No rows from /tool/mac-server/print — cannot verify.");
                        return;
                    }

                    var ifList = rows[0].GetResponseFieldOrDefault("allowed-interface-list", "");
                    Console.WriteLine($"MAC server: allowed-interface-list={ifList}");

                    if (string.Equals(ifList, "none", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrEmpty(ifList))
                    {
                        Assert.Fail(
                            "MAC Telnet server is DISABLED (allowed-interface-list=none). " +
                            "Enable it with: /tool/mac-server set allowed-interface-list=all");
                    }
                }
                catch (Exception ex)
                {
                    Assert.Inconclusive("Cannot read /tool/mac-server: " + ex.Message);
                }
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_MndpOrRouterMac_Configured()
        {
            var macOverride = ConfigurationManager.AppSettings["routerMac"];
            if (!string.IsNullOrEmpty(macOverride))
            {
                Console.WriteLine($"routerMac override in App.config: {macOverride} — MNDP will be skipped.");
                return;
            }

            // Try MNDP discovery to verify the router is discoverable
            Console.WriteLine("No routerMac in App.config — testing MNDP discovery (5 s)...");
            var found = tik4net.Mndp.MndpHelper.FindMacByHost(Host);
            Console.WriteLine(found != null
                ? $"MNDP found router MAC: {string.Join(":", found.Select(b => b.ToString("X2")))}"
                : "MNDP: router not found");

            if (found == null)
                Assert.Fail(
                    $"Router at {Host} not found via MNDP (UDP 5678). " +
                    "Either add <add key='routerMac' value='AA:BB:CC:DD:EE:FF'/> to App.config, " +
                    "or ensure MNDP (neighbour discovery) is enabled on the router.");
        }
    }
}
