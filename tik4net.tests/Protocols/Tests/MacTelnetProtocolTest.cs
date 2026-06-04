// MacTelnetProtocolTest.cs — MAC-Telnet smoke tests via ITikConnection
// Verifies login + CRUD via ConnectionFactory.OpenConnection(TikConnectionType.MacTelnet, ...)

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net.MacTelnet;

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
                    var cmd = conn.CreateCommand("/tool/mac-server/set");
                    cmd.AddParameterAndValues("allowed-interface-list", "all");
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("[init] MAC server: set allowed-interface-list=all");
                }
                catch (Exception ex) { Console.WriteLine("[init] MAC server set failed: " + ex.Message); }
            }
        }

        private static MacTelnetConnection OpenMacTelnetConnection()
        {
            var host    = ConfigurationManager.AppSettings["host"];
            var user    = ConfigurationManager.AppSettings["user"];
            var pass    = ConfigurationManager.AppSettings["pass"] ?? "";
            var macAddr = ConfigurationManager.AppSettings["routerMac"]; // optional MNDP bypass

            var conn = new MacTelnetConnection { RouterMac = macAddr };
            conn.Open(host, user, pass);
            return conn;
        }

        [TestMethod]
        public void MacTelnet_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            using (var conn = OpenMacTelnetConnection())
            {
                var ifaces = conn.CreateCommand("/interface/print").ExecuteList();

                var ifaceList = ifaces.ToList();
                Console.WriteLine($"=== MACTELNET INTERFACES ({ifaceList.Count} found) ===");
                foreach (var i in ifaceList)
                    Console.WriteLine("  name=" + i.GetResponseFieldOrDefault("name", "?"));

                Assert.IsTrue(ifaceList.Count > 0, "Router should expose at least one interface");
            }
        }

        [TestMethod]
        public void MacTelnet_SetAndVerify_InterfaceEther1Comment()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            // Read original comment via API (reliable baseline)
            string original;
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var row = api.CreateCommand("/interface/print", api.CreateParameter("?name", "ether1", TikCommandParameterFormat.Filter))
                    .ExecuteList().FirstOrDefault();
                original = row?.GetResponseFieldOrDefault("comment", "") ?? "";
            }
            Console.WriteLine($"Original comment: '{original}'");

            const string testComment = "tik4net-mactelnet-test";

            using (var conn = OpenMacTelnetConnection())
            {
                // Set via MAC-Telnet
                var setCmd = conn.CreateCommand("/interface/set");
                setCmd.AddParameterAndValues(".id", "ether1", "comment", testComment);
                setCmd.ExecuteNonQuery();
                Console.WriteLine($"Set: '{testComment}'");

                // Verify via MAC-Telnet
                var row = conn.CreateCommand("/interface/print",
                    conn.CreateParameter("?name", "ether1", TikCommandParameterFormat.Filter))
                    .ExecuteList().FirstOrDefault();
                string verified = row?.GetResponseFieldOrDefault("comment", "") ?? "";
                Console.WriteLine($"Verified: '{verified}'");

                Assert.AreEqual(testComment, verified, "Comment should be updated via MAC-Telnet");
            }

            // Restore via API
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var restore = api.CreateCommand("/interface/set");
                restore.AddParameterAndValues(".id", "ether1", "comment", original);
                restore.ExecuteNonQuery();
            }
            Console.WriteLine($"Restored: '{original}'");
        }
    }
}
