// WinboxCliMacProtocolTest.cs — WinBox CLI over MAC smoke tests via ITikConnection.
// Drives the production WinboxCliMacConnection (UDP 20561, client_type=0x0f90, mepty terminal)
// through the shared WinBox CLI engine + CLI Layer.
// Full CRUD parity is verified by the TestBase-based suite via winboxclimac.runsettings.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.WinboxCliMac;

namespace tik4net.tests
{
    [TestClass]
    public class WinboxCliMacProtocolTest
    {
        // Enable the MAC Winbox server on the router before tests run (separate from the mac-telnet server).
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
                    var cmd = conn.CreateCommand("/tool/mac-server/mac-winbox/set");
                    cmd.AddParameterAndValues("allowed-interface-list", "all");
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("[init] MAC Winbox: set allowed-interface-list=all");
                }
                catch (Exception ex) { Console.WriteLine("[init] MAC Winbox set failed: " + ex.Message); }
            }
        }

        private static WinboxCliMacConnection OpenWinboxCliMacConnection()
        {
            var host    = ConfigurationManager.AppSettings["host"];
            var user    = ConfigurationManager.AppSettings["user"];
            var pass    = ConfigurationManager.AppSettings["pass"] ?? "";
            var macAddr = ConfigurationManager.AppSettings["routerMac"]; // optional MNDP bypass

            var conn = new WinboxCliMacConnection { RouterMac = macAddr };
            conn.TransportDiagnostic = msg => Console.Write(msg);
            conn.Open(host, user, pass);
            return conn;
        }

        [TestMethod]
        public void WinboxCliMac_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            using (var conn = OpenWinboxCliMacConnection())
            {
                Console.WriteLine();
                var ifaces = conn.LoadAll<Interface>().ToList();

                Console.WriteLine($"=== WINBOX-MAC INTERFACES ({ifaces.Count} found) ===");
                foreach (var i in ifaces)
                    Console.WriteLine($"  [{i.DefaultName}] type={i.Type}");

                Assert.IsTrue(ifaces.Count > 0, "Router should expose at least one interface");
            }
        }

        [TestMethod]
        public void WinboxCliMac_SetAndVerify_InterfaceEther1Comment()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            string original;
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var e1 = api.LoadAll<Interface>().FirstOrDefault(i => i.DefaultName == "ether1");
                original = e1?.Comment ?? "";
            }
            Console.WriteLine($"Original: '{original}'");

            const string testComment = "tik4net-winboxclimac-test";

            using (var conn = OpenWinboxCliMacConnection())
            {
                Console.WriteLine();
                var e1 = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = testComment;
                conn.Save(e1);
                Console.WriteLine($"Set: '{testComment}'");

                var e1v = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                Console.WriteLine($"Verified: '{e1v.Comment}'");
                Assert.AreEqual(testComment, e1v.Comment, "Comment should be updated via WinBox CLI MAC");
            }

            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var e1 = api.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = original;
                api.Save(e1);
            }
            Console.WriteLine($"Restored: '{original}'");
        }
    }
}
