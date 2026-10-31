// LoginRefusalSoakProbe.cs — how often does the router refuse a correct EC-SRP5 login, per carrier?
//
// RouterOS refuses roughly 0.5-1 % of EC-SRP5 logins that carry correct credentials (Docs/findings-winbox.md §13,
// Docs/findings-mactelnet.md §8); TikOpenRetry retries them, so a caller never sees one. A change to the client
// handshake that broke a sign or a byte would not show as that rate: it would refuse every login, or a fixed share
// of them that a retry does not clear. This probe opens each carrier N times and counts, from the "login" trace
// channel TikOpenRetry writes, the attempts the router refused and the opens that failed even after retrying —
// the figures to compare against the known rate.
//
// Nothing here asserts a rate: it is a measurement, and the output is the result (console, plus TIK4NET_PROBE_LOG).
//
// Env:  TIK4NET_SOAK_OPENS       opens per transport (default 200)
//       TIK4NET_PROBE_TRANSPORTS comma-separated subset of WinboxNative,WinboxNativeMac,WinboxCliMac,MacTelnet
//       TIK4NET_PROBE_LOG        append the report to this file as well
//       TIK4NET_ROUTER           the router profile (App.config), as for every leg

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using tik4net.Diagnostics;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run this.
    [Ignore("Ad-hoc login soak against a live router — comment out to run.")]
    [TestClass]
    [TestLock(TestLockScope.Router)]
    [TestCategory(TestCategories.LegIndependent)]
    public class LoginRefusalSoakProbe : LockedTestBase
    {
        // One per EC-SRP5 client body — WinBox over TCP, WinBox over MAC, MAC-Telnet's control packets — and the WinBox
        // CLI over MAC, whose open goes on past the handshake to the terminal and its login screen.
        private static readonly TikConnectionType[] AllTransports =
        {
            TikConnectionType.WinboxNative,
            TikConnectionType.WinboxNativeMac,
            TikConnectionType.WinboxCliMac,
            TikConnectionType.MacTelnet,
        };

        private sealed class LoginSink : ITikWireTraceSink
        {
            internal readonly List<string> Notes = new List<string>();

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                if (channel == "login" && note != null)
                    lock (Notes) Notes.Add(note);
            }
        }

        [TestMethod]
        public void Probe_RefusedLoginRate_PerCarrier()
        {
            int opens = int.TryParse(Environment.GetEnvironmentVariable("TIK4NET_SOAK_OPENS"), out int n) && n > 0
                ? n : 200;
            string only = Environment.GetEnvironmentVariable("TIK4NET_PROBE_TRANSPORTS");
            var selected = string.IsNullOrEmpty(only)
                ? AllTransports
                : AllTransports.Where(t => only.Split(',').Any(
                    s => string.Equals(s.Trim(), t.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();

            Log($"login soak: {opens} opens per transport, router profile "
                + (Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default"));
            foreach (var type in selected)
            {
                var sink = new LoginSink();
                int failed = 0;
                var failures = new Dictionary<string, int>();
                var sw = Stopwatch.StartNew();
                using (TikWireTrace.Capture(sink))
                {
                    for (int i = 0; i < opens; i++)
                    {
                        try
                        {
                            using (var c = TestBase.LabSetup(type).Create(type))
                                c.Close();
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            string key = ex.GetType().Name + ": " + FirstLine(ex.Message);
                            failures[key] = failures.TryGetValue(key, out int k) ? k + 1 : 1;
                        }
                    }
                }

                var retries = sink.Notes.Where(x => x.Contains("retrying")).ToList();
                int refused = retries.Count(x => x.Contains("refused") || x.Contains("nothing on screen"));
                Log($"{type,-16} opens={opens} failed={failed} retried-attempts={retries.Count} "
                    + $"refusals={refused} ({100.0 * refused / opens:0.0} % of opens) wall={sw.Elapsed:mm\\:ss}");
                foreach (var g in retries.GroupBy(x => x).OrderByDescending(g => g.Count()))
                    Log($"    {g.Count(),4} × {g.Key}");
                foreach (var kv in failures)
                    Log($"    failed {kv.Value} × {kv.Key}");
            }
        }

        private static string FirstLine(string s)
        {
            int i = s.IndexOfAny(new[] { '\r', '\n' });
            return i < 0 ? s : s.Substring(0, i);
        }

        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
