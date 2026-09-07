using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using System.Net.Sockets;
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

        // The router's sniffer settings as they were before StartSniffer overwrote them; null once restored.
        private List<string> _snifferRestore;

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

            using (var side = OpenProbeConnection())
                StartSniffer(side);

            // The sniffer is router state, and this probe is routinely interrupted: without this the router
            // keeps capturing, with this probe's filters, long after the run that set them is gone.
            List<ArmResult> results;
            try
            {
                results = new List<ArmResult>
                {
                    FreshConnectionPerRead(path, reads),
                    OneConnectionReused(path, reads),
                    OneConnectionAfterChatter(path, reads),
                    OneConnectionWithAnIdleMonitor(path, reads),
                    OneConnectionWithALongTimeout(path, reads),
                };
            }
            finally
            {
                StopSnifferBestEffort();
            }

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

        /// <summary>
        /// The same read with a five-minute budget instead of thirty seconds — is the session dead, or just
        /// paused?
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the arm that decides what everything else means, and it exists because a router that stops
        /// answering an established session is a large claim to make on a thirty-second silence. The other arms
        /// cannot tell the two apart: they stop asking at the deadline, so "no reply within the budget" and
        /// "no reply ever" produce the same line.
        /// </para>
        /// <para>
        /// One reading already points at a pause. A stalled read's reply <i>continued arriving after its
        /// caller had given up</i> — 77 further sentences turned up under the abandoned tag while the next
        /// command was waiting. Sentences cannot arrive on a session the router has stopped serving, so at
        /// least that stall was a delay this deadline turned into a failure.
        /// </para>
        /// </remarks>
        private ArmResult OneConnectionWithALongTimeout(string path, int reads)
        {
            const int budgetSeconds = 300;
            var arm = new ArmResult($"one connection, {budgetSeconds} s budget", reads);
            using (var conn = OpenProbeConnection(budgetSeconds))
                for (int i = 1; i <= reads; i++)
                    Measure(arm, conn, path, i);
            return Report(arm);
        }

        // The low-level sentence form, not CreateCommand(path, params): the second overload takes typed
        // ITikCommandParameter, and what this needs is exactly the words the router sees.
        private static void RawSet(ITikConnection conn, string name)
        {
            ((ITikRawSentenceConnection)conn).CallCommandSync("/system/identity/set", "=name=" + name);
        }

        /// <summary>
        /// Whether a failed read is the phenomenon under measurement — the router going quiet — rather than
        /// the read never having been asked properly.
        /// </summary>
        /// <remarks>
        /// A trap (bad path, no permission) and a stall are both "the read threw", and counting them
        /// together lets a typo produce a full column of stalls that no router caused.
        /// </remarks>
        private static bool IsStall(Exception ex) =>
            ex is TikConnectionReceiveTimeoutException
            || ex is System.IO.IOException
            || ex is SocketException;

        private ITikConnection OpenProbeConnection(int receiveTimeoutSeconds = 0)
        {
            var setup = LabSetup(ResolveConnectionType());
            if (receiveTimeoutSeconds > 0)
                setup.ReceiveTimeout = TimeSpan.FromSeconds(receiveTimeoutSeconds);
            return setup.Create(ResolveConnectionType());
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
                {
                    arm.AddRefusal();
                }
                else if (IsStall(ex))
                {
                    arm.AddStall(sw.Elapsed.TotalMilliseconds);
                }
                else
                {
                    // Not a stall and not a refusal: the read never got as far as waiting. A mistyped
                    // TIK4NET_STALL_PATH reported itself as twelve stalls in twelve reads before this
                    // existed — a probe that turns its own misconfiguration into the phenomenon it is
                    // measuring is worse than one that does not run.
                    Log($"  {arm.Name} #{attempt}: NOT A STALL after {sw.ElapsedMilliseconds} ms — "
                        + ex.GetType().Name + ": " + Oneline(ex.Message));
                    throw;
                }
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
                    try
                    {
                        DiagnoseDeadConnection(conn, path);
                    }
                    catch (Exception diagnosisFailure)
                    {
                        // A probe that dies while explaining its first stall reports nothing about the other
                        // seven reads or the four remaining arms — and the arms are the measurement.
                        Log("  --- diagnosis abandoned: " + Oneline(diagnosisFailure.Message) + " ---");
                    }
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
                // Skipped when the fresh connection is stalled too: reading the identity through it would
                // throw, and losing the whole measurement to a failed diagnosis is the worse outcome.
                string marker = "tik4net-stall-" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
                string identityBefore;
                try
                {
                    identityBefore = fresh.CreateCommand("/system/identity/print")
                                          .ExecuteSingleRow().GetResponseField("name");
                }
                catch (Exception ex)
                {
                    Log("  write probe skipped — the fresh connection cannot read either: " + Oneline(ex.Message));
                    return;
                }

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

                // Twice, five seconds apart: a repl-bytes count that climbs while our own byte counter stays
                // frozen is the router actively pushing (or re-pushing) bytes we are not getting, which no
                // single reading can show.
                // Stopped first and read afterwards: the buffer keeps the LAST packets, and every further
                // second of probing pushes the silence we came to look at out of it.
                StopSniffer(fresh);

                LogRouterSideConnectionView(fresh, "sample 1");
                Thread.Sleep(5000);
                LogRouterSideConnectionView(fresh, "sample 2 (+5 s)");
                LogSnifferTail(fresh);

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

        /// <summary>
        /// The router's own view of every API connection it has, byte counters included.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one thing our socket counters cannot supply. "Not one byte arrived" is equally what a
        /// router that sent nothing and a router whose reply was lost on the way look like — and a router that
        /// simply stops answering an established session is a big claim to rest on client-side evidence alone.
        /// <c>repl-bytes</c> is what the <b>router</b> believes it has sent us. More than the stall message's
        /// received count means it answered and the reply never arrived, which is a network or TCP fault; equal
        /// counts mean it really did stop sending.
        /// </para>
        /// <para>
        /// Match the row by the local endpoint the stall message names — <c>src-address</c> carries the same
        /// <c>address:port</c>. <c>tcp-state</c> is worth reading beside it: an <c>established</c> row whose
        /// reply direction has gone quiet is a different animal from one the router has half-closed.
        /// </para>
        /// </remarks>
        private void LogRouterSideConnectionView(ITikConnection fresh, string label)
        {
            try
            {
                // Filter words rather than typed parameters: what this needs is exactly the sentence the
                // router sees, and connection rows are not a mapped entity.
                var rows = ((ITikRawSentenceConnection)fresh).CallCommandSync(
                    "/ip/firewall/connection/print", "?protocol=tcp", "?dst-port=8728").ToList();

                int shown = 0;
                foreach (var row in rows.OfType<ITikReSentence>())
                {
                    Log("  /ip/firewall/connection " + label + ": "
                        + row.GetResponseFieldOrDefault("src-address", "?")
                        + ":" + row.GetResponseFieldOrDefault("src-port", "?") + " -> "
                        + row.GetResponseFieldOrDefault("dst-address", "?")
                        + ":" + row.GetResponseFieldOrDefault("dst-port", "?")
                        + " tcp-state=" + row.GetResponseFieldOrDefault("tcp-state", "?")
                        + " orig-bytes=" + row.GetResponseFieldOrDefault("orig-bytes", "?")
                        + " repl-bytes=" + row.GetResponseFieldOrDefault("repl-bytes", "?")
                        + " timeout=" + row.GetResponseFieldOrDefault("timeout", "?"));
                    shown++;
                }

                if (shown == 0)
                    Log("  /ip/firewall/connection: no tracked rows for port 8728 — connection tracking is "
                        + "off or not tracking local traffic, so this comparison is unavailable.");
            }
            catch (Exception ex)
            {
                Log("  /ip/firewall/connection unavailable: " + Oneline(ex.Message));
            }
        }

        /// <summary>
        /// Puts the router's own packet sniffer on the API port, so a stall can be read from the wire.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Everything else here is measured from one end or the other and cannot separate "the router sent
        /// nothing" from "the router sent and it did not arrive". The router capturing its own egress can:
        /// packets in the silence mean the bytes left, none mean they never did.
        /// </para>
        /// <para>
        /// <c>only-headers</c> matters twice over. It keeps the buffer holding packets rather than payload,
        /// and the payload here is a production-shaped queue tree full of customer names — capture what is
        /// needed to read TCP, not the data.
        /// </para>
        /// <para>
        /// The buffer is small and scrolling on purpose: it then holds the <b>last</b> packets before the
        /// stall, which is exactly the window of interest, and it bounds what has to be read back over the
        /// API. Note the sniffer is load on the router being measured — treat a capture as a look at the
        /// mechanism, not as a clean rate measurement.
        /// </para>
        /// </remarks>
        private void StartSniffer(ITikConnection conn)
        {
            try
            {
                var raw = (ITikRawSentenceConnection)conn;
                _snifferRestore = CaptureSnifferSettings(raw);
                raw.CallCommandSync("/tool/sniffer/set", "=only-headers=yes", "=memory-limit=200",
                    "=memory-scroll=yes", "=file-name=", "=filter-port=8728",
                    "=filter-operator-between-entries=and").ToList();
                raw.CallCommandSync("/tool/sniffer/stop").ToList();
                raw.CallCommandSync("/tool/sniffer/start").ToList();
                Log("  sniffer: started on the router (headers only, last ~2500 packets on port 8728)");
            }
            catch (Exception ex)
            {
                Log("  sniffer: could not start — " + Oneline(ex.Message));
            }
        }

        /// <summary>
        /// The settings this probe is about to overwrite, as the <c>=name=value</c> words that put them back.
        /// </summary>
        /// <remarks>
        /// A probe that leaves the lab reconfigured is how the next investigation gets a baseline nobody can
        /// explain — the sniffer's filters in particular, which silently narrow what a later capture sees.
        /// Only the fields <see cref="StartSniffer"/> writes are captured: restoring a field the probe never
        /// touched would be a second way to change the router. A field the router does not report is
        /// restored to empty, which is what <c>/tool/sniffer/print</c> omitting it means.
        /// </remarks>
        private static List<string> CaptureSnifferSettings(ITikRawSentenceConnection raw)
        {
            string[] fields =
            {
                "only-headers", "memory-limit", "memory-scroll", "file-name",
                "filter-port", "filter-operator-between-entries",
            };

            var row = raw.CallCommandSync("/tool/sniffer/print").OfType<ITikReSentence>().FirstOrDefault();
            if (row == null)
                return null;

            return fields
                .Select(f => "=" + f + "=" + row.GetResponseFieldOrDefault(f, string.Empty))
                .ToList();
        }

        private void StopSniffer(ITikConnection conn)
        {
            try
            {
                var raw = (ITikRawSentenceConnection)conn;
                raw.CallCommandSync("/tool/sniffer/stop").ToList();
                Log("  sniffer: stopped");

                var restore = _snifferRestore;
                if (restore != null)
                {
                    _snifferRestore = null;
                    raw.CallCommandSync(new[] { "/tool/sniffer/set" }.Concat(restore)).ToList();
                    Log("  sniffer: settings restored");
                }
            }
            catch (Exception ex)
            {
                Log("  sniffer: could not stop — " + Oneline(ex.Message));
            }
        }

        /// <summary>
        /// Stops and restores the sniffer on a connection of its own, for the <c>finally</c> that has to run
        /// even when the arms threw — the connections they used are gone by then.
        /// </summary>
        private void StopSnifferBestEffort()
        {
            if (_snifferRestore == null)
                return;

            try
            {
                using (var conn = OpenProbeConnection())
                    StopSniffer(conn);
            }
            catch (Exception ex)
            {
                Log("  sniffer: could not be stopped on a fresh connection — " + Oneline(ex.Message));
            }
        }

        /// <summary>
        /// The captured packets, reported around the <b>longest silence</b> on each TCP session.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The silence is the subject, so the dump finds it rather than printing the last packets and hoping:
        /// by the time a stall has been diagnosed, the newest packets in the buffer are the diagnosis itself.
        /// The session with a ~30 s gap identifies itself, which also saves teaching this code which local
        /// port stalled.
        /// </para>
        /// <para>
        /// What each outcome means. <b>No packets at all in the gap</b> — the router transmitted nothing, so
        /// the bytes never left and the fault is on the router. <b>Retransmissions in the gap</b> — it sent
        /// and got no acknowledgement, so they were lost on the way. <b>A zero window</b> in the client's last
        /// acknowledgement — the router stopped because we told it to, and the fault is ours.
        /// </para>
        /// </remarks>
        private void LogSnifferTail(ITikConnection conn)
        {
            try
            {
                var rows = ((ITikRawSentenceConnection)conn)
                    .CallCommandSync("/tool/sniffer/packet/print").OfType<ITikReSentence>().ToList();

                if (rows.Count == 0)
                {
                    Log("  sniffer: no packets captured");
                    return;
                }

                var sessions = new Dictionary<string, List<Packet>>();
                foreach (var row in rows)
                {
                    var packet = Packet.From(row);
                    if (!sessions.ContainsKey(packet.Session))
                        sessions[packet.Session] = new List<Packet>();
                    sessions[packet.Session].Add(packet);
                }

                Log($"  sniffer: {rows.Count} packet(s) captured over "
                    + $"{rows[rows.Count - 1].GetResponseFieldOrDefault("time", "?")} s, "
                    + $"{sessions.Count} session(s)");

                foreach (var pair in sessions.OrderByDescending(x => MaxGap(x.Value)))
                {
                    var packets = pair.Value;
                    int at = GapIndex(packets);
                    double gap = at > 0 ? packets[at].Time - packets[at - 1].Time : 0;
                    int tx = packets.Count(x => x.Tx);

                    Log($"  sniffer: session {pair.Key}: {tx} tx (router->client), {packets.Count - tx} rx,"
                        + $" longest silence {gap:F1} s");

                    // Only the sessions that actually went quiet are worth their packets; a busy session's
                    // sub-second gaps are the normal request/response rhythm.
                    if (gap < 5 || at <= 0)
                        continue;

                    foreach (var packet in packets.Skip(Math.Max(0, at - 6)).Take(12))
                        Log("  sniffer:   " + (packet.Time >= packets[at].Time ? "AFTER  " : "before ")
                            + packet.Describe());
                }
            }
            catch (Exception ex)
            {
                Log("  sniffer: could not read packets — " + Oneline(ex.Message));
            }
        }

        private static double MaxGap(List<Packet> packets)
        {
            int at = GapIndex(packets);
            return at > 0 ? packets[at].Time - packets[at - 1].Time : 0;
        }

        private static int GapIndex(List<Packet> packets)
        {
            int at = 0;
            double worst = 0;
            for (int i = 1; i < packets.Count; i++)
                if (packets[i].Time - packets[i - 1].Time > worst)
                {
                    worst = packets[i].Time - packets[i - 1].Time;
                    at = i;
                }
            return at;
        }

        private sealed class Packet
        {
            public double Time;
            public bool Tx;
            public string Session;
            public string Flags;
            public string Size;
            public string Seq;
            public string Ack;
            public string Win;

            public static Packet From(ITikReSentence row)
            {
                bool tx = row.GetResponseFieldOrDefault("direction", "") == "tx";
                byte[] frame = ParseFrame(row.GetResponseFieldOrDefault("data", ""));
                double time;
                double.TryParse(row.GetResponseFieldOrDefault("time", "0"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out time);
                return new Packet
                {
                    Time = time,
                    Tx = tx,
                    // The client side of the pair names the session whichever way the packet went.
                    Session = tx ? row.GetResponseFieldOrDefault("dst-address", "?")
                                 : row.GetResponseFieldOrDefault("src-address", "?"),
                    Flags = row.GetResponseFieldOrDefault("tcp-flags", "?"),
                    Size = row.GetResponseFieldOrDefault("size", "?"),
                    Seq = Be32(frame, 38),
                    Ack = Be32(frame, 42),
                    Win = Be16(frame, 48),
                };
            }

            public string Describe() => string.Format(CultureInfo.InvariantCulture,
                "{0,9:F3} {1} {2,-12} {3,5}B seq={4} ack={5} win={6}",
                Time, Tx ? "router->client" : "client->router", Flags, Size, Seq, Ack, Win);
        }

        // The captured bytes come back as a hex dump with an ASCII column. Only the fixed-width hex half of
        // each line is taken, so nothing in the ASCII column can be mistaken for a byte.
        private static byte[] ParseFrame(string dump)
        {
            var bytes = new List<byte>();
            foreach (string line in (dump ?? "").Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon < 0)
                    continue;
                string hex = line.Substring(colon + 1);
                if (hex.Length > 49)
                    hex = hex.Substring(0, 49);
                foreach (string token in hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    byte b;
                    if (token.Length == 2 && byte.TryParse(token, NumberStyles.HexNumber,
                                                           CultureInfo.InvariantCulture, out b))
                        bytes.Add(b);
                }
            }
            return bytes.ToArray();
        }

        private static string Be32(byte[] f, int at) =>
            f.Length < at + 4 ? "?" : (((uint)f[at] << 24) | ((uint)f[at + 1] << 16)
                | ((uint)f[at + 2] << 8) | f[at + 3]).ToString(CultureInfo.InvariantCulture);

        private static string Be16(byte[] f, int at) =>
            f.Length < at + 2 ? "?" : ((f[at] << 8) | f[at + 1]).ToString(CultureInfo.InvariantCulture);

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
