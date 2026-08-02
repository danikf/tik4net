using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    /// <summary>
    /// A measuring harness, not a test: it drives sustained request/response load over whichever
    /// transport is under test and writes a merged trace of the reader thread and the calling threads.
    /// Built for P2.46 and kept because it is the tool that answers "is this stall ours or the
    /// router's" — see <c>Docs/findings-router-session-throughput.md</c> for what it established and
    /// for the knobs.
    /// </summary>
    /// <remarks>
    /// It asserts nothing about elapsed time on purpose. The condition it exposes is the router's, it
    /// varies with the lab and the load, and a stopwatch assertion would only produce a flaky test out
    /// of a diagnostic. Skipped unless <c>TIK_PROBE=1</c> so a normal suite run never pays for it.
    /// </remarks>
    [TestClass]
    public class P246StallProbe : TestBase
    {
        private const int Iterations = 40;
        private const int Workers = 6;
        private const int RoundsPerWorker = 4;

        protected override bool ReuseConnectionAcrossTests => false;

        [TestMethod]
        public void Probe_SustainedLoad_MeasureRoundTripDegradation()
        {
            SkipOnSingleCommandTransport();
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1 to run it. See Docs/findings-router-session-throughput.md.");

            var rows = new ConcurrentQueue<string>();
            var clock = Stopwatch.StartNew();
            var problems = new ConcurrentQueue<string>();
            var timings = new ConcurrentQueue<string>();

            void Row(string dir, string word)
            {
                string id = Regex.Match(word ?? "", @"0xFF0006=u8:(\d+)").Groups[1].Value;
                rows.Enqueue($"{clock.Elapsed.TotalMilliseconds,10:F3} thr{Thread.CurrentThread.ManagedThreadId,-3} {dir} id={id,-4} len={(word ?? "").Length}");
            }

            Connection.OnWriteRow += (s, e) => Row(">>", e.Word);
            Connection.OnReadRow  += (s, e) => Row("<<", e.Word);

            // The row hooks fire on the *caller's* thread once it wakes from its wait, so they date the
            // hand-off, not the arrival. The wire sink below is emitted inside the reader loop, on the
            // reader thread, which is the only place that knows when the bytes actually landed. The gap
            // between the two is precisely what tells a slow router apart from a stalled client.
            var wire = new WireSink(rows, clock);

            string outDir = Environment.GetEnvironmentVariable("TIK_PROBE_DIR") ?? Path.GetTempPath();
            string file = Path.Combine(outDir, $"p246-{ResolveConnectionType()}-{DateTime.Now:HHmmss}.txt");
            using (tik4net.Diagnostics.TikWireTrace.Capture(wire))
            try
            {
                Connection.LoadAll<Interface>().ToList();   // warm the catalog out of the measurement

                int iterations = int.TryParse(Environment.GetEnvironmentVariable("TIK_PROBE_ITERS"), out int it) ? it : Iterations;
                for (int iter = 0; iter < iterations; iter++)
                {
                    var sw = Stopwatch.StartNew();
                    // Serial mode issues exactly the same number of requests one at a time, so a stall that
                    // survives it is about request count, not about concurrency.
                    int workerCount = Environment.GetEnvironmentVariable("TIK_PROBE_SERIAL") == "1" ? 1 : Workers;
                    var workers = new List<Task>();
                    for (int w = 0; w < workerCount; w++)
                    {
                        int worker = w;
                        workers.Add(Task.Run(() =>
                        {
                            for (int r = 0; r < RoundsPerWorker * (Workers / workerCount); r++)
                            {
                                var one = Stopwatch.StartNew();
                                try
                                {
                                    int n = Op();
                                    one.Stop();
                                    if (one.ElapsedMilliseconds > 100)
                                        timings.Enqueue($"slow: w{worker}r{r} thr{Thread.CurrentThread.ManagedThreadId} {one.ElapsedMilliseconds} ms n={n} at t={clock.Elapsed.TotalMilliseconds:F0}");
                                }
                                catch (Exception ex)
                                {
                                    one.Stop();
                                    problems.Enqueue($"w{worker}r{r} thr{Thread.CurrentThread.ManagedThreadId} after {one.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}");
                                }
                            }
                        }));
                    }
                    Task.WaitAll(workers.ToArray(), 120000);
                    sw.Stop();

                    // Well past the point where the first connection has slowed down, ask a *fresh*
                    // connection the same question: if it is fast, whatever degraded is per-connection
                    // state, not a router-wide condition.
                    if (iter == 20)
                    {
                        using (var second = OpenSecondaryConnection())
                        {
                            var s2 = Stopwatch.StartNew();
                            for (int i = 0; i < 24; i++) second.LoadAll<Interface>().Count();
                            s2.Stop();
                            timings.Enqueue($"  >> fresh connection, 24 serial reads: {s2.ElapsedMilliseconds} ms");
                        }
                        var s3 = Stopwatch.StartNew();
                        for (int i = 0; i < 24; i++) Connection.LoadAll<Interface>().Count();
                        s3.Stop();
                        timings.Enqueue($"  >> original connection, 24 interface getalls (1186 B each): {s3.ElapsedMilliseconds} ms");

                        // Same degraded session, a reply two orders of magnitude smaller. Slow here too =
                        // the whole session is slow; still fast = the cost is per reply byte.
                        var s4 = Stopwatch.StartNew();
                        for (int i = 0; i < 24; i++) Connection.LoadSingle<tik4net.Objects.System.SystemIdentity>();
                        s4.Stop();
                        timings.Enqueue($"  >> original connection, 24 identity gets (~50 B each): {s4.ElapsedMilliseconds} ms");

                        // And does simply leaving it alone undo it?
                        Thread.Sleep(5000);
                        var s5 = Stopwatch.StartNew();
                        for (int i = 0; i < 24; i++) Connection.LoadAll<Interface>().Count();
                        s5.Stop();
                        timings.Enqueue($"  >> original connection after a 5 s idle, 24 interface getalls: {s5.ElapsedMilliseconds} ms");
                    }
                    timings.Enqueue($"iter {iter,2}: {sw.ElapsedMilliseconds} ms  (t={clock.Elapsed.TotalMilliseconds:F0})");
                    if (!problems.IsEmpty) break;
                }
            }
            finally
            {
                File.WriteAllText(file,
                    "=== timings ===" + Environment.NewLine + string.Join(Environment.NewLine, timings)
                    + Environment.NewLine + Environment.NewLine + "=== problems ===" + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
                    + Environment.NewLine + Environment.NewLine + "=== rows ===" + Environment.NewLine
                    // Two producers (reader thread and callers) enqueue independently; the leading
                    // fixed-width timestamp makes an ordinal sort a chronological one.
                    + string.Join(Environment.NewLine, rows.OrderBy(x => x, StringComparer.Ordinal)), Encoding.UTF8);
                Console.WriteLine("trace -> " + file);
            }

            Assert.IsTrue(problems.IsEmpty,
                string.Join(Environment.NewLine, problems) + Environment.NewLine
                + "Expected under sustained load — see Docs/findings-router-session-throughput.md. Trace: " + file);
        }

        /// <summary>
        /// The measured operation, and with it the reply size — which is what the knee actually tracks.
        /// <c>interface</c> (default) answers ~1186 B, <c>ipaddress</c> ~194 B, <c>identity</c> ~50 B;
        /// comparing them is how "after N requests" was ruled out in favour of "after N bytes".
        /// </summary>
        private int Op()
        {
            switch (Environment.GetEnvironmentVariable("TIK_PROBE_OP"))
            {
                case "identity":  return Connection.LoadSingle<tik4net.Objects.System.SystemIdentity>() != null ? 1 : 0;
                case "ipaddress": return Connection.LoadAll<Objects.Ip.IpAddress>().Count();
                default:          return Connection.LoadAll<Interface>().Count();
            }
        }

        private sealed class WireSink : tik4net.Diagnostics.ITikWireTraceSink
        {
            private readonly ConcurrentQueue<string> _rows;
            private readonly Stopwatch _clock;

            internal WireSink(ConcurrentQueue<string> rows, Stopwatch clock) { _rows = rows; _clock = clock; }

            public void Emit(string channel, tik4net.Diagnostics.TikWireDir dir, byte[] data, int offset, int count, string note)
                => _rows.Enqueue($"{_clock.Elapsed.TotalMilliseconds,10:F3} thr{Thread.CurrentThread.ManagedThreadId,-3} "
                    + $"WIRE {dir} {channel} len={count}");
        }
    }
}
