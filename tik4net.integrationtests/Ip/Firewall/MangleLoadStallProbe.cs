using System;
using System.Configuration;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Field report probe: <c>LoadAll&lt;FirewallMangle&gt;()</c> over a few thousand rules intermittently
    /// fails with <c>No response received … within 30000 ms</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReceiveTimeout</c> bounds the wait for the <b>next sentence</b>, not the whole command
    /// (<c>ApiReceiveTimeoutIsPerSentenceTests</c> pins that router-free), so the failure means a gap with
    /// nothing arriving at all — not "the read took too long". This measures where that gap is, and how big
    /// the largest one gets on a healthy run.
    /// </para>
    /// <para>
    /// The <c>OnReadRow</c> handler records a timestamp into a pre-sized array and does nothing else. That
    /// matters twice over: per-word handlers run <b>on the reader thread</b>, so an expensive one would stall
    /// delivery for every tag on the connection and produce exactly the symptom being investigated — and a
    /// probe that used a logging handler would therefore be measuring itself.
    /// </para>
    /// <para>
    /// Connected the way the reporting application does — <c>ConnectionFactory.CreateConnection</c> plus
    /// <c>Open</c>, no <c>TikConnectionSetup</c> — so the defaults under test are the ones it gets.
    /// </para>
    /// </remarks>
    [TestClass]
    public class MangleLoadStallProbe : TestBase
    {
        private const int Attempts = 8;
        private const int ReceiveTimeoutMs = 10000;

        [TestMethod]
        [Ignore] // manual probe: needs a router carrying a few thousand mangle rules
        public void MeasureMangleLoadGaps()
        {
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                var gapTicks = new List<long>(200000);
                var sw = Stopwatch.StartNew();
                long lastTick = 0;

                using (ITikConnection connection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
                {
                    connection.ReceiveTimeout = ReceiveTimeoutMs;
                    connection.OnReadRow += (s, e) =>
                    {
                        long now = sw.ElapsedTicks;
                        gapTicks.Add(now - lastTick);
                        lastTick = now;
                    };
                    connection.Open(ConfigurationManager.AppSettings["host"],
                                    ConfigurationManager.AppSettings["user"],
                                    ConfigurationManager.AppSettings["pass"] ?? "");

                    long openedAt = sw.ElapsedMilliseconds;
                    try
                    {
                        var rows = connection.LoadAll<FirewallMangle>().ToList();
                        Report(attempt, "OK", rows.Count, sw, openedAt, gapTicks, null);
                    }
                    catch (TikConnectionReceiveTimeoutException ex)
                    {
                        int partialRows = ex.PartialResponse == null
                            ? 0
                            : ex.PartialResponse.Split('\n').Length;
                        Report(attempt, "TIMEOUT", partialRows, sw, openedAt, gapTicks, ex);
                        // THE question: was the data sitting in our socket unread (our bug), or did the
                        // router genuinely send nothing (its side)? Asked at the moment of the timeout,
                        // because a byte that arrives afterwards answers a different question.
                        Console.WriteLine("            socket at timeout: " + DescribeSocket(connection));
                        Assert.Fail($"attempt {attempt} reproduced the stall after {partialRows} sentence(s): "
                                    + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Bytes pending in the receive buffer, and the reader task's state, read straight off the
        /// connection's private fields. Reflection because this is a diagnosis, not an API.
        /// </summary>
        private static string DescribeSocket(ITikConnection connection)
        {
            try
            {
                var type = connection.GetType();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var tcp = type.GetField("_tcpConnection", Flags)?.GetValue(connection) as System.Net.Sockets.TcpClient;
                var readerTask = type.GetField("_readerTask", Flags)?.GetValue(connection) as System.Threading.Tasks.Task;
                var fault = type.GetField("_readerFault", Flags)?.GetValue(connection) as Exception;
                string available = tcp == null ? "?" : tcp.Available.ToString();
                string connected = tcp == null ? "?" : tcp.Connected.ToString();
                string reader = readerTask == null ? "?" : readerTask.Status.ToString();
                string faultText = fault == null ? "(none)" : fault.Message;
                return "available=" + available + " connected=" + connected
                     + " reader=" + reader + " readerFault=" + faultText;
            }
            catch (Exception ex) { return "could not inspect: " + ex.Message; }
        }

        private static void Report(int attempt, string outcome, int rows, Stopwatch sw, long openedAtMs,
                                   List<long> gapTicks, Exception ex)
        {
            double MaxGapMs() => gapTicks.Count == 0
                ? 0
                : gapTicks.Max() * 1000.0 / Stopwatch.Frequency;
            double P99GapMs()
            {
                if (gapTicks.Count == 0) return 0;
                var sorted = gapTicks.OrderBy(t => t).ToList();
                return sorted[(int)(sorted.Count * 0.99)] * 1000.0 / Stopwatch.Frequency;
            }

            Console.WriteLine(
                $"attempt {attempt}: {outcome} rows={rows} words={gapTicks.Count} "
                + $"open={openedAtMs}ms total={sw.ElapsedMilliseconds}ms "
                + $"maxWordGap={MaxGapMs():F1}ms p99WordGap={P99GapMs():F1}ms"
                + (ex == null ? "" : $" ex={ex.GetType().Name}"));
        }
    }
}
