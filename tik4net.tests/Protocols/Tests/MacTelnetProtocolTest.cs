// MacTelnetProtocolTest.cs — MAC-Telnet protocol tests
// Scenario: login + list interfaces + set/restore comment on ether1.
// Uses UDP 20561 with client_type=0x0015.
//
// Fix applied (Chapter D): router responds to client port 20561, not to ephemeral port.
// MacLayerTransport.BaseConnect now binds to 0.0.0.0:20561 with SO_REUSEADDR.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;

namespace tik4net.tests
{
    [TestClass]
    public class MacTelnetProtocolTest
    {
        // Enable MAC server on the router before MAC tests run.
        [ClassInitialize]
        public static void EnableMacServer(TestContext _)
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                try
                {
                    var print1 = conn.CreateCommand("/tool/mac-server/print");
                    foreach (var row in print1.ExecuteList())
                        Console.WriteLine("[init] MAC Telnet: allowed-interface-list=" +
                            row.GetResponseFieldOrDefault("allowed-interface-list", "?"));
                }
                catch (Exception ex) { Console.WriteLine("[init] Cannot read mac-server: " + ex.Message); }

                try
                {
                    var cmd = conn.CreateCommand("/tool/mac-server/set");
                    cmd.AddParameterAndValues("allowed-interface-list", "all");
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("[init] MAC Telnet: set allowed-interface-list=all");
                }
                catch (Exception ex) { Console.WriteLine("[init] MAC Telnet set failed: " + ex.Message); }
            }
        }

        [TestMethod]
        public void MacTelnet_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new MacTelnetClient())
            {
                client.Connect(host, user, pass);
                var ifaces = client.ListInterfaces();

                Console.WriteLine($"=== MACTELNET INTERFACES ({ifaces.Count} found) ===");
                foreach (var i in ifaces) Console.WriteLine("  " + i);

                Assert.IsTrue(ifaces.Count > 0, "Router should expose at least one interface");
            }
        }

        [TestMethod]
        public void MacTelnet_SetAndVerify_InterfaceEther1Comment()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new MacTelnetClient())
            {
                client.Connect(host, user, pass);

                string original = client.GetInterfaceComment("ether1");
                Console.WriteLine($"Original: '{original}'");

                const string testComment = "tik4net-mactelnet-test";
                client.SetInterfaceComment("ether1", testComment);
                Console.WriteLine($"Set: '{testComment}'");

                string verified = client.GetInterfaceComment("ether1");
                Console.WriteLine($"Verified: '{verified}'");

                client.SetInterfaceComment("ether1", original);
                string restored = client.GetInterfaceComment("ether1");
                Console.WriteLine($"Restored: '{restored}'");

                Assert.AreEqual(testComment, verified, "Comment should be updated via MACTelnet");
                Assert.AreEqual(original,    restored, "Original comment should be restored");
            }
        }
    }
}
