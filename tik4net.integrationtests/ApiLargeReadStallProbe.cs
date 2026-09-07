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
            Log("arm                                stalls  refused  median ms  max ms");
            foreach (var r in results)
                Log(string.Format(CultureInfo.InvariantCulture, "{0,-34} {1,3}/{2,-3} {3,7} {4,9:F0} {5,7:F0}",
                    r.Name, r.Stalls, r.Reads - r.Refusals, r.Refusals, r.MedianMs, r.MaxMs));

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

        // The low-level sentence form, not CreateCommand(path, params): the second overload takes typed
        // ITikCommandParameter, and what this needs is exactly the words the router sees.
        private static void RawSet(ITikConnection conn, string name)
        {
            ((ITikRawSentenceConnection)conn).CallCommandSync("/system/identity/set", "=name=" + name);
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
                // A refusal is not a new stall — it is the same wedged session being declined without a
                // command going out, so counting the two together makes an arm's rate depend on how many
                // reads happened to be left after it wedged. Which would compare across runs as if the
                // router had got worse: eight reads that wedge on the third report six "stalls" where the
                // pre-fail-fast probe reported one wedge and five more 30-second waits.
                if (ex is TikConnectionSessionClosedException)
                    arm.AddRefusal();
                else
                    arm.AddStall(sw.Elapsed.TotalMilliseconds);
                // The message now carries the socket counters, which say WHICH stall this is: silent
                // router, a reply still trickling in, or this tag starved while the socket stayed busy.
                Log($"  {arm.Name} #{attempt}: STALL after {sw.ElapsedMilliseconds} ms — "
                    + ex.GetType().Name + ": " + Oneline(ex.Message));

                // The first stall of the run is the only chance to look at a dead connection while it is
                // still dead — diagnose it here rather than trying to reproduce it in a test of its own,
                // which took 45 clean reads without ever reaching the state this run reaches in eight.
                if (!_diagnosed)
                {
                    _diagnosed = true;
                    DiagnoseDeadConnection(conn, path);
                }
            }
        }

        private bool _diagnosed;

        /// <summary>
        /// What is actually dead once a connection stops receiving: the router's session, or our reader?
        /// </summary>
        /// <remarks>
        /// "Not one byte since" is deliberately ambiguous — it is equally what a router that stopped sending
        /// and a reader thread that stopped reading look like. A <b>second, fresh</b> connection settles it
        /// from the outside: if it reads the same table fine, the router is answering and this session was
        /// killed. The write probe goes further and asks whether the dead session's commands still reach the
        /// router at all, which separates "the router is not answering us" from "the router is not hearing us".
        /// </remarks>
        private void DiagnoseDeadConnection(ITikConnection dead, string path)
        {
            Log("  --- post-stall diagnosis ---");
            Log($"  dead connection: IsOpened={dead.IsOpened}");

            using (var fresh = OpenProbeConnection())
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    int rows = fresh.CreateCommand(path + "/print").ExecuteList().Count();
                    Log($"  FRESH connection read the same table in {sw.ElapsedMilliseconds} ms ({rows} rows)"
                        + " -> the router is answering; it is this SESSION that is dead.");
                }
                catch (Exception ex)
                {
                    Log($"  FRESH connection also failed after {sw.ElapsedMilliseconds} ms -> the ROUTER is "
                        + "refusing everyone, not just the stalled session. " + Oneline(ex.Message));
                }

                // Does the dead session still HEAR us? A write whose effect a third party can see separates
                // "the router is not answering us" from "the router is not processing our session at all".
                string marker = "tik4net-stall-" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
                string identityBefore = fresh.CreateCommand("/system/identity/print")
                                             .ExecuteSingleRow().GetResponseField("name");
                try
                {
                    RawSet(dead, marker);
                    Log("  the dead connection's write returned normally");
                }
                catch (Exception ex)
                {
                    Log("  the dead connection's write failed: " + Oneline(ex.Message));
                }

                string identityAfter = fresh.CreateCommand("/system/identity/print")
                                            .ExecuteSingleRow().GetResponseField("name");
                Log(identityAfter == marker
                    ? "  …but the router APPLIED it -> the router hears the dead session and its replies are "
                      + "what never arrive."
                    : $"  …and the router did not apply it (identity is still '{identityAfter}') -> the dead "
                      + "session is not being processed at all.");

                if (identityAfter != identityBefore)
                {
                    RawSet(fresh, identityBefore);
                    Log($"  identity restored to '{identityBefore}'");
                }

                foreach (var row in fresh.CreateCommand("/user/active/print").ExecuteList())
                    Log("  /user/active: " + string.Join(" ", row.Words.Select(w => w.Key + "=" + w.Value)));

                var log = fresh.CreateCommand("/log/print").ExecuteList().ToList();
                foreach (var row in log.Skip(Math.Max(0, log.Count - 12)))
                    Log("  /log: " + row.GetResponseFieldOrDefault("time", "")
                        + " " + row.GetResponseFieldOrDefault("topics", "")
                        + " " + row.GetResponseFieldOrDefault("message", ""));
            }

            Log("  --- end diagnosis ---");
        }

        private ArmResult Report(ArmResult arm)
        {
            Log($"  -> {arm.Name}: {arm.Stalls} stall(s) in {arm.Reads - arm.Refusals} attempted read(s)"
                + (arm.Refusals > 0 ? $" (+{arm.Refusals} refused on the wedged session)" : "")
                + $", median {arm.MedianMs:F0} ms");
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

            /// <summary>
            /// Reads the connection refused outright once it had been declared dead. Kept apart from
            /// <see cref="Stalls"/>: they cost nothing, prove nothing new, and only say how far into the arm
            /// the wedge happened.
            /// </summary>
            public int Refusals { get; private set; }

            public void Add(double ms) => _ok.Add(ms);
            public void AddStall(double ms) => _stalled.Add(ms);
            public void AddRefusal() => Refusals++;

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
