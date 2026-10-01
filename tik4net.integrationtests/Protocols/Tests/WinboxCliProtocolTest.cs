// WinboxCliProtocolTest.cs — WinBox CLI smoke tests via ITikConnection.
// Drives the production WinboxCliConnection (TCP 8291, mepty terminal) through the CLI Layer.
// Full CRUD parity is verified by the TestBase-based suite via winboxcli.runsettings.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.WinboxCli;

namespace tik4net.integrationtests
{
    [TestClass]
    // Writes the comment of the test interface, as InterfaceTest and TikCommandTest do in the transport legs.
    [TestLock("testInterface-comment")]
    [TestCategory(TestCategories.LegIndependent)]
    public class WinboxCliProtocolTest : LockedTestBase
    {
        private static WinboxCliConnection OpenWinboxConnection()
        {
            var host = LabConfig.Get("host");
            var user = LabConfig.Get("user");
            var pass = LabConfig.Get("pass") ?? "";

            var conn = new WinboxCliConnection();
            conn.TransportDiagnostic = msg => Console.Write(msg);
            conn.Open(host, user, pass);
            return conn;
        }

        [TestMethod]
        public void WinboxCli_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            using (var conn = OpenWinboxConnection())
            {
                Console.WriteLine();
                var ifaces = conn.LoadAll<Interface>().ToList();

                Console.WriteLine($"=== WINBOX INTERFACES ({ifaces.Count} found) ===");
                foreach (var i in ifaces)
                    Console.WriteLine($"  [{i.DefaultName}] type={i.Type}");

                Assert.IsTrue(ifaces.Count > 0, "Router should expose at least one interface");
            }
        }

        [TestMethod]
        public void WinboxCli_SetAndVerify_InterfaceEther1Comment()
        {
            var host = LabConfig.Get("host");
            var user = LabConfig.Get("user");
            var pass = LabConfig.Get("pass") ?? "";

            string original;
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var e1 = api.LoadAll<Interface>().FirstOrDefault(i => i.DefaultName == "ether1");
                original = e1?.Comment.Value ?? "";
            }
            Console.WriteLine($"Original: '{original}'");

            const string testComment = "tik4net-winbox-test";

            using (var conn = OpenWinboxConnection())
            {
                Console.WriteLine();
                var e1 = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = testComment;
                conn.Save(e1);
                Console.WriteLine($"Set: '{testComment}'");

                var e1v = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                Console.WriteLine($"Verified: '{e1v.Comment}'");
                Assert.AreEqual(testComment, e1v.Comment, "Comment should be updated via WinBox Console");
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
