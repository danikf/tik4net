// WinboxMproxyMemoryProbeTest.cs — does an mproxy file open cost the router memory it never gets back?
//
// P2.20 established that mproxy file handles cannot be released: the handle id is a plain sequential
// counter and there is no close/release verb (cmds 8–11 answer 0xFE0009 not-implemented). That was noted
// as a curiosity while ruling out a handle leak as the cause of channel loss. What was never measured is
// the other half of it — whether each unreleasable handle also pins ROUTER MEMORY.
//
// It matters well beyond the probes: every WinboxNative connection opens `list` plus up to 18 plugin
// files, so a long test run performs hundreds of opens against a router that (if this is real) never
// frees any of them. That would make it a library problem, not a probe artefact, and a candidate cause
// for P2.35 (the lab router filling up and rebooting) and for P2.40 (the catalog test failing only inside
// a full run).
//
// ⚠️ READ THIS BEFORE TRUSTING A NUMBER THIS PROBE PRINTS. On the lab CHR (Hyper-V VM `Mikrotik-host`)
// **Dynamic Memory is enabled**, and RouterOS's `free-memory` is then fiction: it reports total-memory
// 4096 MB while the host had actually assigned 512 MB (demand 56 MB), so the balloon appears inside the
// guest as ~3584 MB "used". It arrives as a sharp step roughly 4 minutes after boot, with nothing running
// and nothing in /log — which is indistinguishable from a leak, and cost an hour on 2026-07-28 before the
// host's own `Get-VM … MemoryAssigned/MemoryDemand` settled it. Give that VM FIXED RAM before running
// this, or every delta below is noise on top of a 3.5 GB step. The tell that you are looking at the
// balloon rather than a leak: the suite stays green at "77 MB free".
//
// Method: read free-memory over a SEPARATE binary-API connection — never the channel under test — then
// perform a batch of opens, then re-read. Batches start small and start with a tiny file so the per-open
// cost is known before anything large is attempted; the probe aborts itself if free memory falls below a
// floor, because measuring a router to death proves nothing and costs a reboot.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Ad-hoc memory probe against a live router — comment out to run.")]
    [TestClass]
    public class WinboxMproxyMemoryProbeTest
    {
        private const int WINBOX_PORT = 8291;

        // Stop before the router is in danger. Below this much free memory a CHR starts behaving oddly and
        // the next batch could take it down mid-measurement.
        private const long FreeMemoryFloorBytes = 512L * 1024 * 1024;

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        // Always over the binary API, so the measurement cannot perturb (or be perturbed by) the M2
        // channel whose opens are being counted.
        private static long FreeMemory()
        {
            var (host, user, pass) = Cfg();
            using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                conn.Open(host, user, pass);
                var row = conn.CreateCommandAndParameters("/system/resource/print").ExecuteSingleRow();
                return long.Parse(row.GetResponseField("free-memory"), CultureInfo.InvariantCulture);
            }
        }

        private static WinboxM2Client Connect()
        {
            var (host, user, pass) = Cfg();
            var client = new WinboxM2Client();
            client.Connect(host, WINBOX_PORT);
            client.Authenticate(host, WINBOX_PORT, user, pass);
            return client;
        }

        [TestMethod]
        public void Probe_MproxyOpen_MemoryCost()
        {
            long baseline = FreeMemory();
            Console.WriteLine($"baseline free-memory: {baseline:N0} B");
            Assert.IsTrue(baseline > FreeMemoryFloorBytes,
                $"router has only {baseline:N0} B free — reboot it before measuring, the probe needs headroom");

            using (var client = Connect())
            {
                var entries = WinboxM2Client.ParseCatalog(client.ReadListCatalog())
                    .Where(e => (e.Name ?? "").EndsWith(".jg", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var tiny = entries.OrderBy(e => e.Size).First();
                var core = entries.First(e => e.Name == "roteros.jg");
                Console.WriteLine($"tiny={tiny.Name} ({tiny.Size} B)  core={core.Name} ({core.Size} B)");

                // Open-only, tiny file: isolates the cost of the HANDLE from the cost of transferring bytes.
                Measure(client, "open only, tiny  ", tiny, 10, read: false, ref baseline);
                Measure(client, "open only, tiny  ", tiny, 40, read: false, ref baseline);

                // Open+read, tiny: adds one chunk of transfer.
                Measure(client, "open+read, tiny  ", tiny, 40, read: true, ref baseline);

                // Open-only on the 933 KB file: if the cost scales with FILE size rather than with the
                // handle, this is where it shows, and it shows without moving a byte of content.
                Measure(client, "open only, roteros", core, 10, read: false, ref baseline);

                // Open+read the big one: the shape the production catalog loader actually performs.
                Measure(client, "open+read, roteros", core, 5, read: true, ref baseline);
            }

            long after = FreeMemory();
            Console.WriteLine($"final free-memory: {after:N0} B");
        }

        private static void Measure(WinboxM2Client client, string label, CatalogEntry entry, int count,
                                    bool read, ref long free)
        {
            if (free < FreeMemoryFloorBytes)
            {
                Console.WriteLine($"{label} x{count,-4}: SKIPPED — only {free:N0} B free, below the floor");
                return;
            }

            long before = free;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long bytesRead = 0;
            for (int i = 0; i < count; i++)
            {
                int handle = client.MproxyOpenHandle(entry.Unique + ".gz");
                if (handle < 0) { Console.WriteLine($"{label}: open #{i} refused"); break; }
                if (!read) continue;
                for (int c = 0; c < 64; c++)
                {
                    byte[] chunk = client.MproxyReadChunk(handle);
                    bytesRead += chunk.Length;
                    if (chunk.Length < 32768) break;
                }
            }
            sw.Stop();

            free = FreeMemory();
            long delta = before - free;
            Console.WriteLine($"{label} x{count,-4}: free {before,13:N0} -> {free,13:N0} B " +
                $"(delta {delta,11:N0} B, {delta / Math.Max(1, count),9:N0} B/open, " +
                $"read {bytesRead:N0} B, {sw.ElapsedMilliseconds} ms)");
        }
    }
}
