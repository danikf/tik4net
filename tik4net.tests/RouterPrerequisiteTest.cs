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

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ITikReSentence FindService(ITikConnection conn, string name)
            => conn.CreateCommand("/ip/service/print").ExecuteList()
                   .FirstOrDefault(s => string.Equals(
                       s.GetResponseFieldOrDefault("name", ""), name,
                       StringComparison.OrdinalIgnoreCase));

        private static void AssertServiceEnabled(ITikReSentence svc, string name)
        {
            bool disabled = string.Equals(
                svc.GetResponseFieldOrDefault("disabled", "false"), "true",
                StringComparison.OrdinalIgnoreCase);

            Console.WriteLine($"{name}: disabled={disabled}, port={svc.GetResponseFieldOrDefault("port", "?")}");

            if (disabled)
                Assert.Fail($"Service '{name}' is DISABLED. " +
                            $"Enable with: /ip/service set {name} disabled=no");
        }

        private static void AssertServiceHasCertificate(ITikReSentence svc, string name)
        {
            string cert = svc.GetResponseFieldOrDefault("certificate", "");
            Console.WriteLine($"{name} certificate: '{cert}'");

            if (string.IsNullOrEmpty(cert) || string.Equals(cert, "none", StringComparison.OrdinalIgnoreCase))
                Assert.Fail(
                    $"Service '{name}' has no certificate. " +
                    $"Create one: /certificate add name={name}-cert common-name={name}; " +
                    $"/certificate sign {name}-cert; /ip/service set {name} certificate={name}-cert");
        }

        // ── API ────────────────────────────────────────────────────────────────

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_ApiService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var version = conn.CreateCommand("/system/resource/print")
                                  .ExecuteSingleRow().GetResponseField("version");
                Console.WriteLine("Router version: " + version);

                var svc = FindService(conn, "api");
                if (svc == null) { Assert.Inconclusive("Service 'api' not found in /ip/service."); return; }
                AssertServiceEnabled(svc, "api");
            }
        }

        // ── API-SSL ────────────────────────────────────────────────────────────

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_ApiSslService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "api-ssl");
                if (svc == null) { Assert.Inconclusive("Service 'api-ssl' not found in /ip/service."); return; }
                AssertServiceEnabled(svc, "api-ssl");
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_ApiSslCertificate_IsConfigured()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "api-ssl");
                if (svc == null) { Assert.Inconclusive("Service 'api-ssl' not found in /ip/service."); return; }
                AssertServiceHasCertificate(svc, "api-ssl");
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_ApiSslConnection_CanOpen()
        {
            try
            {
                using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.ApiSsl, Host, User, Pass))
                {
                    var version = conn.CreateCommand("/system/resource/print")
                                      .ExecuteSingleRow().GetResponseField("version");
                    Console.WriteLine("API-SSL connected. Version: " + version);
                }
            }
            catch (Exception ex)
            {
                Assert.Fail("API-SSL connection failed: " + ex.Message);
            }
        }

        // ── Telnet ─────────────────────────────────────────────────────────────

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_TelnetService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "telnet");
                if (svc == null) { Assert.Inconclusive("Service 'telnet' not found in /ip/service."); return; }
                AssertServiceEnabled(svc, "telnet");
            }
        }

        // ── Winbox ─────────────────────────────────────────────────────────────

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_WinboxService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "winbox");
                if (svc == null) { Assert.Inconclusive("Service 'winbox' not found in /ip/service."); return; }
                AssertServiceEnabled(svc, "winbox");
            }
        }

        // ── REST ───────────────────────────────────────────────────────────────

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_RestService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "www");
                if (svc == null) { Assert.Inconclusive("Service 'www' not found in /ip/service."); return; }
                AssertServiceEnabled(svc, "www");
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_RestSslService_IsEnabled()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "www-ssl");
                if (svc == null) { Assert.Inconclusive("Service 'www-ssl' not found in /ip/service."); return; }
                AssertServiceEnabled(svc, "www-ssl");
            }
        }

        [TestMethod]
        [TestCategory("Prerequisites")]
        public void Router_RestSslCertificate_IsConfigured()
        {
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, Host, User, Pass))
            {
                var svc = FindService(conn, "www-ssl");
                if (svc == null) { Assert.Inconclusive("Service 'www-ssl' not found in /ip/service."); return; }
                AssertServiceHasCertificate(svc, "www-ssl");
            }
        }

        // ── MAC-Telnet ─────────────────────────────────────────────────────────

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
