// ApiProtocolTest.cs — MikroTik API protocol: login + list interfaces + set/restore comment on ether1
// Uses the production tik4net + tik4net.objects library (ConnectionFactory + O/R mapper).

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
                // Read original comment
                var ether1 = conn.LoadAll<Interface>()
                    .FirstOrDefault(i => i.DefaultName == "ether1");
                Assert.IsNotNull(ether1, "ether1 must exist on router");

                string original = ether1.Comment ?? "";
                Console.WriteLine($"Original ether1 comment: '{original}'");

                // Set test comment
                const string testComment = "tik4net-api-test";
                ether1.Comment = testComment;
                conn.Save(ether1);
                Console.WriteLine($"Set comment to: '{testComment}'");

                // Verify — re-read
                var ether1After = conn.LoadAll<Interface>()
                    .FirstOrDefault(i => i.DefaultName == "ether1");
                Assert.IsNotNull(ether1After);
                string verified = ether1After.Comment ?? "";
                Console.WriteLine($"Verified comment: '{verified}'");

                // Restore
                ether1After.Comment = original;
                conn.Save(ether1After);

                var ether1Restored = conn.LoadAll<Interface>()
                    .FirstOrDefault(i => i.DefaultName == "ether1");
                string restored = ether1Restored?.Comment ?? "";
                Console.WriteLine($"Restored comment: '{restored}'");

                Assert.AreEqual(testComment, verified,  "Comment should be set correctly via API");
                Assert.AreEqual(original,    restored,  "Original comment should be restored");
            }
        }
    }
}
