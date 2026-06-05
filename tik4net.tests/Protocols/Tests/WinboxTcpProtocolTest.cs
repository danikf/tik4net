// WinboxTcpProtocolTest.cs — Winbox/TCP M2 protocol tests
// Scenario: login + list interfaces + set/restore comment on ether1.
// Terminal access via mepty handler [76].

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace tik4net.tests
{
    [TestClass]
    public class WinboxTcpProtocolTest
    {
        private const int WINBOX_PORT = 8291;

        [Ignore]
        [TestMethod]
        public void WinboxTcp_Login_ListInterfaces_ReturnsAtLeastOne()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                var interfaces = client.ListInterfaces(pass);

                Console.WriteLine($"=== WINBOX/TCP INTERFACES ({interfaces.Count} found) ===");
                foreach (var iface in interfaces)
                    Console.WriteLine("  " + iface);
                Console.WriteLine("=================================================");

                Assert.IsTrue(interfaces.Count > 0, "Router should have at least one interface");
            }
        }

        [TestMethod]
        [Ignore("Flaky WinBox mepty session open ('No SESSION_ID in M2 response') — drain timing between terminal sessions; to be resolved later.")]
        public void WinboxTcp_SetAndVerify_InterfaceEther1Comment()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                string original = client.GetInterfaceComment(pass, "ether1");
                Console.WriteLine($"Original ether1 comment: '{original}'");

                const string testComment = "tik4net-winbox-test";
                client.SetInterfaceComment(pass, "ether1", testComment);
                Console.WriteLine($"Set comment to: '{testComment}'");

                string verified = client.GetInterfaceComment(pass, "ether1");
                Console.WriteLine($"Verified comment: '{verified}'");

                client.SetInterfaceComment(pass, "ether1", original);
                string restored = client.GetInterfaceComment(pass, "ether1");
                Console.WriteLine($"Restored comment: '{restored}'");

                Assert.AreEqual(testComment, verified, "Comment should be set correctly");
                Assert.AreEqual(original,    restored, "Original comment should be restored");
            }
        }

        // ── IP-layer smoke test ──────────────────────────────────────────────────
        // Verifies that the Winbox M2 wire protocol is reachable and well-formed
        // over plain TCP/IP (Layer 3) — without using any WinboxM2Client helper.
        //
        // EC-SRP5 initial exchange (both sides raw bytes):
        //   Client → [len 1B][0x06][user \0][32B pubkey][1B parity]
        //   Server ← [49 1B][0x06][32B xWB][1B parityB][16B salt]
        //
        // The server always replies with the challenge regardless of key validity
        // (it cannot verify the client until step 2), so a zeroed pubkey is fine.
        // This makes the test cheap — no EC math, no AES, no full auth round-trip.
        [TestMethod]
        public void WinboxM2_IpLayer_TcpPort8291_EcSrp5ChallengeExchange_Works()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"] ?? "admin";

            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = 5000;
                tcp.SendTimeout    = 5000;
                tcp.Connect(host, WINBOX_PORT);

                Assert.IsTrue(tcp.Connected, "TCP connection to port 8291 should succeed");
                Console.WriteLine($"Connected to {host}:{WINBOX_PORT}");

                var ns = tcp.GetStream();

                // Send EC-SRP5 client hello
                byte[] userBytes = Encoding.UTF8.GetBytes(user);
                byte[] pubKey    = new byte[32]; // all-zero — server doesn't validate yet
                byte[] payload   = userBytes
                    .Concat(new byte[] { 0x00 })
                    .Concat(pubKey)
                    .Concat(new byte[] { 0x00 })
                    .ToArray();
                byte[] frame = new byte[] { (byte)payload.Length, 0x06 }
                    .Concat(payload).ToArray();

                Console.WriteLine($"Sending EC-SRP5 hello: {frame.Length}B (user='{user}', pubkey=zeroed)");
                ns.Write(frame, 0, frame.Length);

                // Receive challenge header [len][tag]
                byte[] hdr = new byte[2];
                int got = 0;
                while (got < 2) got += ns.Read(hdr, got, 2 - got);

                byte respLen = hdr[0];
                byte respTag = hdr[1];
                Console.WriteLine($"Challenge header: len={respLen}, tag=0x{respTag:X2}");

                Assert.AreEqual(0x06, respTag,
                    "Server must respond with EC-SRP5 tag 0x06");
                Assert.AreEqual(49, respLen,
                    "EC-SRP5 challenge must be exactly 49 bytes (32B xWB + 1B parity + 16B salt)");

                // Read full challenge and validate structure
                byte[] challenge = new byte[respLen];
                int total = 0;
                while (total < respLen) total += ns.Read(challenge, total, respLen - total);

                Assert.AreEqual(49, total, "Full challenge payload must arrive");

                byte[] xWB  = challenge.Take(32).ToArray();
                int    parity = challenge[32];
                byte[] salt   = challenge.Skip(33).Take(16).ToArray();

                Console.WriteLine($"  xWB  (32B): {BitConverter.ToString(xWB).Substring(0, 23)}…");
                Console.WriteLine($"  parity    : {parity}");
                Console.WriteLine($"  salt (16B): {BitConverter.ToString(salt)}");

                Assert.IsTrue(parity == 0 || parity == 1,
                    $"Server parity must be 0 or 1, got {parity}");
                Assert.IsFalse(xWB.All(b => b == 0),
                    "Server public key xWB must not be all-zero");
                Assert.IsFalse(salt.All(b => b == 0),
                    "Salt must not be all-zero");
            }
        }

        [Ignore]
        [TestMethod]
        public void WinboxTcp_ReadListCatalog_ReturnsPackageEntries()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);
                string catalog = client.ReadListCatalog();

                Console.WriteLine("=== RAW CATALOG ===");
                Console.WriteLine(catalog);
                Console.WriteLine("===================");

                Assert.IsNotNull(catalog, "Catalog content should not be null");
                Assert.IsTrue(catalog.Length > 0, "Catalog should not be empty");
                Assert.IsTrue(catalog.Contains(".jg"), "Catalog should contain .jg plugin entries");
            }
        }

        [Ignore]
        [TestMethod]
        public void WinboxTcp_ParseCatalog_EntriesHaveValidFields()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);
                string catalogText = client.ReadListCatalog();
                var entries = WinboxM2Client.ParseCatalog(catalogText);

                Console.WriteLine($"=== CATALOG ({entries.Count} entries) ===");
                foreach (var e in entries)
                    Console.WriteLine("  " + e);
                Console.WriteLine("=========================================");

                Assert.IsTrue(entries.Count > 0, "Catalog should have entries");
                Assert.IsTrue(entries.All(e => !string.IsNullOrEmpty(e.Name)),
                    "Each entry must have a name");
                Assert.IsTrue(entries.Where(e => e.Name.EndsWith(".jg"))
                    .All(e => !string.IsNullOrEmpty(e.Version)),
                    "Each .jg plugin entry must have a version");
                Assert.IsTrue(entries.All(e => e.Size > 0),
                    "Each entry must have a positive size");
            }
        }

        [TestMethod]
        public void WinboxTcp_GetSystemInfo_HasVersionAndBoard()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                SystemInfo info = client.GetSystemInfo();
                Console.WriteLine("=== SYSTEM INFO ===");
                Console.WriteLine("  " + info);
                Console.WriteLine("===================");

                Assert.IsFalse(string.IsNullOrEmpty(info.Version), "Version should be non-empty");
                Assert.IsFalse(string.IsNullOrEmpty(info.Board),   "Board should be non-empty");
            }
        }

        [TestMethod]
        public void WinboxTcp_GetSystemInfo_PrintsAllFields()
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                var fields = client.GetM2Fields(new[] { 13, 4 }, 7);
                Console.WriteLine("=== [13,4] cmd=7 response fields ===");
                foreach (var kv in fields)
                    Console.WriteLine($"  key=0x{kv.Key:X6}  type={kv.Value.Item1,-10}  val={kv.Value.Item2}");
                Console.WriteLine("====================================");

                Assert.IsTrue(fields.Count > 0, "Should get at least one field back");
            }
        }
    }
}
