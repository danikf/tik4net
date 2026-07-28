// WinboxMproxyBudgetProbeTest.cs — does mproxy really degrade under load, or were we reading the
// channel out of step? (P2.20, re-opened by P2.40)
//
// P2.20 is written as ROUTER-side behaviour: "mproxy degrades under back-to-back M2 sessions and stays
// degraded", concluded from probes that observed 56 opens / 1.54 MB on one channel before it stopped
// answering, with the degradation outliving the TCP connection. Every one of those observations was made
// through WinboxM2Client, whose SendRecvEncrypted was a bare `send; recv` with no request-id correlation
// — precisely the construct P2.22 proved wrong once already, where "the MAC transport is lossy" turned
// out to be our own uncorrelated read shifting every reply by one. The correlation now exists in this
// client, so these probes re-measure the same ground with an instrument that can tell its own reply from
// somebody else's.
//
// Read the numbers, not the pass/fail: the probes assert only that the router answered at all, and print
// what they measured. StaleFramesDrained/Skipped are the deciding figures — a non-zero count means the
// pre-correlation client WAS returning the wrong frame to its caller, and every conclusion drawn from it
// about the router has to be re-derived.
//
// Measured 2026-07-28, RouterOS 7.23.2 CHR, correlation on — both of P2.20's claims came out false, and
// the correlation was not why:
//   • not a budget — one channel took 74 opens / 6 838 567 B in 55 s and answered `list` afterwards; the
//     failures that do happen land at no consistent offset (0 B, 542 KB, 1.48 MB, 2.68 MB)
//   • it does not outlive the connection — 8 back-to-back full fetches (~12 MB, no pauses) served `list`
//     8/8 and all 18 bodies in 7 of 8; the one short session was followed immediately by a complete one
//   • what DOES die is the one channel, permanently: after a failure the same socket will not return a
//     26-byte `list` at +0 s or +15 s
//   • drained=0 / skipped=0 in every run, failures included — the uncorrelated client was never reading
//     off by one here, so P2.22's mechanism does not apply to the mproxy path
//   • the failure is `timeout with 0/2 B in hand` — not one byte of the chunk header arrives, so it is not
//     our framing desyncing after a partial read
//   • slow is not dead: roteros.jg normally reads in 7.7 s but took 23.2 s once and still completed; a
//     full 18-plugin set normally takes 12.2 s and twice took 35–40 s

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Ad-hoc load probes against a live router (read-only, minutes long) — comment out to run.")]
    [TestClass]
    public class WinboxMproxyBudgetProbeTest
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

        // Only the resolved '<unique>.gz' name is openable (P2.18), so the probe reads what the router
        // itself advertises rather than a guessed filename — a refusal must mean load, not a bad request.
        private static List<CatalogEntry> JgEntries(WinboxM2Client client) =>
            WinboxM2Client.ParseCatalog(client.ReadListCatalog())
                .Where(e => (e.Name ?? "").EndsWith(".jg", StringComparison.OrdinalIgnoreCase))
                .ToList();

        /// <summary>
        /// Probe A — how far one channel goes. P2.20's headline observation was that a single channel does
        /// not survive unbounded file reads (56 opens / 1.54 MB). Repeat that with correlation on, reading
        /// the whole advertised plugin set round and round until the router refuses or a cap is hit.
        /// </summary>
        [TestMethod]
        public void Probe_MproxyByteBudget_OnOneChannel()
        {
            const int MaxOpens = 120;      // ~2x the 56 opens P2.20 recorded before it wedged
            const long MaxBytes = 6L << 20; // ~4x the 1.54 MB
            long bytes = 0;
            int opens = 0, rounds = 0;
            string stopReason = "cap reached";
            var sw = Stopwatch.StartNew();

            using (var client = Connect())
            {
                var entries = JgEntries(client);
                opens++; // the list read is itself an mproxy open
                // roteros.jg first, as the production loader does: it is the 933 KB one and every router
                // serves it, so reading it last would confound "the channel died" with "the big file died".
                entries = entries.OrderByDescending(e => e.Name == "roteros.jg").ToList();
                Console.WriteLine($"list: {entries.Count} .jg plugins advertised (op timeout {client.OperationTimeoutMs} ms)");
                Assert.IsTrue(entries.Count > 0, "mproxy served no plugin list at all on a fresh channel");

                while (opens < MaxOpens && bytes < MaxBytes && stopReason == "cap reached")
                {
                    rounds++;
                    foreach (var e in entries)
                    {
                        if (opens >= MaxOpens || bytes >= MaxBytes) break;
                        opens++;
                        var one = Stopwatch.StartNew();
                        byte[] body;
                        try { body = ReadWholeFile(client, e.Unique + ".gz"); }
                        catch (Exception ex)
                        {
                            stopReason = $"exception on open #{opens} ({e.Name}) after {bytes} B: {ex.GetType().Name}";
                            Console.WriteLine($"  #{opens,3} {e.Name,-16} EXC after {one.ElapsedMilliseconds} ms " +
                                $"[{client.LastReadFailure}]: {ex.Message}");
                            break;
                        }
                        int len = body?.Length ?? 0;
                        Console.WriteLine($"  #{opens,3} {e.Name,-16} {len,8} B in {one.ElapsedMilliseconds,5} ms  (cum {bytes + len} B)");
                        if (len == 0)
                        {
                            stopReason = $"empty read on open #{opens} ({e.Name}) after {bytes} B";
                            break;
                        }
                        bytes += len;
                    }
                }

                Console.WriteLine($"stopped: {stopReason}");
                Console.WriteLine($"opens={opens} rounds={rounds} bytes={bytes} elapsed={sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"stale frames: drained={client.StaleFramesDrained} skipped={client.StaleFramesSkipped}");

                // Whatever happened above, is the channel still usable, and does it come back if left alone?
                // P2.20 says a wedged channel stays wedged; recovery after a pause is a different animal
                // from a channel that is simply gone.
                for (int wait = 0; wait <= 15; wait += 15)
                {
                    if (wait > 0) System.Threading.Thread.Sleep(wait * 1000);
                    int after = -1;
                    try { after = JgEntries(client).Count; }
                    catch (Exception ex) { Console.WriteLine($"post-load list (+{wait}s) threw [{client.LastReadFailure}]: {ex.Message}"); }
                    Console.WriteLine($"post-load list (+{wait}s): {after} plugins");
                    if (after > 0) break;
                }
            }
        }

        /// <summary>
        /// Probe C — the same fetch the production loader does, driven one chunk at a time. Probe A shows
        /// 17 of 18 plugins reading cleanly and <c>roteros.jg</c> — the 933 KB one, and the only multi-chunk
        /// read — never answering. That is the fetch that must be taken apart: which chunk stops, whether
        /// the open itself succeeded, and whether the channel is dead afterwards or only that handle is.
        /// </summary>
        [TestMethod]
        public void Probe_MproxyRoteros_ChunkByChunk()
        {
            using (var client = Connect())
            {
                var entries = JgEntries(client);
                var core = entries.First(e => e.Name == "roteros.jg");
                var small = entries.First(e => e.Name != "roteros.jg");
                Console.WriteLine($"roteros: unique={core.Unique} size={core.Size} version={core.Version}");

                // A small plugin first, as the control: same code path, one chunk.
                ReadChunked(client, small, small.Unique + ".gz");

                // Then the big one, exactly as production names it.
                ReadChunked(client, core, core.Unique + ".gz");

                // Is the channel itself gone, or only that handle?
                int after = -1;
                try { after = JgEntries(client).Count; }
                catch (Exception ex) { Console.WriteLine("list after roteros threw: " + ex.Message); }
                Console.WriteLine($"list after roteros: {after} plugins");
                Console.WriteLine($"stale frames: drained={client.StaleFramesDrained} skipped={client.StaleFramesSkipped}");
            }
        }

        // Same open + chunk loop probe C uses, quiet. Equivalent to WinboxM2Client.ReadFileBytes (A/B'd:
        // both wedge, at different offsets, so the wedge is not a property of either) — kept only because
        // it checks the handle before reading, which turns a refused open into an empty result instead of
        // 30 s of reads on a handle that does not exist.
        private static byte[] ReadWholeFile(WinboxM2Client client, string openName)
        {
            int handle = client.MproxyOpenHandle(openName);
            if (handle < 0) return Array.Empty<byte>();
            var all = new List<byte>();
            for (int i = 0; i < 64; i++)
            {
                byte[] chunk = client.MproxyReadChunk(handle);
                if (chunk.Length == 0) break;
                all.AddRange(chunk);
                if (chunk.Length < 32768) break;
            }
            return all.ToArray();
        }

        private static void ReadChunked(WinboxM2Client client, CatalogEntry entry, string openName)
        {
            Console.WriteLine($"--- {entry.Name} as '{openName}' (advertised size {entry.Size} B)");
            int handle;
            var sw = Stopwatch.StartNew();
            try { handle = client.MproxyOpenHandle(openName); }
            catch (Exception ex) { Console.WriteLine($"  open threw after {sw.ElapsedMilliseconds} ms: {ex.Message}"); return; }
            Console.WriteLine($"  open -> handle={handle} in {sw.ElapsedMilliseconds} ms");
            if (handle < 0) return;

            long total = 0;
            for (int i = 0; i < 64; i++)
            {
                var one = Stopwatch.StartNew();
                byte[] chunk;
                try { chunk = client.MproxyReadChunk(handle); }
                catch (Exception ex)
                {
                    Console.WriteLine($"  chunk #{i} threw after {one.ElapsedMilliseconds} ms (total {total} B): {ex.Message}");
                    return;
                }
                Console.WriteLine($"  chunk #{i}: {chunk.Length} B in {one.ElapsedMilliseconds} ms (total {total + chunk.Length} B)");
                total += chunk.Length;
                if (chunk.Length == 0) break;
                if (chunk.Length < 32768) break;
            }
            Console.WriteLine($"  done: {total} B");
        }

        /// <summary>
        /// Probe B — back-to-back M2 sessions, the trigger P2.40 says the faster suite now hits. Each
        /// iteration is a full connect / list / fetch-everything / disconnect, with no pause, so any
        /// degradation that outlives the TCP connection shows up as a later iteration resolving fewer
        /// plugins or none.
        /// </summary>
        [TestMethod]
        public void Probe_MproxyBackToBackSessions()
        {
            const int Sessions = 8;
            var listed = new List<int>();
            var fetched = new List<int>();
            var ms = new List<long>();
            int drained = 0, skipped = 0;

            for (int i = 0; i < Sessions; i++)
            {
                var sw = Stopwatch.StartNew();
                int gotList = -1, gotBodies = 0;
                try
                {
                    using (var client = Connect())
                    {
                        var entries = JgEntries(client);
                        gotList = entries.Count;
                        foreach (var e in entries)
                        {
                            byte[] body = null;
                            try { body = ReadWholeFile(client, e.Unique + ".gz"); }
                            catch (Exception ex) { Console.WriteLine($"  #{i} {e.Name}: {ex.GetType().Name} {ex.Message}"); }
                            if (body != null && body.Length > 0) gotBodies++;
                            else break; // the loader stops at the first failure too — do not hammer a dying channel
                        }
                        drained += client.StaleFramesDrained;
                        skipped += client.StaleFramesSkipped;
                    }
                }
                catch (Exception ex) { Console.WriteLine($"  #{i} session failed: {ex.GetType().Name} {ex.Message}"); }
                sw.Stop();
                listed.Add(gotList);
                fetched.Add(gotBodies);
                ms.Add(sw.ElapsedMilliseconds);
                Console.WriteLine($"session #{i}: list={gotList} bodies={gotBodies} in {sw.ElapsedMilliseconds} ms");
            }

            Console.WriteLine($"list:    {string.Join("/", listed)}");
            Console.WriteLine($"bodies:  {string.Join("/", fetched)}");
            Console.WriteLine($"elapsed: {string.Join("/", ms)} ms");
            Console.WriteLine($"stale frames across all sessions: drained={drained} skipped={skipped}");

            Assert.IsTrue(listed.Any(c => c > 0),
                "mproxy served no plugin list in any of the " + Sessions + " sessions");
        }
    }
}
