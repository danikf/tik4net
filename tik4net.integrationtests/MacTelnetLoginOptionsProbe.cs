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

        /// <summary>
        /// H7: whether the console's colour follows the SSH terminal type. The cursor probe is answered, so the console
        /// draws its prompt, and a <c>/interface print</c> shows whether the table comes in colour. Telnet has no such
        /// choice from here, so the name's <c>+c</c> over Telnet is the control in the other probe.
        /// </summary>
        [TestMethod]
        public void Probe_SshTerminalType_DecidesColour()
        {
            string user = LabConfig.Get("user");
            Log("SSH terminal type, router profile " + (Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default"));
            foreach (string loginName in new[] { user, user + "+c" })
                foreach (string terminalType in new[] { "vt100", "dumb", "xterm", "xterm-256color", "linux", "ansi", "" })
                {
                    try
                    {
                        using (var ssh = new SshClient(LabConfig.Get("host"), loginName, LabConfig.Get("pass") ?? ""))
                        {
                            ssh.Connect();
                            // The modes SshShellClient opens with: without them the line discipline eats the Enter.
                            var raw = new System.Collections.Generic.Dictionary<Renci.SshNet.Common.TerminalModes, uint>
                            {
                                { Renci.SshNet.Common.TerminalModes.ECHO, 0 }, { Renci.SshNet.Common.TerminalModes.ICANON, 0 },
                                { Renci.SshNet.Common.TerminalModes.ISIG, 0 }, { Renci.SshNet.Common.TerminalModes.IEXTEN, 0 },
                                { Renci.SshNet.Common.TerminalModes.IXON, 0 }, { Renci.SshNet.Common.TerminalModes.IXOFF, 0 },
                                { Renci.SshNet.Common.TerminalModes.ICRNL, 0 }, { Renci.SshNet.Common.TerminalModes.INLCR, 0 },
                                { Renci.SshNet.Common.TerminalModes.OPOST, 0 },
                            };
                            using (var shell = ssh.CreateShellStream(terminalType, 0, 0, 4096, 25, 8192, raw))
                            {
                                var received = new StringBuilder();
                                var vt = new tik4net.Cli.Vt100State(tik4net.Cli.Vt100State.RouterOsWidth, tik4net.Cli.Vt100State.RouterOsHeight);
                                void Pump(int ms)
                                {
                                    var clock = System.Diagnostics.Stopwatch.StartNew();
                                    while (clock.ElapsedMilliseconds < ms)
                                    {
                                        string text = shell.Read();
                                        if (text.Length == 0) { System.Threading.Thread.Sleep(30); continue; }
                                        received.Append(text);
                                        // The console's cursor probe, answered as the library answers it: it draws nothing until then.
                                        vt.ProcessAndAnswer(text, null, reply => { shell.Write(reply); shell.Flush(); });
                                    }
                                }
                                Pump(3000);
                                // An empty password gets the change-password nag first; never type into it.
                                if (received.ToString().Contains("new password>")) { shell.Write("\x03"); shell.Flush(); Pump(1500); }
                                if (received.ToString().Contains("new password>") && !received.ToString().EndsWith("> ")) throw new InvalidOperationException("still at the nag");
                                int atPrompt = received.Length;
                                shell.Write("/interface print\r");
                                shell.Flush();
                                Pump(2500);
                                string table = received.ToString().Substring(atPrompt);
                                bool promptColour = Sgr.IsMatch(received.ToString(0, atPrompt));
                                bool tableColour = Sgr.IsMatch(table);
                                Log($"  {loginName,-10} TERM={(terminalType.Length == 0 ? "(none)" : terminalType),-15} prompt {(promptColour ? "COLOUR" : "plain ")}; "
                                    + $"table {(tableColour ? "COLOUR" : "plain ")}; {atPrompt} B + {table.Length} B; ends '{Regex.Replace(table.Substring(Math.Max(0, table.Length - 90)), @"[\x00-\x1f]", m => "<" + ((int)m.Value[0]).ToString("X2") + ">")}'");
                            }
                            ssh.Disconnect();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  {loginName,-10} TERM={terminalType,-15} FAILED {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    }
                }
        }

        private static readonly Regex Sgr = new Regex(@"\x1b\[(\d+;)*3[0-7](;\d+)*m");

        /// <summary>
        /// Whether <c>detail</c> made the MAC-Telnet <c>/log</c> refusal worse: the command the library sends for a generic
        /// read, with and without the word, on one connection each, three times, against what the binary API counts.
        /// </summary>
        [TestMethod]
        public void Probe_LogOverMacTelnet_WithAndWithoutDetail()
        {
            string user = LabConfig.Get("user"), pass = LabConfig.Get("pass") ?? "";
            Log("/log over MAC-Telnet, router profile " + (Environment.GetEnvironmentVariable("TIK4NET_ROUTER") ?? "default"));
            using (var api = new TikConnectionSetup(TestBase.LabAddress(TikConnectionType.Api), user, pass).Create(TikConnectionType.Api))
                Log("  API rows " + api.CreateCommand("/log/print").ExecuteList().Count());

            string sentCommand = null;
            int runs = int.Parse(Environment.GetEnvironmentVariable("TIK4NET_LOG_RUNS") ?? "3");
            bool libraryOnly = Environment.GetEnvironmentVariable("TIK4NET_LOG_RUNS") != null;
            for (int run = 1; run <= runs; run++)
            {
                using (var c = new TikConnectionSetup(TestBase.LabAddress(TikConnectionType.MacTelnet), user, pass).Create(TikConnectionType.MacTelnet))
                {
                    c.OnWriteRow += (_, e) => { if (e.Word.Contains("/log print")) sentCommand = e.Word; };
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try { Log($"  run {run} library read: {c.CreateCommand("/log/print").ExecuteList().Count()} rows, {clock.Elapsed.TotalSeconds:F1} s"); }
                    catch (Exception ex) { Log($"  run {run} library read: {clock.Elapsed.TotalSeconds:F1} s, {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
                }
                if (sentCommand == null || libraryOnly) continue;
                foreach (string form in new[] { sentCommand, sentCommand.Replace(" detail", "") })
                    using (var c = new TikConnectionSetup(TestBase.LabAddress(TikConnectionType.MacTelnet), user, pass).Create(TikConnectionType.MacTelnet))
                    {
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            string text = ((ITikRawSentenceConnection)c).CreateRawCommand(form).ExecuteScalar();
                            string counted = Regex.Match(text, @"#n=(\d+)").Groups[1].Value;
                            Log($"  run {run} raw {(form.Contains(" detail") ? "detail " : "plain  ")}: {text.Length} B, {Regex.Matches(text, @"\*[0-9A-F]+").Count} ids, counted {counted}, {clock.Elapsed.TotalSeconds:F1} s");
                        }
                        catch (Exception ex)
                        {
                            Log($"  run {run} raw {(form.Contains(" detail") ? "detail " : "plain  ")}: {clock.Elapsed.TotalSeconds:F1} s, {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                        }
                    }
            }
            Log("  command: " + sentCommand);
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
