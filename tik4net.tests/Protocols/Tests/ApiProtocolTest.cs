// ApiProtocolTest.cs — MikroTik API protocol: login + list interfaces + set/restore comment on ether1
// Uses the production tik4net + tik4net.objects library (ConnectionFactory + O/R mapper).
// DataTestMethod variant covers all 4 transports (Api / ApiSsl / Rest / RestSsl) in one run.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.tests
{
    [TestClass]
    public class ApiProtocolTest
    {
        [TestMethod]
        public void Api_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var interfaces = conn.LoadAll<Interface>().ToList();

                Console.WriteLine($"=== API INTERFACES ({interfaces.Count} found) ===");
                foreach (var iface in interfaces)
                    Console.WriteLine($"  [{iface.DefaultName}] type={iface.Type} running={iface.Running}");
                Console.WriteLine("=================================================");

                Assert.IsTrue(interfaces.Count > 0, "Router should have at least one interface");
                Assert.IsTrue(interfaces.Any(i => i.DefaultName == "ether1"),
                    "Router should have ether1");
            }
        }

        [TestMethod]
        public void Api_SetAndVerify_InterfaceEther1Comment()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                Login_ListInterfaces_SetComment_Body(conn);
            }
        }

        // ── Parity test: same CRUD body over all 4 transports ────────────────

        // CLI transports (Telnet, MacTelnet, SSH) are tested via their own runsettings
        // (telnet.runsettings, mactelnet.runsettings) — they require special router setup
        // and have different capability/behaviour from the binary protocols below.
        [DataTestMethod]
        [DataRow(TikConnectionType.Api)]
        [DataRow(TikConnectionType.ApiSsl)]
        [DataRow(TikConnectionType.Rest)]
        [DataRow(TikConnectionType.RestSsl)]
        public void AllTransports_Login_ListInterfaces_SetComment(TikConnectionType type)
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            Console.WriteLine($"=== Transport: {type} ===");
            try
            {
                using (var conn = ConnectionFactory.OpenConnection(type, host, user, pass))
                {
                    Login_ListInterfaces_SetComment_Body(conn);
                }
            }
            catch (TikConnectionException ex)
            {
                Console.WriteLine($"Connection error for {type}: {ex.Message}");
                throw;
            }
        }

        private static void Login_ListInterfaces_SetComment_Body(ITikConnection conn)
        {
            // List interfaces
            var interfaces = conn.LoadAll<Interface>().ToList();

            Console.WriteLine($"=== INTERFACES ({interfaces.Count} found) ===");
            foreach (var iface in interfaces)
                Console.WriteLine($"  [{iface.DefaultName}] type={iface.Type} running={iface.Running}");
            Console.WriteLine("=============================================");

            Assert.IsTrue(interfaces.Count > 0, "Router should have at least one interface");
            Assert.IsTrue(interfaces.Any(i => i.DefaultName == "ether1"), "Router should have ether1");

            // Set and verify comment on ether1
            var ether1 = interfaces.First(i => i.DefaultName == "ether1");
            string original = ether1.Comment ?? "";
            Console.WriteLine($"Original ether1 comment: '{original}'");

            const string testComment = "tik4net-transport-parity-test";
            ether1.Comment = testComment;
            conn.Save(ether1);
            Console.WriteLine($"Set comment to: '{testComment}'");

            var ether1After = conn.LoadAll<Interface>().FirstOrDefault(i => i.DefaultName == "ether1");
            Assert.IsNotNull(ether1After);
            string verified = ether1After.Comment ?? "";
            Console.WriteLine($"Verified comment: '{verified}'");

            // Restore
            ether1After.Comment = original;
            conn.Save(ether1After);

            var ether1Restored = conn.LoadAll<Interface>().FirstOrDefault(i => i.DefaultName == "ether1");
            string restored = ether1Restored?.Comment ?? "";
            Console.WriteLine($"Restored comment: '{restored}'");

            Assert.AreEqual(testComment, verified, "Comment should be set correctly");
            Assert.AreEqual(original, restored, "Original comment should be restored");
        }
    }
}
