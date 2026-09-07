// MangleNativeLegProbe.cs — step 1 of the WinBox M2 paging-stall trace plan: do BOTH native legs stall?
//
// The stall under investigation: /ip/firewall/mangle with a few thousand rules over WinboxNative (TCP 8291)
// delivers ~12 pages at ~120 ms/page and then gets nothing for ~28.5 s, and the read fails. The candidates
// the shipping code cannot tell apart split into carrier-specific ones (our reader wedged mid-frame in
// WinboxTcpTransport.ReadExact with an infinite socket deadline) and carrier-neutral ones (the router
// answered nothing — stale continuation cursor, or the autorefresh stats bit on a blocking window).
//
// The two legs share every line of the M2 code and differ only in the carrier, so reading the SAME table
// over both discriminates: if MAC also stalls, the carrier-specific candidates die; if only TCP does, they
// survive. That is the whole purpose here — cheapest discriminator, run before any byte tracing.
//
// Attempts are INTERLEAVED (TCP, MAC, TCP, MAC, …) rather than grouped, because the stall is intermittent
// and router load drifts: grouping would let a quiet minute be read as "MAC is fine".
//
// Page-level timing comes from OnReadRow, which the native transport raises once per M2 reply frame — i.e.
// once per page of a getall — so the gap series IS the paging cadence, and the handler only stamps a clock.
//
// Nothing is asserted about timing. A stall is reported as the exception it produced; the output is the
// result.
//
// A third method (Probe_MangleGetAll_FlagsAb) covers step 3 of the same plan: with the stall attributed to
// the router, the remaining question is whether the request's own content provokes it, so it A/Bs the one
// bit that differs between our getall and webfig's — which reads the same table in ~1 s without stalling.
//
// Env:  TIK_PROBE=1               required, or the test reports Inconclusive
//       TIK4NET_MANGLE_ATTEMPTS   attempts per leg (default 3)
//       TIK4NET_PROBE_TRANSPORTS  comma-separated subset of WinboxNative,WinboxNativeMac
//       TIK4NET_PROBE_LOG         append the report to this file as well

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using tik4net.Diagnostics;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Winbox;

namespace tik4net.integrationtests
{
    // Gated on TIK_PROBE=1 rather than [Ignore], so it can be run from the command line without editing
    // the file (MSTest skips [Ignore] even under --filter) and a normal suite run still never pays for it.
    [TestClass]
    public class MangleNativeLegProbe
    {
        private const int DefaultAttempts = 3;

        /// <summary>Gaps at or above this are printed individually; below it they only feed the summary.</summary>
        private const double InterestingGapMs = 500;

        // Flags A/B arm (Probe_MangleGetAll_FlagsAb) — it drives the raw M2 client, so it owns these.
        private const int WinboxPort = 8291;

        /// <summary>A page gap at or above this is the stall under investigation, not slow paging.</summary>
        private const double StallGapMs = 5000;

        /// <summary>Per-page receive bound. Above the ~30 s the stall recovers under, so a pause is measured
        /// rather than turned into a timeout — the arm has to report the gap, not just that it failed.</summary>
        private const int ArmTimeoutMs = 60000;

        /// <summary>Cursor-loop cap. 1672 rows arrive in 14 pages, so this is slack, not a page budget.</summary>
        private const int MaxRounds = 128;

        /// <summary>Per-page deadline in the retry arm — ~8× the ~300 ms baseline page, so a normal page can
        /// never trip it and a stall trips it in well under a second.</summary>
        private const int RetryDeadlineMs = 2500;

        /// <summary>Re-sends of one page's continuation before the arm gives up.</summary>
        private const int MaxRetriesPerPage = 3;

        private static readonly TikConnectionType[] AllLegs =
        {
            TikConnectionType.WinboxNative,
            TikConnectionType.WinboxNativeMac,
        };

        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        [TestMethod]
        public void Probe_MangleRead_BothNativeLegs()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1 and point it at a router carrying "
                                    + "a few thousand mangle rules.");

            int attempts = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_MANGLE_ATTEMPTS"), out int n) && n > 0
                ? n : DefaultAttempts;

            string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS");
            var legs = string.IsNullOrEmpty(only)
                ? AllLegs
                : AllLegs.Where(t => only.Split(',').Any(
                    s => string.Equals(s.Trim(), t.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();

            Log("");
            Log($"══ mangle read, both native legs — {attempts} interleaved attempt(s) per leg ══");

            for (int attempt = 1; attempt <= attempts; attempt++)
                foreach (var leg in legs)
                {
                    try { MeasureOne(leg, attempt); }
                    catch (Exception ex)
                    {
                        // Setup failure (login, MNDP discovery, mac-server not enabled) — not a stall.
                        Log($"  {leg,-16} attempt {attempt}: SETUP FAILED {ex.GetType().Name}: {ex.Message}");
                    }
                }
        }

        /// <summary>
        /// The decisive measurement: the same read over the TCP leg only, with the socket-level trace on, so
        /// the largest gap can be attributed. A gap spanned by <b>no</b> <c>wbxtcp.sock</c> event at all is a
        /// router that sent nothing while we sat blocked in a read; a gap that starts with a short
        /// <c>got=</c> is a frame arriving in pieces, i.e. our reader parked mid-frame with the socket
        /// deadline effectively infinite.
        /// </summary>
        /// <remarks>
        /// <c>wbxtcp.frame</c> alone cannot answer this — it is emitted only after a frame is fully
        /// assembled. Both channels are captured so the last completed frame before the gap is visible next
        /// to the socket reads that followed it.
        /// </remarks>
        [TestMethod]
        public void Probe_MangleRead_TcpSocketLevel()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1 and point it at a router carrying "
                                    + "a few thousand mangle rules.");

            int attempts = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_MANGLE_ATTEMPTS"), out int n) && n > 0
                ? n : DefaultAttempts;

            Log("");
            Log($"══ mangle read, WinboxNative TCP, socket-level trace — {attempts} attempt(s) ══");

            var sink = new SocketTimelineSink();
            using (TikWireTrace.Capture(sink))
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    sink.Reset();
                    string outcome;
                    int rows = 0;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using (ITikConnection connection = ConnectionFactory.CreateConnection(
                                   TikConnectionType.WinboxNative))
                        {
                            connection.Open(ConfigurationManager.AppSettings["host"],
                                            ConfigurationManager.AppSettings["user"],
                                            ConfigurationManager.AppSettings["pass"] ?? "");
                            // Login and the catalog fetch use the same socket; start the series at the read
                            // under test so their events cannot own the largest gap.
                            sink.Reset();
                            sw.Restart();
                            rows = connection.LoadAll<FirewallMangle>().Count();
                            outcome = "OK";
                        }
                    }
                    catch (Exception ex)
                    {
                        outcome = "STALL " + ex.GetType().Name + ": " + Oneline(ex.Message);
                    }

                    ReportSocketTimeline(attempt, outcome, rows, sw.ElapsedMilliseconds, sink.Snapshot());
                }
        }

        /// <summary>
        /// Step 3 of the trace plan: does the <b>content</b> of the request provoke the pause? The two arms
        /// differ in one bit of <c>ufe000c</c> — <c>0x10000007</c> is what the shipping mangle read sends
        /// (the autorefresh stats bit OR'd in at <c>WinboxNativeConnection.cs:609-610</c> so getall returns
        /// bytes/packets), <c>0x10000005</c> is what webfig sends and what reads the same table in about a
        /// second without stalling.
        /// </summary>
        /// <remarks>
        /// Driven through <see cref="WinboxM2Client"/>'s raw getall rather than <c>LoadAll</c>, because the
        /// flags are decided inside the connection and the whole point is to vary them. The arms are
        /// interleaved for the same reason the legs are, and each attempt gets a fresh login: at ~1 stall in
        /// 8 reads, an arm that happens to run during a quiet minute would otherwise look like the fix.
        /// </remarks>
        [TestMethod]
        public void Probe_MangleGetAll_FlagsAb()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1 and point it at a router carrying "
                                    + "a few thousand mangle rules.");

            int attempts = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_MANGLE_ATTEMPTS"), out int n) && n > 0
                ? n : DefaultAttempts;

            Log("");
            Log($"══ mangle getall, flags A/B — {attempts} interleaved attempt(s) per arm ══");
            Log("   0x10000007 = shipping (autorefresh stats bit) · 0x10000005 = webfig / GetAllFlags");

            int stalls7 = 0, stalls5 = 0;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (RunFlagArm(attempt, 0x10000007)) stalls7++;
                if (RunFlagArm(attempt, 0x10000005)) stalls5++;
            }

            Log($"   totals: 0x10000007 stalled {stalls7}/{attempts} · 0x10000005 stalled {stalls5}/{attempts}");
        }

        /// <summary>
        /// One full paged getall of the mangle table with <paramref name="flags"/>. Returns true when a page
        /// took at least <see cref="StallGapMs"/> — the marker the earlier steps established for this stall
        /// (~20–30 s, against a ~120–150 ms/page baseline), so "stalled" is not a judgement call.
        /// </summary>
        private static bool RunFlagArm(int attempt, int flags)
        {
            int[] handler = { 20, 7 };          // .jg: /ip/firewall/mangle
            var pages = new List<int>();
            var gaps = new List<double>();
            int rows = 0, status = 0;
            string error = null;

            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new WinboxM2Client())
                {
                    client.Connect(ConfigurationManager.AppSettings["host"], WinboxPort);
                    client.Authenticate(ConfigurationManager.AppSettings["host"], WinboxPort,
                                        ConfigurationManager.AppSettings["user"],
                                        ConfigurationManager.AppSettings["pass"] ?? "");

                    object cont = null;
                    byte reqId = 1;
                    sw.Restart();
                    double last = 0;

                    for (int round = 0; round < MaxRounds; round++)
                    {
                        var head = new List<byte[]>
                        {
                            M2Message.SysToArr(handler), M2Message.SysFrom(),
                            M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                            M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, reqId++),
                            M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                            M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, flags),
                        };
                        if (cont != null)
                            head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation,
                                                      Convert.ToInt32(cont)));

                        client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                        byte[] resp = client.RecvAndDecryptPublic(ArmTimeoutMs);

                        double now = sw.Elapsed.TotalMilliseconds;
                        gaps.Add(now - last);
                        last = now;

                        status = M2Message.ParseSysStatus(resp);
                        if (status != WinboxM2Protocol.Error.None
                            && status != WinboxM2Protocol.Error.ObjectNonexistent)
                        {
                            error = $"status 0x{status:X}";
                            break;
                        }

                        int page = M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records).Count;
                        pages.Add(page);
                        rows += page;

                        if (status == WinboxM2Protocol.Error.ObjectNonexistent) break;

                        var fields = M2Message.ParseAllFields(resp);
                        if (!fields.TryGetValue(WinboxM2Protocol.RecordKey.Continuation, out var next)) break;
                        cont = next.Item2;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Oneline(ex.Message);
            }

            double maxGap = gaps.Count == 0 ? 0 : gaps.Max();
            bool stalled = maxGap >= StallGapMs || error != null;

            Log($"  0x{flags:X8} attempt {attempt}: {(stalled ? "STALL" : "OK")}"
                + (error == null ? "" : " " + error));
            Log($"      rows={rows} pages={pages.Count} read={sw.ElapsedMilliseconds}ms maxGap={maxGap:F0}ms");
            for (int i = 0; i < gaps.Count; i++)
                if (gaps[i] >= InterestingGapMs)
                    Log($"      gap before page #{i + 1}: {gaps[i]:F0}ms");

            return stalled;
        }

        /// <summary>
        /// Does a short per-page deadline plus a re-sent continuation get the page? The stall is the router
        /// declining to answer a request it received, so the question is whether asking again from the same
        /// cursor is answered promptly — the cheap client-side mitigation — or whether the handler is busy and
        /// the retry queues behind the same silence.
        /// </summary>
        /// <remarks>
        /// The retry carries a <b>new</b> request id and the <b>same</b> continuation, which is the shape any
        /// real implementation would have to use: the abandoned request's reply may still arrive, and it is
        /// only safely discardable while its id is not back in circulation (defect S-1). It is also only safe
        /// at all because the abandoned read consumed nothing — the deadline fires at a clean frame boundary
        /// with an empty buffer (step 4), so the stream cipher's keystream stays aligned. A frame arriving
        /// with an unexpected id is reported rather than skipped silently, because that is the case where the
        /// mitigation would be unsound.
        /// </remarks>
        [TestMethod]
        public void Probe_MangleGetAll_ShortDeadlineAndRetry()
        {
            if (Environment.GetEnvironmentVariable("TIK_PROBE") != "1")
                Assert.Inconclusive("Diagnostic harness; set TIK_PROBE=1 and point it at a router carrying "
                                    + "a few thousand mangle rules.");

            int attempts = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_MANGLE_ATTEMPTS"), out int n) && n > 0
                ? n : DefaultAttempts;

            Log("");
            Log($"══ mangle getall, {RetryDeadlineMs} ms per page + re-sent continuation — {attempts} attempt(s) ══");

            int stalled = 0, rescued = 0;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                var r = RunRetryArm(attempt);
                if (r.Retries > 0) stalled++;
                if (r.Retries > 0 && r.Rows > 0 && r.Error == null) rescued++;
            }

            Log($"   totals: {stalled}/{attempts} attempt(s) needed a retry, {rescued} of those completed the table");
        }

        private struct RetryResult { public int Rows; public int Retries; public string Error; }

        /// <summary>
        /// One paged getall with a short per-page deadline: on a timeout the same continuation is re-sent
        /// under a fresh request id, up to <see cref="MaxRetriesPerPage"/> times per page.
        /// </summary>
        private static RetryResult RunRetryArm(int attempt)
        {
            int[] handler = { 20, 7 };          // .jg: /ip/firewall/mangle
            int rows = 0, pages = 0, retries = 0;
            string error = null;
            var sw = Stopwatch.StartNew();

            try
            {
                using (var client = new WinboxM2Client())
                {
                    client.Connect(ConfigurationManager.AppSettings["host"], WinboxPort);
                    client.Authenticate(ConfigurationManager.AppSettings["host"], WinboxPort,
                                        ConfigurationManager.AppSettings["user"],
                                        ConfigurationManager.AppSettings["pass"] ?? "");

                    object cont = null;
                    byte reqId = 1;
                    sw.Restart();

                    for (int round = 0; round < MaxRounds; round++)
                    {
                        byte[] resp = null;
                        double pageStart = sw.Elapsed.TotalMilliseconds;

                        for (int tryNo = 0; tryNo <= MaxRetriesPerPage && resp == null; tryNo++)
                        {
                            byte id = reqId++;
                            client.EncryptAndSendPublic(BuildGetAll(handler, id, cont));
                            try
                            {
                                byte[] frame = client.RecvAndDecryptPublic(RetryDeadlineMs);
                                int? gotId = ReqIdOf(frame);
                                if (gotId != id)
                                {
                                    // The abandoned request answered late. Real code would drop it by id and
                                    // keep waiting; here it is the finding, so say so and take it.
                                    Log($"      page #{pages + 1}: frame for id {gotId} while waiting on {id} "
                                        + "— a late reply to an abandoned request");
                                }
                                resp = frame;
                            }
                            catch (Exception ex) when (ex is System.IO.IOException
                                                       || ex is System.Net.Sockets.SocketException)
                            {
                                if (tryNo == MaxRetriesPerPage) throw;
                                retries++;
                                Log($"      page #{pages + 1}: no reply within {RetryDeadlineMs} ms "
                                    + $"(id {id}) — re-sending the same continuation as id {reqId}");
                            }
                        }

                        int status = M2Message.ParseSysStatus(resp);
                        if (status != WinboxM2Protocol.Error.None
                            && status != WinboxM2Protocol.Error.ObjectNonexistent)
                        {
                            error = $"status 0x{status:X}";
                            break;
                        }

                        int page = M2Message.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records).Count;
                        pages++;
                        rows += page;
                        double pageMs = sw.Elapsed.TotalMilliseconds - pageStart;
                        if (pageMs >= InterestingGapMs)
                            Log($"      page #{pages} took {pageMs:F0}ms");

                        if (status == WinboxM2Protocol.Error.ObjectNonexistent) break;

                        var fields = M2Message.ParseAllFields(resp);
                        if (!fields.TryGetValue(WinboxM2Protocol.RecordKey.Continuation, out var next)) break;
                        cont = next.Item2;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Oneline(ex.Message);
            }

            Log($"  attempt {attempt}: {(error == null ? "OK" : "FAILED " + error)}");
            Log($"      rows={rows} pages={pages} retries={retries} read={sw.ElapsedMilliseconds}ms");

            return new RetryResult { Rows = rows, Retries = retries, Error = error };
        }

        /// <summary>The request id a reply frame carries, or null when it has none.</summary>
        private static int? ReqIdOf(byte[] m2)
            => M2Message.ParseAllFields(m2).TryGetValue(WinboxM2Protocol.SysKey.RequestId, out var v)
                ? (int?)Convert.ToInt32(v.Item2) : null;

        /// <summary>A getall request for one page, with an explicit request id and continuation cursor.</summary>
        private static byte[] BuildGetAll(int[] handler, byte reqId, object cont)
        {
            var head = new List<byte[]>
            {
                M2Message.SysToArr(handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, reqId),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags),
            };
            if (cont != null)
                head.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, Convert.ToInt32(cont)));
            return M2Message.BuildM2(head.ToArray());
        }

        /// <summary>
        /// Prints the read's socket-level shape and a window around its largest gap. The window is what the
        /// diagnosis is read from; the counts are there to show the window is representative.
        /// </summary>
        private static void ReportSocketTimeline(int attempt, string outcome, int rows, long elapsedMs,
                                                 SocketEvent[] events)
        {
            const int Window = 6;

            Log($"  attempt {attempt}: {outcome}");

            var sock = events.Where(e => e.Channel == "wbxtcp.sock").ToArray();
            int frames = events.Count(e => e.Channel == "wbxtcp.frame" && e.Dir == TikWireDir.Recv);
            int reads = sock.Count(e => e.Dir == TikWireDir.Recv);

            Log($"      rows={rows} read={elapsedMs}ms framesRecv={frames} socketReads={reads} "
                + $"socketEvents={sock.Length}");

            if (sock.Length < 2) { Log("      (no socket events — nothing to attribute)"); return; }

            int worst = 0;
            double worstGap = 0;
            for (int i = 1; i < sock.Length; i++)
            {
                double gap = sock[i].Ms - sock[i - 1].Ms;
                if (gap > worstGap) { worstGap = gap; worst = i; }
            }

            // The tail matters as much as the middle: when the read ends in a timeout the gap that killed it
            // is not between two events, it is after the last one.
            double tailGap = elapsedMs - sock[sock.Length - 1].Ms;

            Log($"      largest gap between socket events: {worstGap:F0}ms (before event #{worst + 1} of "
                + $"{sock.Length}); after the last event: {tailGap:F0}ms");

            LogWindow("window around the largest gap", sock, worst - Window, worst + Window);

            // The tail is where a timeout's evidence lives, and it has to be read in full: a trailing
            // "read want=N" with no Recv after it means nothing arrived, whereas a Recv short of its want
            // would mean a frame was already half in our buffer. One line cannot distinguish those.
            // Both channels, because the outgoing request has to be visible next to the silence that
            // followed it — taking "the request went out" from the multiplexer's own bookkeeping is the
            // mistake V-7 is about.
            LogWindow("tail, both channels", events, events.Length - 1 - 2 * Window, events.Length - 1);
        }

        private static void LogWindow(string label, SocketEvent[] sock, int from, int to)
        {
            from = Math.Max(0, from);
            to = Math.Min(sock.Length - 1, to);
            Log($"      ── {label}: events {from + 1}..{to + 1} of {sock.Length} ──");
            for (int i = from; i <= to; i++)
            {
                double gap = i == 0 ? 0 : sock[i].Ms - sock[i - 1].Ms;
                Log($"        #{i + 1,-5} {sock[i].Ms,9:F1}ms  +{gap,8:F1}ms  {sock[i].Channel,-13} "
                    + $"{sock[i].Dir,-4} {sock[i].Count,6}B  {sock[i].Note}");
            }
        }

        /// <summary>One socket-level wire event, reduced to what the attribution needs.</summary>
        private struct SocketEvent
        {
            public double Ms;
            public string Channel;
            public TikWireDir Dir;
            public int Count;
            public string Note;
        }

        /// <summary>
        /// Timestamps events against a resettable origin and keeps no payload. Both matter: a full mangle
        /// read produces tens of thousands of socket events, and rendering their bytes would make the probe
        /// slow enough to change what it measures.
        /// </summary>
        private sealed class SocketTimelineSink : ITikWireTraceSink
        {
            private const int MaxEvents = 400000;   // hard ceiling, so a runaway read cannot exhaust memory

            private readonly object _lock = new object();
            private readonly List<SocketEvent> _events = new List<SocketEvent>();
            private Stopwatch _clock = Stopwatch.StartNew();

            public void Reset()
            {
                lock (_lock) { _events.Clear(); _clock = Stopwatch.StartNew(); }
            }

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                lock (_lock)
                {
                    if (_events.Count >= MaxEvents) return;
                    _events.Add(new SocketEvent
                    {
                        Ms = _clock.Elapsed.TotalMilliseconds,
                        Channel = channel, Dir = dir, Count = count, Note = note,
                    });
                }
            }

            public SocketEvent[] Snapshot()
            {
                lock (_lock) return _events.ToArray();
            }
        }

        /// <summary>
        /// One full read of the table over one leg, on a connection of its own so no earlier exchange can
        /// carry state into it.
        /// </summary>
        private static void MeasureOne(TikConnectionType leg, int attempt)
        {
            var pageTicks = new List<long>(4096);
            var sw = Stopwatch.StartNew();
            long lastTick = 0;

            using (ITikConnection connection = ConnectionFactory.CreateConnection(leg))
            {
                string mac = ConfigurationManager.AppSettings["routerMac"];
                if (!string.IsNullOrEmpty(mac) && connection is ITikMacLayerConnection macConn)
                    macConn.RouterMac = mac;

                connection.OnReadRow += (s, e) =>
                {
                    long now = sw.ElapsedTicks;
                    pageTicks.Add(now - lastTick);
                    lastTick = now;
                };
                connection.Open(ConfigurationManager.AppSettings["host"],
                                ConfigurationManager.AppSettings["user"],
                                ConfigurationManager.AppSettings["pass"] ?? "");

                long openedAtMs = sw.ElapsedMilliseconds;
                // The catalog fetch and login also raise OnReadRow; drop what they logged so the gap series
                // starts at the first page of the read under test.
                pageTicks.Clear();
                lastTick = sw.ElapsedTicks;

                string outcome;
                int rows = 0;
                try
                {
                    rows = connection.LoadAll<FirewallMangle>().Count();
                    outcome = "OK";
                }
                catch (Exception ex)
                {
                    outcome = "STALL " + ex.GetType().Name + ": " + Oneline(ex.Message);
                }

                Report(leg, attempt, outcome, rows, openedAtMs, sw, pageTicks);
            }
        }

        private static void Report(TikConnectionType leg, int attempt, string outcome, int rows,
                                   long openedAtMs, Stopwatch sw, List<long> pageTicks)
        {
            double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

            var gaps = pageTicks.Select(Ms).ToList();
            double median = 0;
            if (gaps.Count > 0)
            {
                var sorted = gaps.OrderBy(g => g).ToList();
                median = sorted[sorted.Count / 2];
            }

            Log($"  {leg,-16} attempt {attempt}: {outcome}");
            Log($"      rows={rows} replies={gaps.Count} open={openedAtMs}ms read={sw.ElapsedMilliseconds - openedAtMs}ms "
                + $"medianGap={median:F0}ms maxGap={(gaps.Count == 0 ? 0 : gaps.Max()):F0}ms");

            // Where the cadence broke, if it did. The shape matters as much as the maximum: full speed then
            // a cliff is a different story from a gradual slide.
            for (int i = 0; i < gaps.Count; i++)
                if (gaps[i] >= InterestingGapMs)
                    Log($"      gap before reply #{i + 1}: {gaps[i]:F0}ms");
        }

        private static string Oneline(string text)
            => text == null ? "" : text.Replace("\r", " ").Replace("\n", " ");
    }
}
