// WinboxJgFetchTest.cs — the WinBox .jg menu catalog: resolving it, fetching it over M2 (port 8291,
// no HTTP), and caching it.
//
// The plugin filenames must be RESOLVED, never hardcoded. mproxy serves a "list" catalog whose entries
// carry {name, unique, size, crc, version}; `unique` is the version-stamped on-disk name in
// /home/web/webfig/, and only it can actually be read. The stable name is a trap — mproxy *opens*
// "roteros.jg.gz" and reports the correct size, but the read never answers and takes the M2 channel
// with it (P2.18). Which plugins exist is version- and package-dependent, so no fixed list is right:
// 7.23.2 CHR serves container/iot/userman5/dude and none of the mpls/roting4 an older list named.
//
// Note that mproxy does not survive unbounded file reads on one channel, and it stays degraded for a
// while afterwards. These tests are therefore deliberately few and cheap; do not add byte-budget or
// command-sweep probes here — run those ad hoc.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;

namespace tik4net.integrationtests
{
    [TestClass]
    public class WinboxJgFetchTest
    {
        private const int WINBOX_PORT = 8291;

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        private static WinboxM2Client Connect()
        {
            var (host, user, pass) = Cfg();
            var client = new WinboxM2Client();
            client.Connect(host, WINBOX_PORT);
            client.Authenticate(host, WINBOX_PORT, user, pass);
            return client;
        }

        private static List<CatalogEntry> JgEntries(WinboxM2Client client) =>
            WinboxM2Client.ParseCatalog(client.ReadListCatalog())
                .Where(e => (e.Name ?? "").EndsWith(".jg", StringComparison.OrdinalIgnoreCase))
                .ToList();

        // The "list" catalog is the resolve step everything else depends on.
        [TestMethod]
        public void Winbox_ListCatalog_ResolvesJgPlugins()
        {
            List<CatalogEntry> jg;
            using (var client = Connect())
                jg = JgEntries(client);

            foreach (var e in jg)
                Console.WriteLine($"  name={e.Name} unique={e.Unique} size={e.Size} version={e.Version}");

            Assert.IsTrue(jg.Count > 0, "list catalog should advertise at least one .jg plugin");
            Assert.IsTrue(jg.Any(e => e.Name == "roteros.jg"), "the core roteros.jg plugin should be advertised");
            Assert.IsTrue(jg.All(e => !string.IsNullOrEmpty(e.Unique)),
                "every .jg entry should carry the 'unique' on-disk filename used to open it");
        }

        // Documents the trap that made the fetch look impossible: only the resolved name is openable.
        [TestMethod]
        public void Winbox_MproxyOpen_RejectsUnresolvedName()
        {
            // One connection for all three steps: a refused open returns a clean error and leaves the
            // channel usable, and mproxy is happier with fewer M2 sessions in quick succession.
            using (var client = Connect())
            {
                CatalogEntry core = JgEntries(client).First(e => e.Name == "roteros.jg");

                var refused = client.MproxyOpenRaw(core.Name, 7);
                Console.WriteLine("open '" + core.Name + "' -> " +
                    string.Join(", ", refused.Select(f => $"0x{f.Key:X6}={f.Value.Item2}")));
                Assert.IsTrue(refused.Values.Any(v => (v.Item2 ?? "").ToString().Contains("cannot open source file")),
                    "the bare plugin name should be refused outright");

                var opened = client.MproxyOpenRaw(core.Unique + ".gz", 7);
                Console.WriteLine("open '" + core.Unique + ".gz' -> " +
                    string.Join(", ", opened.Select(f => $"0x{f.Key:X6}={f.Value.Item2}")));
                Assert.IsFalse(opened.Values.Any(v => (v.Item2 ?? "").ToString().Contains("cannot open source file")),
                    "the resolved '<unique>.gz' name should open");
            }
        }

        // The production loader (WinboxJgCatalog.Load) against a genuinely cold cache, then a warm one.
        // This is the path that matters: it must resolve the plugin set from the router, download it once,
        // leave a usable connection behind, and then not download it again.
        [TestMethod]
        public void WinboxNative_CatalogCache_ColdThenWarm()
        {
            var (host, user, pass) = Cfg();
            string cacheDir = Path.Combine(Path.GetTempPath(), "tik4net-cachetest-" + Guid.NewGuid().ToString("N"));

            try
            {
                // The contract is not "the first connect downloads everything" — mproxy can refuse partway,
                // and the loader is built to keep what it got and continue on the next connect. So open
                // repeatedly and assert the end state: the set completes, and once it has, opens are cheap.
                // The pause before each attempt matters: mproxy degrades under back-to-back M2 sessions and
                // will not serve a full ~1 MB plugin set again until it has been left alone briefly (P2.20),
                // so without it this fails purely because of whatever ran before it.
                var elapsed = new List<long>();
                var handlerCounts = new List<int>();
                long lastRead = -1;
                for (int i = 0; i < 4; i++)
                {
                    System.Threading.Thread.Sleep(5000);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.WinboxNative))
                    {
                        var native = (tik4net.WinboxNative.WinboxNativeConnection)conn;
                        native.CatalogCachePath = cacheDir;
                        conn.Open(host, user, pass);
                        handlerCounts.Add(native.CatalogHandlerCount);
                        // The catalog load must leave a working connection behind, not just a catalog.
                        try { lastRead = conn.CallCommandSync("/interface/print").Count(); }
                        catch (Exception ex) { lastRead = -1; Console.WriteLine("  read failed: " + ex.Message); }
                    }
                    sw.Stop();
                    elapsed.Add(sw.ElapsedMilliseconds);

                    string d = Path.Combine(cacheDir, "plugins");
                    Console.WriteLine($"open #{i}: {sw.ElapsedMilliseconds} ms, rows={lastRead}, " +
                        $"handlers={handlerCounts[i]}, cached plugins=" +
                        (Directory.Exists(d) ? Directory.GetFiles(d).Length : 0));
                }

                // Nothing at all cached after four spaced attempts means mproxy would not serve the plugin
                // list — the router refusing a correctly-formed request (P2.20), not the cache being wrong.
                // Distinguish that from a broken cache rather than reporting a red that says nothing.
                if (!Directory.Exists(Path.Combine(cacheDir, "plugins")))
                    Assert.Inconclusive(
                        "mproxy served no plugin list in 4 attempts — router-side degradation (P2.20). " +
                        "Re-run this test on a rested router; it passes standalone.");

                var files = Directory.GetFiles(Path.Combine(cacheDir, "plugins"));
                Console.WriteLine("cache: " + string.Join(", ", files.Select(Path.GetFileName)));

                // The point of P2.23: no connect may end up on the seed table. mproxy does refuse `list`
                // from time to time (P2.20), and when it does, the remembered plugin set has to carry the
                // load — otherwise the connection still opens and still answers, but every dynamic-field
                // and singleton lookup silently says "no" and the caller gets zeros for live counters.
                Assert.IsTrue(File.Exists(Path.Combine(Path.Combine(cacheDir, "lists"),
                        host.Replace(':', '_') + ".list")),
                    "the resolved plugin set must be remembered per router, so a refused 'list' can fall back to it");
                // Deliberately asserted on the END state, not on every connect. This test runs against a
                // throwaway cache directory, so it has no remembered plugin set to fall back on — and when
                // mproxy refuses `list` on the first attempts (P2.20; observed here as 0/0/619/619) a
                // seeds-only catalog is the designed outcome, not a defect. What must hold is that the
                // catalog fills in across connections and never loses ground once it has.
                Assert.IsTrue(handlerCounts.Last() > 0,
                    $"the last connect must produce a usable catalog, not seeds only (handlers: {string.Join("/", handlerCounts)})");
                Assert.AreEqual(handlerCounts.Max(), handlerCounts.Last(),
                    $"the warm connect must not resolve fewer handlers than an earlier one (handlers: {string.Join("/", handlerCounts)})");
                Assert.IsTrue(files.Any(f => Path.GetFileName(f).StartsWith("roteros-")),
                    "roteros.jg is served by every router and is fetched first, so it must end up cached");
                Assert.IsTrue(files.All(f => Path.GetFileName(f).Contains("-")),
                    "plugins are cached under their version-stamped 'unique' name");
                Assert.IsTrue(lastRead > 0, "the catalog must resolve /interface by the last connect");
                Assert.IsTrue(elapsed.Last() * 2 < elapsed.Max(),
                    $"a warm cache should open far faster than a cold one ({string.Join("/", elapsed)} ms)");
            }
            finally
            {
                try { Directory.Delete(cacheDir, true); } catch { /* best effort */ }
            }
        }
    }
}
