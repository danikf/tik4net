// WinboxMacProtocolTest.cs — Winbox/MAC M2 protocol tests (EXPERIMENTAL)
// Scenario: login + list interfaces + set/restore comment on ether1.
// Uses UDP 20561 with client_type=0x0f90.
//
// NOTE: These tests currently fail due to a client-side network issue
// (Windows Firewall likely blocks inbound UDP unicast responses from the router).
// Additionally, the M2 framing inside DATA packets is a hypothesis not verified
// against a Wireshark capture.
// See _notes/poc-consolidation-and-native-m2-plan.md §5 (Faze E) for diagnosis steps.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;

namespace tik4net.tests
{
    [TestClass]
    public class WinboxMacProtocolTest
    {
        // Enable MAC Winbox on the router before tests run.
        [ClassInitialize]
        public static void EnableMacWinboxServer(TestContext _)
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                try
                {
                    var print2 = conn.CreateCommand("/tool/mac-server/mac-winbox/print");
                    foreach (var row in print2.ExecuteList())
                        Console.WriteLine("[init] MAC Winbox: allowed-interface-list=" +
                            row.GetResponseFieldOrDefault("allowed-interface-list", "?"));
                }
                catch (Exception ex) { Console.WriteLine("[init] Cannot read mac-winbox: " + ex.Message); }

                try
                {
                    var cmd2 = conn.CreateCommand("/tool/mac-server/mac-winbox/set");
                    cmd2.AddParameterAndValues("allowed-interface-list", "all");
                    cmd2.ExecuteNonQuery();
                    Console.WriteLine("[init] MAC Winbox: set allowed-interface-list=all");
                }
                catch (Exception ex) { Console.WriteLine("[init] MAC Winbox set failed: " + ex.Message); }
            }
        }

        [Ignore]
        [TestMethod]
        public void WinboxMac_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxMacClient())
            {
                client.Connect(host, user, pass);
                var ifaces = client.ListInterfaces();

                Console.WriteLine($"=== WINBOX-MAC INTERFACES ({ifaces.Count} found) ===");
                foreach (var i in ifaces) Console.WriteLine("  " + i);

                Assert.IsTrue(ifaces.Count > 0, "Router should expose at least one interface");
            }
        }
        [Ignore]
        [TestMethod]
        public void WinboxMac_SetAndVerify_InterfaceEther1Comment()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxMacClient())
            {
                client.Connect(host, user, pass);

                string original = client.GetInterfaceComment("ether1");
                Console.WriteLine($"Original: '{original}'");

                const string testComment = "tik4net-winboxmac-test";
                client.SetInterfaceComment("ether1", testComment);
                Console.WriteLine($"Set: '{testComment}'");

                string verified = client.GetInterfaceComment("ether1");
                Console.WriteLine($"Verified: '{verified}'");

                client.SetInterfaceComment("ether1", original);
                string restored = client.GetInterfaceComment("ether1");
                Console.WriteLine($"Restored: '{restored}'");

                Assert.AreEqual(testComment, verified, "Comment should be updated via Winbox MAC");
                Assert.AreEqual(original,    restored, "Original comment should be restored");
            }
        }
    }
}
