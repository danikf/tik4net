// MacTelnetLoginOptionsProbe.cs — what RouterOS makes of RouterOS login options in a MAC-Telnet login name.
//
// A login name may carry options after a '+' ('admin+t'). Two questions, measured for the WinBox + MAC review (X6, F1):
//   * the EC-SRP5 validator: does RouterOS hash the account name alone? tik4net builds it from EcSrp5.AccountName;
//     built from the whole name the router ends the session during login (measured before the fix).
//   * the console flags tik4net adds (+c, no colour): does RouterOS read a second '+' group? The client merges them
//     into the caller's group ('admin+t' → 'admin+tc'); 'admin+t+c' is sent as written, so it shows whether the
//     second group is read — a coloured console says it is not.
// Each name is opened over MAC-Telnet, one read is run, and the received terminal bytes are searched for an SGR colour
// sequence (ESC[…3xm, a foreground colour). Nothing is asserted: it is a development-time measurement, the output is the result (console,
// plus TIK4NET_PROBE_LOG). Run per router with -Router.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using tik4net.Diagnostics;

namespace tik4net.integrationtests
{
    // MSTest skips [Ignore] even under --filter, so comment it out to run this.
    [Ignore("Ad-hoc login-name measurement against a live router — comment out to run.")]
    [TestClass]
    [TestLock(TestLockScope.Router)]
    [TestLock(TestLockAttribute.MacLayer)]
    [TestCategory(TestCategories.LegIndependent)]
    [TestCategory(TestCategories.AnyRouter)]
    public class MacTelnetLoginOptionsProbe : LockedTestBase
    {
        private sealed class TerminalSink : ITikWireTraceSink
        {
            internal readonly StringBuilder Received = new StringBuilder();

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                if (channel == "mactelnet.udp" && dir == TikWireDir.Recv && data != null && count > 0)
                    lock (Received) Received.Append(Encoding.ASCII.GetString(data, offset, count));
            }
        }

        [TestMethod]
        public void Probe_LoginOptions_OverMacTelnet()
        {
            string user = LabConfig.Get("user");
            Log("MAC-Telnet login options, router profile " + (Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default"));
            foreach (string loginName in new[] { user, user + "+t", user + "+t+c" })
            {
                var sink = new TerminalSink();
                string result;
                using (TikWireTrace.Capture(sink))
                {
                    try
                    {
                        var setup = new TikConnectionSetup(TestBase.LabAddress(TikConnectionType.MacTelnet), loginName,
                            LabConfig.Get("pass") ?? "");
                        using (var c = setup.Create(TikConnectionType.MacTelnet))
                            result = "login OK, identity=" + c.CreateCommand("/system/identity/print").ExecuteScalar("name");
                    }
                    catch (Exception ex)
                    {
                        result = "FAILED " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
                    }
                }
                string received;
                lock (sink.Received) received = sink.Received.ToString();
                // A foreground-colour SGR (ESC[…3xm), not just any ESC[3 — cursor-up 3 is ESC[3A.
                bool coloured = Regex.IsMatch(received, @"\x1b\[(\d+;)*3[0-7](;\d+)*m");
                Log($"  {loginName,-14} {result}; terminal {(coloured ? "IN COLOUR" : "no colour")} ({received.Length} B received)");
            }
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
