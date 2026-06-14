// WinboxDumpCatalogTest.cs — Phase B: download all /home/web/webfig/ files to disk
// Run manually from Test Explorer when a live router is reachable.
// Output goes to _notes/WinboxMessage/ (configurable via App.config "catalogDumpDir").

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace tik4net.tests
{
    [TestClass]
    public class WinboxDumpCatalogTest
    {
        private const int WINBOX_PORT = 8291;

        private static readonly string DefaultDumpDir = @".\.tik4net";

        private static string ResolvePath(string path) =>
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

        [Ignore]
        [TestMethod]
        public void DumpAllWebfigFiles()
        {
            // ── Config ───────────────────────────────────────────────────────────
            string host  = ConfigurationManager.AppSettings["host"]           ?? "192.168.4.236";
            string user  = ConfigurationManager.AppSettings["user"]           ?? "admin";
            string pass  = ConfigurationManager.AppSettings["pass"]           ?? "";
            string dumpDir = ResolvePath(ConfigurationManager.AppSettings["catalogDumpDir"] ?? DefaultDumpDir);

            Directory.CreateDirectory(dumpDir);
            Console.WriteLine($"=== WINBOX CATALOG DUMP ===");
            Console.WriteLine($"  Router    : {host}:{WINBOX_PORT}");
            Console.WriteLine($"  User      : {user}");
            Console.WriteLine($"  Output dir: {dumpDir}");
            Console.WriteLine();

            // ── Connect + Authenticate ───────────────────────────────────────────
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);
                Console.WriteLine("Authentication OK.");

                // ── Download catalog text ────────────────────────────────────────
                string catalog = client.ReadListCatalog();
                Assert.IsNotNull(catalog, "Catalog content should not be null");
                Assert.IsTrue(catalog.Length > 0, "Catalog should not be empty");

                string listPath = Path.Combine(dumpDir, "list");
                File.WriteAllBytes(listPath, Encoding.UTF8.GetBytes(catalog));
                Console.WriteLine($"  Saved: list ({catalog.Length} chars → {new FileInfo(listPath).Length}B)");

                // ── Parse entries ────────────────────────────────────────────────
                List<CatalogEntry> entries = WinboxM2Client.ParseCatalog(catalog);
                Console.WriteLine($"  Catalog has {entries.Count} entries.");
                Console.WriteLine();

                // ── Download each file ───────────────────────────────────────────
                var results = new List<Tuple<string, long, long, bool>>(); // name, catalogSize, actualSize, ok
                foreach (CatalogEntry entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Console.WriteLine($"  SKIP: entry with empty name");
                        continue;
                    }

                    try
                    {
                        // .jg plugins: opened from /var/pckg/ via cmd=3, content gunzipped by client.
                        // Static files (list, *.png): opened from /home/web/webfig/ via cmd=7.
                        // Routing is handled transparently by ReadFileBytes(CatalogEntry).
                        string openName = string.IsNullOrEmpty(entry.Unique) ? entry.Name : entry.Unique;
                        byte[] bytes = client.ReadFileBytes(entry);
                        long actualSize = bytes != null ? bytes.Length : 0L;

                        if (bytes != null && bytes.Length > 0)
                        {
                            string outPath = Path.Combine(dumpDir, entry.Name);
                            File.WriteAllBytes(outPath, bytes);
                            results.Add(Tuple.Create(entry.Name, entry.Size, actualSize, true));
                            string note = actualSize == entry.Size ? "" : $"  (catalog/actual MISMATCH)";
                            Console.WriteLine($"  OK   : {entry.Name,-32} via {openName,-36} catalog={entry.Size,8}B  actual={actualSize,8}B{note}");
                        }
                        else
                        {
                            results.Add(Tuple.Create(entry.Name, entry.Size, 0L, false));
                            Console.WriteLine($"  EMPTY: {entry.Name,-32}  catalog={entry.Size,8}B  (empty response)");
                            Trace.WriteLine($"[WinboxDump] Empty response for: {entry.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add(Tuple.Create(entry.Name, entry.Size, 0L, false));
                        Console.WriteLine($"  FAIL : {entry.Name,-32}  ERROR: {ex.Message}");
                        Trace.WriteLine($"[WinboxDump] Failed to download '{entry.Name}': {ex}");
                    }
                }

                // ── Summary ──────────────────────────────────────────────────────
                Console.WriteLine();
                Console.WriteLine("=== DOWNLOAD SUMMARY ===");
                Console.WriteLine($"  {"Name",-32}  {"Cat.Size",9}  {"Actual",9}  Status");
                Console.WriteLine($"  {new string('-', 32)}  {new string('-', 9)}  {new string('-', 9)}  ------");
                foreach (var r in results)
                    Console.WriteLine($"  {r.Item1,-32}  {r.Item2,9}  {r.Item3,9}  {(r.Item4 ? "OK" : "FAIL")}");
                Console.WriteLine();

                int ok   = results.Count(r => r.Item4);
                int fail = results.Count(r => !r.Item4);
                Console.WriteLine($"  Downloaded: {ok} OK, {fail} failed");
                Console.WriteLine($"  Output dir: {dumpDir}");
                Console.WriteLine();

                // ── Report results ───────────────────────────────────────────────
                var jgFiles = Directory.GetFiles(dumpDir, "*.jg");
                int jgOk    = results.Count(r => r.Item1.EndsWith(".jg", StringComparison.OrdinalIgnoreCase) && r.Item4);
                int jgTotal = results.Count(r => r.Item1.EndsWith(".jg", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"  .jg files in output dir: {jgFiles.Length}  (downloaded {jgOk}/{jgTotal})");
                foreach (string jg in jgFiles)
                    Console.WriteLine($"    {Path.GetFileName(jg)}  ({new FileInfo(jg).Length}B)");

                Assert.IsTrue(ok >= 1,
                    $"At least the static files (list/*.png) should download to {dumpDir}");
                Assert.IsTrue(File.Exists(Path.Combine(dumpDir, "list")),
                    "The catalog 'list' file should have been written.");
                if (jgTotal > 0)
                    Assert.IsTrue(jgOk > 0,
                        $"At least one .jg plugin should download via /var/pckg/ cmd=3 (got 0 of {jgTotal}).");
            }
        }
    }
}
