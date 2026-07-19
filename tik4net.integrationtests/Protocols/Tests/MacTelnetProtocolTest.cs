// MacTelnetProtocolTest.cs — MAC-Telnet smoke tests via ITikConnection
// Router prerequisites are checked by RouterPrerequisiteTest (uses API).
// Full CRUD parity is verified by the TestBase-based suite via mactelnet.runsettings.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net.MacTelnet;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class MacTelnetProtocolTest
    {
        private static MacTelnetConnection OpenMacTelnetConnection()
        {
            var host    = ConfigurationManager.AppSettings["host"];
            var user    = ConfigurationManager.AppSettings["user"];
            var pass    = ConfigurationManager.AppSettings["pass"] ?? "";
            var macAddr = ConfigurationManager.AppSettings["routerMac"]; // optional MNDP bypass

            var conn = new MacTelnetConnection { RouterMac = macAddr };
            // Capture transport-level diagnostics to test output.
            conn.TransportDiagnostic = msg => Console.Write(msg);
            conn.Open(host, user, pass);
            return conn;
        }

        [TestMethod]
        public void MacTelnet_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            using (var conn = OpenMacTelnetConnection())
            {
                Console.WriteLine();  // newline after diagnostic dots
                var ifaces = conn.LoadAll<Interface>().ToList();

                Console.WriteLine($"=== MACTELNET INTERFACES ({ifaces.Count} found) ===");
                foreach (var i in ifaces)
                    Console.WriteLine($"  [{i.DefaultName}] type={i.Type}");

                Assert.IsTrue(ifaces.Count > 0, "Router should expose at least one interface");
            }
        }

        [TestMethod]
        public void MacTelnet_SetAndVerify_InterfaceEther1Comment()
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

            const string testComment = "tik4net-mactelnet-test";

            using (var conn = OpenMacTelnetConnection())
            {
                Console.WriteLine();
                var e1 = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = testComment;
                conn.Save(e1);
                Console.WriteLine($"Set: '{testComment}'");

                var e1v = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                Console.WriteLine($"Verified: '{e1v.Comment}'");
                Assert.AreEqual(testComment, e1v.Comment, "Comment should be updated via MAC-Telnet");
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
