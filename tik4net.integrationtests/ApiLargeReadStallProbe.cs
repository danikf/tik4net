using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Why a large binary-API read sometimes dies on its receive timeout part-way through, and whether the
    /// state of the connection it runs on has anything to do with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The observed failure is <c>QueueTreeU64WriteTest</c> on the <c>api</c> leg: a
    /// <c>LoadList&lt;QueueTree&gt;()</c> over ~550 rows timed out with 550 sentences already in hand.
    /// <c>GetAll</c> applies <c>ReceiveTimeout</c> <b>per sentence</b>, not per read, so this means the
    /// router went quiet for a full 30 s mid-answer — the same shape as the WinBox M2 paged-read stall
    /// (<c>Docs/winbox-native-m2-protocol.md</c> §29).
    /// </para>
    /// <para>
    /// Two candidate causes are worth ruling in or out before anything else, because both would be cheap to
    /// work around if true. <b>Age</b>: the suite shares one process-wide connection across the entire run
    /// (<c>TestBase</c>), so the read that failed ran on a session that had already served hundreds of
    /// commands. <b>Contention</b>: the binary API multiplexes by tag, so another command or monitor may be
    /// holding a tag while this read starves. Neither applies to the WinBox measurement — that one opened a
    /// fresh connection per read and did one read on it — which is why they have to be tested here rather
    /// than inferred from there.
    /// </para>
    /// <para>
    /// Four arms, same read in each, so the stall rate is comparable across them. Gated behind
    /// <c>TIK_PROBE=1</c>; a probe is a measurement, not a regression test, and it deliberately takes
    /// minutes. Tune with <c>TIK4NET_STALL_READS</c> (reads per arm, default 12) and
    /// <c>TIK4NET_STALL_PATH</c> (the table, default <c>/queue/tree</c> — it needs to be a big one).
    /// </para>
    /// </remarks>
    [TestClass]
    public class ApiLargeReadStallProbe : TestBase
    {
        // Its own connections throughout, so the suite's shared session is neither used nor disturbed.
        protected override bool ReuseConnectionAcrossTests => false;

        private static int ReadsPerArm
        {
            get
            {
                int n;
                return int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_STALL_READS"),
                                    out n) && n > 0 ? n : 12;
            }
        }

        private static string TablePath =>
            Environment.GetEnvironmentVariable("TIK4NET_STALL_PATH") ?? "/queue/tree";

        [TestMethod]
        public void Probe_ApiLargeRead_ConnectionStateArms()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Probe — set TIK_PROBE=1 to run it.");

            var type = ResolveConnectionType();
            if (type != TikConnectionType.Api && type != TikConnectionType.ApiSsl)
                Assert.Inconclusive($"This probe measures the binary-API reader; transport is '{type}'.");

            string path = TablePath;
            int reads = ReadsPerArm;
            Log($"=== {type} {path}/print, {reads} reads per arm ===");

            var results = new List<ArmResult>
            {
                FreshConnectionPerRead(path, reads),
                OneConnectionReused(path, reads),
                OneConnectionAfterChatter(path, reads),
                OneConnectionWithAnIdleMonitor(path, reads),
            };

            Log("");
            Log("arm                                stalls  median ms  max ms");
            foreach (var r in results)
                Log(string.Format(CultureInfo.InvariantCulture, "{0,-34} {1,3}/{2,-3} {3,9:F0} {4,7:F0}",
                    r.Name, r.Stalls, r.Reads, r.MedianMs, r.MaxMs));

            // Deliberately not asserted. The question is which arm stalls, and a threshold here would turn
            // a measurement into a flaky test — see the summary above and Docs/HISTORY.md for the numbers.
            Log("");
            Log("A stall in EVERY arm points at the router, not at the connection's state.");
        }

        /// <summary>A brand-new connection per read, doing that one read and nothing else.</summary>
        private ArmResult FreshConnectionPerRead(string path, int reads)
        {
            var arm = new ArmResult("fresh connection per read", reads);
            for (int i = 1; i <= reads; i++)
            {
                using (var conn = OpenProbeConnection())
                    Measure(arm, conn, path, i);
            }
            return Report(arm);
        }

        /// <summary>One connection, every read on it — hypothesis 1, minus the unrelated traffic.</summary>
        private ArmResult OneConnectionReused(string path, int reads)
        {
            var arm = new ArmResult("one connection, reused", reads);
            using (var conn = OpenProbeConnection())
                for (int i = 1; i <= reads; i++)
                    Measure(arm, conn, path, i);
            return Report(arm);
        }

        /// <summary>
        /// Hypothesis 1 proper — a "tired" connection: a few hundred unrelated commands first, so the
        /// session has the history the suite's shared connection has by the time it reaches a large read.
        /// </summary>
        private ArmResult OneConnectionAfterChatter(string path, int reads)
        {
            var arm = new ArmResult("one connection after 300 commands", reads);
            using (var conn = OpenProbeConnection())
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 300; i++)
                    conn.CreateCommand("/system/identity/print").ExecuteList();
                Log($"  warm-up: 300 commands in {sw.ElapsedMilliseconds} ms");

                for (int i = 1; i <= reads; i++)
                    Measure(arm, conn, path, i);
            }
            return Report(arm);
        }

        /// <summary>
        /// Hypothesis 2 — a second tag occupied while the read runs. An <c>/interface/listen</c> is the
        /// cheapest way to hold one: it stays open, produces nothing on an idle router, and is exactly the
        /// shape the monitor tests leave behind.
        /// </summary>
        private ArmResult OneConnectionWithAnIdleMonitor(string path, int reads)
        {
            var arm = new ArmResult("one connection + idle listen on another tag", reads);
            using (var conn = OpenProbeConnection())
            {
                ITikCommand monitor = conn.CreateCommand("/interface/listen");
                monitor.ExecuteWithCallback(row => { });
                Thread.Sleep(500);      // let it settle into its wait

                try
                {
                    for (int i = 1; i <= reads; i++)
                        Measure(arm, conn, path, i);
                }
                finally
                {
                    try { monitor.CancelAndJoin(5000); } catch { /* the probe's outcome is the reads */ }
                }
            }
            return Report(arm);
        }

        private ITikConnection OpenProbeConnection()
        {
            var conn = LabSetup(ResolveConnectionType()).Create(ResolveConnectionType());
            return conn;
        }

        private void Measure(ArmResult arm, ITikConnection conn, string path, int attempt)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                int rows = conn.CreateCommand(path + "/print").ExecuteList().Count();
                arm.Add(sw.Elapsed.TotalMilliseconds);
                Log($"  {arm.Name} #{attempt}: {sw.ElapsedMilliseconds} ms, {rows} rows");
            }
            catch (Exception ex)
            {
                arm.AddStall(sw.Elapsed.TotalMilliseconds);
                // The message now carries the socket counters, which say WHICH stall this is: silent
                // router, a reply still trickling in, or this tag starved while the socket stayed busy.
                Log($"  {arm.Name} #{attempt}: STALL after {sw.ElapsedMilliseconds} ms — "
                    + ex.GetType().Name + ": " + Oneline(ex.Message));
            }
        }

        private ArmResult Report(ArmResult arm)
        {
            Log($"  -> {arm.Name}: {arm.Stalls} stall(s) in {arm.Reads}, median {arm.MedianMs:F0} ms");
            return arm;
        }

        private static string Oneline(string s) =>
            (s ?? "").Replace("\r", " ").Replace("\n", " ");

        private void Log(string line)
        {
            Console.WriteLine(line);
            TestContext?.WriteLine(line);
        }

        private sealed class ArmResult
        {
            private readonly List<double> _ok = new List<double>();
            private readonly List<double> _stalled = new List<double>();

            public ArmResult(string name, int reads) { Name = name; Reads = reads; }

            public string Name { get; }
            public int Reads { get; }
            public int Stalls => _stalled.Count;

            public void Add(double ms) => _ok.Add(ms);
            public void AddStall(double ms) => _stalled.Add(ms);

            public double MedianMs
            {
                get
                {
                    if (_ok.Count == 0) return 0;
                    var sorted = _ok.OrderBy(x => x).ToList();
                    return sorted[sorted.Count / 2];
                }
            }

            public double MaxMs => _ok.Count == 0 ? 0 : _ok.Max();
        }
    }
}
