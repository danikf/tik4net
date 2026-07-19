// WinboxNativeMacProtocolTest.cs — WinBox native-M2 CRUD over the MAC layer, via ITikConnection.
// Drives the production WinboxNativeMacConnection (UDP 20561, client_type=0x0f90, structured M2 —
// NOT a terminal) through the shared native-M2 engine (.jg resolver, encode/decode, Safe Mode).
// Mirrors WinboxCliMacProtocolTest; sets RouterMac from App.config to bypass MNDP discovery.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.WinboxNativeMac;

namespace tik4net.integrationtests
{
    [TestClass]
    public class WinboxNativeMacProtocolTest
    {
        // Enable the MAC Winbox server on the router before tests run (separate from the mac-telnet server).
        [ClassInitialize]
        public static void EnableMacWinboxServer(TestContext _)
        {
            var (host, user, pass) = Cfg();
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

                // Best-effort clear of any stale safe-mode lock from a previous unclean run.
                try { conn.CreateCommand("/safe-mode/release").ExecuteNonQuery(); } catch { }
            }
        }

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        private static WinboxNativeMacConnection OpenWinboxNativeMacConnection()
        {
            var (host, user, pass) = Cfg();
            var macAddr = ConfigurationManager.AppSettings["routerMac"]; // optional MNDP bypass

            var conn = new WinboxNativeMacConnection { RouterMac = macAddr };
            conn.Open(host, user, pass);
            return conn;
        }

        // ── 1. read (getall) over the MAC layer ───────────────────────────────
        [TestMethod]
        public void WinboxNativeMac_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            using (var conn = OpenWinboxNativeMacConnection())
            {
                var ifaces = conn.LoadAll<Interface>().ToList();

                Console.WriteLine($"=== WINBOX-NATIVE-MAC INTERFACES ({ifaces.Count} found) ===");
                foreach (var i in ifaces)
                    Console.WriteLine($"  [{i.DefaultName}] type={i.Type}");

                Assert.IsTrue(ifaces.Count > 0, "Router should expose at least one interface");
            }
        }

        // ── 2. CRUD round-trip (set / verify / restore) over the MAC layer ─────
        [TestMethod]
        public void WinboxNativeMac_SetAndVerify_InterfaceEther1Comment()
        {
            var (host, user, pass) = Cfg();

            string original;
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                original = api.LoadAll<Interface>().FirstOrDefault(i => i.DefaultName == "ether1")?.Comment ?? "";
            Console.WriteLine($"Original: '{original}'");

            const string testComment = "tik4net-winboxnativemac-test";
            using (var conn = OpenWinboxNativeMacConnection())
            {
                var e1 = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = testComment;
                conn.Save(e1);

                var e1v = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                Console.WriteLine($"Verified: '{e1v.Comment}'");
                Assert.AreEqual(testComment, e1v.Comment, "Comment should be updated via WinBox native MAC");
            }

            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var e1 = api.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = original;
                api.Save(e1);
            }
            Console.WriteLine($"Restored: '{original}'");
        }

        // ── 3. Safe Mode take/release over the MAC layer (handler [17]) ────────
        [TestMethod]
        public void WinboxNativeMac_SafeMode_TakeRelease_PersistsChange()
        {
            var (host, user, pass) = Cfg();

            string original;
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                original = api.LoadAll<Interface>().FirstOrDefault(i => i.DefaultName == "ether1")?.Comment ?? "";

            const string testComment = "tik4net-winboxnativemac-safemode";
            using (var conn = OpenWinboxNativeMacConnection())
            {
                Assert.IsTrue(conn.Supports(TikConnectionCapability.SafeMode), "native MAC should advertise SafeMode");

                conn.SafeModeTake();
                Assert.IsTrue(conn.SafeModeGet(), "SafeModeGet should be true after take");

                var e1 = conn.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                e1.Comment = testComment;
                conn.Save(e1);

                conn.SafeModeRelease(); // commit — change must survive
                Assert.IsFalse(conn.SafeModeGet(), "SafeModeGet should be false after release");
            }

            // Verify the committed change persisted (seen from a fresh API session), then restore.
            using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var e1 = api.LoadAll<Interface>().First(i => i.DefaultName == "ether1");
                Assert.AreEqual(testComment, e1.Comment, "released safe-mode change should persist");
                e1.Comment = original;
                api.Save(e1);
            }
            Console.WriteLine($"Restored: '{original}'");
        }
    }
}
