// WinboxCliLatencyProbeTest.cs — where does a WinboxCliMac round trip actually spend its time? (P2.43)
//
// A full winboxclimac run takes ~1 h 22 m and ends with a handful of failures; the same CLI engine over
// TCP (winboxcli) is green in ~8 min. Two measurements from P2.50 narrowed the question and are what this
// probe exists to confirm and quantify:
//
//   * the FIRST output of a command arrives ~5 s after it is sent over the MAC channel, while the channel
//     then streams a row a second — so the cost is in starting, not in carrying;
//   * a *skipped* test costs ~6 s on this transport, i.e. per-test connection setup, not command
//     execution, is what fills the wall clock.
//
// Both are attributions made from the outside. This probe measures them from the inside, by reading the
// wire trace rather than a stopwatch around the public API, and splits one command into the four spans
// that can each have a different cause:
//
//     send -> first byte      the router (or the channel) taking its time to start answering
//     first byte -> prompt    carrying the output
//     prompt -> return        our own settle window (SettleMs, a constant — a control on the other three)
//
// The mepty terminal engine is shared by WinboxCli and WinboxCliMac and emits on ONE trace channel
// ("wbxcli.mepty"), so the two transports are directly comparable: same emit points, same code, only the
// channel underneath differs. WinboxNative / WinboxNativeMac are measured alongside as the control that
// separates "the MAC layer is slow" from "the mepty terminal over the MAC layer is slow" — they ride the
// same MAC transport with no terminal on top.
//
// Nothing here asserts a timing. A stopwatch assertion would need a different threshold per transport and
// per host; the output IS the result, and it goes to the console (plus TIK4NET_PROBE_LOG if set).
//
// Env:  TIK4NET_LATENCY_COMMANDS  commands per transport (default 8)
//       TIK4NET_PROBE_TRANSPORTS  comma-separated subset of WinboxCli,WinboxCliMac,WinboxNative,WinboxNativeMac
//       TIK4NET_PROBE_LOG         append the report to this file as well

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using tik4net.Diagnostics;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run these.
    [Ignore("Ad-hoc latency probe against a live router — comment out to run.")]
    [TestClass]
    public class WinboxCliLatencyProbeTest
    {
        private const int DefaultCommands = 8;

        /// <summary>The command under measurement: the smallest useful read, one record, on every transport.</summary>
        private const string ProbeCommand = "/system/identity/print";

        private static readonly TikConnectionType[] AllTransports =
        {
            TikConnectionType.WinboxCli,
            TikConnectionType.WinboxCliMac,
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

        // ── Trace capture ─────────────────────────────────────────────────────

        /// <summary>One wire event, timestamped against a caller-controlled origin.</summary>
        private struct Event
        {
            public double Ms;        // since the current origin
            public string Channel;
            public TikWireDir Dir;
            public int Count;
            public string Note;
        }

        /// <summary>
        /// Records every event with a millisecond offset from a resettable origin, so a span can be taken
        /// across the exact moment a command was issued rather than across a whole connection.
        /// </summary>
        private sealed class TimelineSink : ITikWireTraceSink
        {
            private readonly object _lock = new object();
            private readonly List<Event> _events = new List<Event>();
            private Stopwatch _clock = Stopwatch.StartNew();

            public void Reset()
            {
                lock (_lock) { _events.Clear(); _clock = Stopwatch.StartNew(); }
            }

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                lock (_lock)
                    _events.Add(new Event
                    {
                        Ms = _clock.Elapsed.TotalMilliseconds,
                        Channel = channel, Dir = dir, Count = count, Note = note,
                    });
            }

            public Event[] Snapshot()
            {
                lock (_lock) return _events.ToArray();
            }
        }

        // ── Connection helpers ────────────────────────────────────────────────

        private static ITikConnection Open(TikConnectionType type)
        {
            var conn = ConnectionFactory.CreateConnection(type);
            string mac = ConfigurationManager.AppSettings["routerMac"];
            if (!string.IsNullOrEmpty(mac) && conn is ITikMacLayerConnection macConn)
                macConn.RouterMac = mac;
            conn.Open(ConfigurationManager.AppSettings["host"],
                      ConfigurationManager.AppSettings["user"],
                      ConfigurationManager.AppSettings["pass"] ?? "");
            return conn;
        }

        private static bool IsMac(TikConnectionType t)
            => t == TikConnectionType.WinboxCliMac || t == TikConnectionType.WinboxNativeMac;

        private static bool IsCli(TikConnectionType t)
            => t == TikConnectionType.WinboxCli || t == TikConnectionType.WinboxCliMac;

        /// <summary>The channel that carries the actual request/reply bytes for this transport.</summary>
        private static string DataChannel(TikConnectionType t)
            => IsCli(t) ? "wbxcli.mepty" : (IsMac(t) ? "wbxmac.udp" : "wbxtcp.frame");

        // ── The probe ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Probe_FirstByteLatency_CliVsCliMac()
        {
            int commands = int.TryParse(
                Environment.GetEnvironmentVariable("TIK4NET_LATENCY_COMMANDS"), out int n) && n > 0
                ? n : DefaultCommands;

            string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS");
            var selected = string.IsNullOrEmpty(only)
                ? AllTransports
                : AllTransports.Where(t => only.Split(',').Any(
                    s => string.Equals(s.Trim(), t.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();

            var sink = new TimelineSink();
            using (TikWireTrace.Capture(sink))
            {
                foreach (var type in selected)
                {
                    Log("");
                    Log("══ " + type + " ══════════════════════════════════════════════");
                    try { MeasureOne(type, commands, sink); }
                    catch (Exception ex)
                    {
                        Log("  FAILED: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0].Trim());
                    }
                }
            }
        }

        private static void MeasureOne(TikConnectionType type, int commands, TimelineSink sink)
        {
            string dataChannel = DataChannel(type);

            // ── Phase A: open. This is the "a skipped test costs 6 s" number — per-test connection setup.
            sink.Reset();
            var swOpen = Stopwatch.StartNew();
            ITikConnection conn = Open(type);
            swOpen.Stop();

            var openEvents = sink.Snapshot();
            Log($"  open: {swOpen.ElapsedMilliseconds} ms, {openEvents.Length} wire events");
            foreach (string line in SummariseOpen(openEvents, dataChannel))
                Log("    " + line);

            try
            {
                var sendToFirst = new List<double>();
                var firstToPrompt = new List<double>();
                var promptToReturn = new List<double>();
                var totals = new List<double>();

                for (int i = 0; i < commands; i++)
                {
                    sink.Reset();
                    var sw = Stopwatch.StartNew();
                    int rows = conn.CreateCommand(ProbeCommand).ExecuteList().Count();
                    sw.Stop();

                    var ev = sink.Snapshot();
                    Span span = Measure(ev, dataChannel, IsCli(type));

                    totals.Add(sw.Elapsed.TotalMilliseconds);
                    if (span.SendToFirstByte  >= 0) sendToFirst.Add(span.SendToFirstByte);
                    if (span.FirstByteToPrompt >= 0) firstToPrompt.Add(span.FirstByteToPrompt);
                    if (span.PromptToReturn   >= 0) promptToReturn.Add(span.PromptToReturn);

                    if (i == 0 && Environment.GetEnvironmentVariable("TIK4NET_LATENCY_DUMP") == "1")
                        foreach (var e in ev)
                            Log($"      {e.Ms,8:F1} ms {e.Channel,-14} {e.Dir,-4} {e.Count,4}B {e.Note}");

                    Log($"  #{i + 1,-2} total {sw.ElapsedMilliseconds,6} ms | " +
                        $"send->1st {Fmt(span.SendToFirstByte),8} | " +
                        $"1st->prompt {Fmt(span.FirstByteToPrompt),8} | " +
                        $"prompt->ret {Fmt(span.PromptToReturn),8} | " +
                        $"recv {span.RecvFrames}f/{span.RecvBytes}B, pulls {span.Pulls}, rows {rows}");
                }

                Log("");
                Log($"  {type}: {commands} x {ProbeCommand}");
                Log("    total        " + Stats(totals));
                Log("    send->1st    " + Stats(sendToFirst));
                Log("    1st->prompt  " + Stats(firstToPrompt));
                Log("    prompt->ret  " + Stats(promptToReturn));
            }
            finally
            {
                var swClose = Stopwatch.StartNew();
                conn.Dispose();
                swClose.Stop();
                Log($"    close        {swClose.ElapsedMilliseconds} ms");
            }
        }

        private struct Span
        {
            public double SendToFirstByte;    // -1 when not observable
            public double FirstByteToPrompt;
            public double PromptToReturn;
            public int RecvFrames;
            public int RecvBytes;
            public int Pulls;
        }

        /// <summary>
        /// Splits one command's timeline. The anchor is the LAST Send carrying payload before the first
        /// reply — for the CLI transports that is the keystroke frame (an empty-payload PULL is a Note, so
        /// it cannot be mistaken for the command), for native it is the request frame.
        /// </summary>
        private static Span Measure(Event[] events, string dataChannel, bool cli)
        {
            var span = new Span { SendToFirstByte = -1, FirstByteToPrompt = -1, PromptToReturn = -1 };

            var onChannel = events.Where(e => e.Channel == dataChannel).ToArray();
            span.Pulls = onChannel.Count(e => e.Dir == TikWireDir.Send && e.Note != null
                                              && e.Note.StartsWith("PULL", StringComparison.Ordinal));

            Event? send = onChannel.Where(e => e.Dir == TikWireDir.Send && e.Count > 0)
                                   .Cast<Event?>().FirstOrDefault();
            if (send == null) return span;

            var recvs = onChannel.Where(e => e.Dir == TikWireDir.Recv && e.Ms >= send.Value.Ms).ToArray();
            span.RecvFrames = recvs.Length;
            span.RecvBytes  = recvs.Sum(e => e.Count);
            if (recvs.Length == 0) return span;

            span.SendToFirstByte = recvs[0].Ms - send.Value.Ms;

            if (cli)
            {
                Event? prompt = onChannel.Where(e => e.Dir == TikWireDir.Note && e.Note != null
                                                     && e.Note.StartsWith("prompt seen", StringComparison.Ordinal))
                                         .Cast<Event?>().FirstOrDefault();
                Event? settled = onChannel.Where(e => e.Dir == TikWireDir.Note && e.Note != null
                                                      && e.Note.StartsWith("settled", StringComparison.Ordinal))
                                          .Cast<Event?>().FirstOrDefault();
                if (prompt != null)
                {
                    span.FirstByteToPrompt = prompt.Value.Ms - recvs[0].Ms;
                    if (settled != null) span.PromptToReturn = settled.Value.Ms - prompt.Value.Ms;
                }
            }
            else
            {
                span.FirstByteToPrompt = recvs[recvs.Length - 1].Ms - recvs[0].Ms;
            }

            return span;
        }

        /// <summary>
        /// Names the milestones of an open, so a slow one can be attributed to a phase rather than to the
        /// whole. Deliberately generic (first/last event on each channel) — the phases differ per transport
        /// and hard-coding them would encode today's handshake shape into a diagnostic.
        /// </summary>
        private static IEnumerable<string> SummariseOpen(Event[] events, string dataChannel)
        {
            foreach (var group in events.GroupBy(e => e.Channel).OrderBy(g => g.Min(e => e.Ms)))
            {
                var first = group.OrderBy(e => e.Ms).First();
                var last  = group.OrderBy(e => e.Ms).Last();
                yield return $"{group.Key,-14} {group.Count(),4} events, " +
                             $"{first.Ms,8:F0} .. {last.Ms,8:F0} ms";
            }

            // The single most useful open-phase number: how long after the FIRST byte we sent did the
            // router's first reply arrive. On the MAC transports that includes MNDP/SESSIONSTART.
            var send = events.Where(e => e.Dir == TikWireDir.Send).OrderBy(e => e.Ms).ToArray();
            var recv = events.Where(e => e.Channel == dataChannel && e.Dir == TikWireDir.Recv)
                             .OrderBy(e => e.Ms).ToArray();
            if (send.Length > 0 && recv.Length > 0)
                yield return $"first send {send[0].Ms:F0} ms -> first reply {recv[0].Ms:F0} ms " +
                             $"(gap {recv[0].Ms - send[0].Ms:F0} ms)";
        }

        private static string Fmt(double ms) => ms < 0 ? "-" : ms.ToString("F0") + " ms";

        private static string Stats(List<double> values)
        {
            if (values.Count == 0) return "no samples";
            var sorted = values.OrderBy(v => v).ToArray();
            return $"n={sorted.Length,-3} min {sorted[0],7:F0} | med {sorted[sorted.Length / 2],7:F0} | " +
                   $"max {sorted[sorted.Length - 1],7:F0} | avg {values.Average(),7:F0}  (ms)";
        }
    }
}
