// MacTelnetLoginOptionsProbe.cs — what RouterOS makes of RouterOS login options in a MAC-Telnet login name.
//
// A login name may carry options after a '+' ('admin+t'). Two questions, measured for the WinBox + MAC review (X6, F1):
//   * the EC-SRP5 validator: does RouterOS hash the account name alone? tik4net builds it from EcSrp5.AccountName;
//     built from the whole name the router ends the session during login (measured before the fix).
//   * the console flags tik4net adds (+c, no colour): does RouterOS read a second '+' group? The client merges them
//     into the caller's group ('admin+t' → 'admin+tc'); 'admin+t+c' is sent as written, so it shows whether the
//     second group is read — a coloured console says it is not.
// Each name is opened over MAC-Telnet (and, for F7, over Telnet, SSH and the WinBox CLI), one read is run, and the received terminal bytes are searched for an SGR colour
// sequence (ESC[…3xm, a foreground colour). Nothing is asserted: it is a development-time measurement, the output is the result (console,
// plus TIK4NET_PROBE_LOG). Run per router with -Router.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Renci.SshNet;
using System;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// F7: Telnet, SSH and the WinBox CLI append the console flags to the caller's name as a second group
        /// (<c>admin+t</c> goes out as <c>admin+t+c</c>). A console in colour for <c>user+t</c> says RouterOS reads one
        /// group only there too; <c>user+tc</c> (one group, the flag in it) is the control.
        /// </summary>
        [TestMethod]
        public void Probe_LoginOptions_OverTheOtherConsoles()
        {
            string user = LabConfig.Get("user");
            Log("Console login options, router profile " + (Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default"));
            foreach (var (type, channel) in new[] { (TikConnectionType.Telnet, "telnet.sock"), (TikConnectionType.Ssh, "ssh.pty"),
                         (TikConnectionType.WinboxCli, "wbxcli.mepty") })
                foreach (string loginName in new[] { user, user + "+t", user + "+tc" })
                {
                    var sink = new ChannelSink(channel);
                    string result;
                    using (TikWireTrace.Capture(sink))
                    {
                        try
                        {
                            var setup = new TikConnectionSetup(TestBase.LabAddress(type), loginName, LabConfig.Get("pass") ?? "");
                            using (var c = setup.Create(type))
                                result = "login OK, identity=" + c.CreateCommand("/system/identity/print").ExecuteScalar("name");
                        }
                        catch (Exception ex)
                        {
                            result = "FAILED " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
                        }
                    }
                    string received;
                    lock (sink.Received) received = sink.Received.ToString();
                    bool coloured = Regex.IsMatch(received, @"\x1b\[(\d+;)*3[0-7](;\d+)*m");
                    Log($"  {type,-10} {loginName,-14} {result}; terminal {(coloured ? "IN COLOUR" : "no colour")} ({received.Length} B received)");
                }
        }

        /// <summary>
        /// Whether SSH takes the name's options, with the library's retry out of the way: SSH.NET directly, so a refused
        /// name stays refused. The console draws nothing beyond its cursor probe until that is answered, unless the name's
        /// <c>+t</c> was read and the probe skipped, so the bytes of the first seconds say which happened.
        /// </summary>
        [TestMethod]
        public void Probe_SshLoginName_AcceptedAndHonoured()
        {
            string user = LabConfig.Get("user");
            Log("SSH login name, router profile " + (Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default"));
            using (var api = new TikConnectionSetup(TestBase.LabAddress(TikConnectionType.Api), user, LabConfig.Get("pass") ?? "").Create(TikConnectionType.Api))
                foreach (string loginName in new[] { user, user + "+c", user + "+t", user + "+tc" })
                {
                    try
                    {
                        using (var ssh = new SshClient(LabConfig.Get("host"), loginName, LabConfig.Get("pass") ?? ""))
                        {
                            ssh.Connect();
                            using (var shell = ssh.CreateShellStream("vt100", 80, 25, 640, 480, 8192))
                            {
                                var received = new StringBuilder();
                                var clock = System.Diagnostics.Stopwatch.StartNew();
                                while (clock.ElapsedMilliseconds < 5000)
                                {
                                    string text = shell.Read();
                                    if (text.Length > 0) received.Append(text);
                                    else System.Threading.Thread.Sleep(50);
                                }
                                string names = string.Join(",", api.CreateCommand("/user/active/print").ExecuteList()
                                    .Where(r => r.GetResponseFieldOrDefault("via", "") == "ssh")
                                    .Select(r => r.GetResponseField("name")));
                                Log($"  {loginName,-12} accepted; /user/active ssh names [{names}]; {received.Length} B in 5 s");
                            }
                            ssh.Disconnect();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  {loginName,-12} FAILED {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    }
                }
        }

        private sealed class ChannelSink : ITikWireTraceSink
        {
            private readonly string _channel;
            internal readonly StringBuilder Received = new StringBuilder();
            internal ChannelSink(string channel) { _channel = channel; }

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                if (channel == _channel && dir == TikWireDir.Recv && data != null && count > 0)
                    lock (Received) Received.Append(Encoding.ASCII.GetString(data, offset, count));
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
