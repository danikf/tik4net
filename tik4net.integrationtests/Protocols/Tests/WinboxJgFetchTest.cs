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
// An mproxy read occasionally stops answering, and when it does that CHANNEL is finished for good — a new
// connection to the same router works immediately (measured 2026-07-28; see WinboxMproxyBudgetProbeTest,
// which is where byte-budget and command-sweep probes belong — not here).

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
                // ⚠️ The pause was added on the belief that mproxy degrades under back-to-back M2 sessions
                // and needs to be left alone before it will serve a full ~1 MB plugin set again. That is
                // REFUTED (2026-07-28): eight full 18-plugin fetches back to back, no pauses, served `list`
                // 8/8 and all 18 bodies in 7 of 8. The pause is kept only because P2.40 — this test failing
                // inside a full run — has not been measured yet, and removing it would change two things at
                // once. It is not protecting against what the comment used to claim.
                // A throwaway cache directory is NOT enough to make the load cold. The parsed-catalog cache
                // is process-wide and keyed by the plugin set, not by the cache dir, so inside a full run
                // an earlier connection has already populated it and this one is served from memory — 619
                // handlers, 309 ms, and nothing written to its own cache dir. That is what made this test
                // pass standalone and go Inconclusive in the suite, misread as mproxy refusing `list`.
                tik4net.Winbox.WinboxJgCatalog.ClearSharedCatalogs();

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

                // Nothing cached. Say WHICH of the two very different reasons it is, instead of blaming the
                // router for both — that conflation is the whole of P2.40. A full handler count with an
                // empty cache dir means the load was served from a parsed catalog this process already
                // held, so the cold path was never exercised (should be impossible now that the test
                // clears it above, hence the assert rather than an Inconclusive).
                if (!Directory.Exists(Path.Combine(cacheDir, "plugins")))
                {
                    Assert.IsTrue(handlerCounts.All(h => h == 0),
                        "nothing was cached yet the catalog resolved (handlers: " + string.Join("/", handlerCounts) +
                        ") — the load came from the process-wide parsed catalog, so this run did not test the " +
                        "cold path at all. ClearSharedCatalogs() should have prevented that.");
                    Assert.Inconclusive(
                        "mproxy served no plugin list on any of 4 fresh connections, and no catalog was " +
                        "resolved either — the router genuinely served nothing (P2.20/P2.40). " +
                        "Capture a wbx.catalog trace; it passes standalone.");
                }

                var files = Directory.GetFiles(Path.Combine(cacheDir, "plugins"));
                Console.WriteLine("cache: " + string.Join(", ", files.Select(Path.GetFileName)));

                // The point of P2.23: no connect may end up on the seed table. A `list` does fail from time
                // to time — its channel stopped answering (P2.20) — and when it does, the remembered set has
                // to carry the load; otherwise the connection still opens and still answers, but every dynamic-field
                // and singleton lookup silently says "no" and the caller gets zeros for live counters.
                Assert.IsTrue(File.Exists(Path.Combine(Path.Combine(cacheDir, "lists"),
                        host.Replace(':', '_') + ".list")),
                    "the resolved plugin set must be remembered per router, so a refused 'list' can fall back to it");
                // Deliberately asserted on the END state, not on every connect. This test runs against a
                // throwaway cache directory, so it has no remembered plugin set to fall back on, and a
                // connect whose channel died mid-fetch legitimately ends on seeds only. What must hold is that the
                // catalog fills in across connections and never loses ground once it has. (The 0/0/619/619
                // shape once recorded here as normal is P2.40's open question, not a documented outcome.)
                Assert.IsTrue(handlerCounts.Last() > 0,
                    $"the last connect must produce a usable catalog, not seeds only (handlers: {string.Join("/", handlerCounts)})");
                Assert.AreEqual(handlerCounts.Max(), handlerCounts.Last(),
                    $"the warm connect must not resolve fewer handlers than an earlier one (handlers: {string.Join("/", handlerCounts)})");
                Assert.IsTrue(files.Any(f => Path.GetFileName(f).StartsWith("roteros-")),
                    "roteros.jg is served by every router and is fetched first, so it must end up cached");
                Assert.IsTrue(files.All(f => Path.GetFileName(f).Contains("-")),
                    "plugins are cached under their version-stamped 'unique' name");
                Assert.IsTrue(lastRead > 0, "the catalog must resolve /interface by the last connect");

                // The warm-cache speed-up, measured only between connects that were actually SERVED. The
                // earlier assertions are all on the end state because mproxy legitimately refuses `list` on
                // a connect (P2.20/P2.40) and that connect then ends on seeds; this one used to compare the
                // last connect against elapsed.Max() regardless, so a run where the first three were refused
                // and the FOURTH did the cold fetch failed for having no warm connect to be faster than —
                // measured live 31353/35051/34768/39120 ms with handlers 0/0/0/1361. There is no warm
                // measurement to make in that shape, so make none.
                int firstServed = handlerCounts.FindIndex(h => h > 0);
                if (firstServed >= 0 && firstServed < elapsed.Count - 1)
                {
                    Assert.IsTrue(elapsed.Last() * 2 < elapsed[firstServed],
                        "a warm cache should open far faster than the cold connect that filled it " +
                        $"({string.Join("/", elapsed)} ms, handlers {string.Join("/", handlerCounts)})");
                }
                else
                {
                    Console.WriteLine("no warm connect to measure: the catalog only arrived on the last of " +
                        $"{elapsed.Count} connects (handlers {string.Join("/", handlerCounts)})");
                }
            }
            finally
            {
                try { Directory.Delete(cacheDir, true); } catch { /* best effort */ }
            }
        }
    }
}
