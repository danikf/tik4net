// RomonAgentProbeTest.cs — what does a RoMON agent answer on the M2 layer? (5.0 RoMON research)
//
// WinBox reaches a RoMON node by asking a RoMON-enabled router, the agent, to relay for it. How the client
// asks is not documented anywhere, and the .jg catalog names only the agent's RoMON *management* handlers:
// [127,1] port list, [127,2] settings + ping, [127,4] discover. A relay handler, if the relay rides M2 at
// all, answers but is in no .jg window. This walks SYS_TO around [127,x] with the three request shapes
// every handler understands one of, and logs the raw answer of each — "not implemented" (0xFE0002) versus
// anything else is the signal.
//
// Read-only: getall, get-singleton and a command-less message. Nothing is written to the agent.
// Opt-in: set TIK4NET_ROMON_PROBE=1 (and TIK4NET_PROBE_LOG=<file> to keep the output).
//
// The Probe_Romon_SshRelay_* methods drive the SSH relay instead (/tool romon ssh from a Telnet or SSH session to
// the agent — TIK4NET_ROMON_AGENT_TRANSPORT). They log in to the TARGET, so they need its user and password in
// TIK4NET_ROMON_TARGET_USER / TIK4NET_ROMON_TARGET_PASS (never stored), and they only read.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using tik4net.Objects;
using tik4net.Winbox;
using M2 = tik4net.Winbox.M2Message;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    public class RomonAgentProbeTest : LockedTestBase
    {
        private static void Log(string line)
        {
            Console.WriteLine(line);
            string path = Environment.GetEnvironmentVariable("TIK4NET_PROBE_LOG");
            if (!string.IsNullOrEmpty(path))
                try { System.IO.File.AppendAllText(path, line + Environment.NewLine); } catch { }
        }

        private static WinboxM2Session OpenAgent()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("RoMON agent probe — set TIK4NET_ROMON_PROBE=1 to run.");
            var session = new WinboxM2Session();
            session.Open(LabConfig.Get("host"), 8291, LabConfig.Get("user"),
                LabConfig.Get("pass") ?? "", 5000, 5000);
            return session;
        }

        private static void Probe(WinboxM2Session s, string label, int[] handler, params byte[][] extra)
        {
            var fields = new System.Collections.Generic.List<byte[]>
            {
                M2.SysToArr(handler), M2.SysFrom(),
                M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
            };
            fields.AddRange(extra);
            byte[] resp;
            try { resp = s.SendReceive(M2.BuildM2(fields.ToArray()), 3000); }
            catch (Exception ex) { Log($"[{string.Join(",", handler)}] {label}: EXCEPTION {ex.GetType().Name}: {ex.Message}"); return; }
            if (resp == null) { Log($"[{string.Join(",", handler)}] {label}: (no reply)"); return; }
            Log($"[{string.Join(",", handler)}] {label}: status=0x{M2.ParseSysStatus(resp):X} {M2.DescribeSysError(resp)}");
            Log("    " + M2.Describe(resp).Replace("\n", "\n    "));
        }

        private static byte[] ParseMac(string mac)
        {
            var parts = mac.Split(':', '-');
            var b = new byte[6];
            for (int i = 0; i < 6; i++) b[i] = Convert.ToByte(parts[i], 16);
            return b;
        }

        /// <summary>
        /// [127,3] answers nothing to the generic request shapes — it is not "not implemented", it withholds
        /// the reply. Each variant runs on a fresh session so one withheld reply cannot poison the next.
        /// The target's RoMON id comes from TIK4NET_ROMON_TARGET — a real MAC does not belong in this file.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_Handler3_Variants()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target)) Assert.Inconclusive("Set TIK4NET_ROMON_TARGET=<romon id>.");
            byte[] id = ParseMac(target);
            int[] h = { 127, 3 };
            var variants = new (string, byte[][])[]
            {
                ("r1=id", new[] { M2.RawUser(1, id) }),
                ("r1=id u2=8291", new[] { M2.RawUser(1, id), M2.U32User(2, 8291) }),
                ("cmd=getall r1=id", new[] { M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll), M2.RawUser(1, id) }),
                ("cmd=7 r1=id", new[] { M2.U32Sys(WinboxM2Protocol.SysKey.Command, 7), M2.RawUser(1, id) }),
                ("cmd=1 r1=id", new[] { M2.U32Sys(WinboxM2Protocol.SysKey.Command, 1), M2.RawUser(1, id) }),
                ("cmd=0xFE0005 r1=id", new[] { M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Add), M2.RawUser(1, id) }),
            };
            foreach (var (label, extra) in variants)
                using (var s = OpenAgent())
                    Probe(s, label, h, extra);
        }

        /// <summary>
        /// WinBox's own connect, as the decrypting proxy (RomonWinboxProxyProbe) logged it: SYS_TO=[2], command 2001,
        /// target id raw at key 4, target user at 7, password at 8, bool 6 = true. The reply carries the link id in
        /// STD_ID; the target's handlers are then reached with SYS_TO=[2, link, handler...]. WinBox logs in to the agent
        /// as "user+r", but a plain login opens the link just the same. Target login from TIK4NET_ROMON_TARGET_USER /
        /// _PASS, else App.config romonTarget*.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_StreamOpen_Connect()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target)) Assert.Inconclusive("Set TIK4NET_ROMON_TARGET=<romon id>.");
            string user = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_USER") ?? LabConfig.Get("romonTargetUser");
            string pass = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_PASS") ?? LabConfig.Get("romonTargetPass") ?? "";
            using (var s = OpenAgent())
            {
                var connect = M2.BuildM2(M2.SysToArr(2), M2.SysFrom(1), M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                    s.NextReqIdField(), M2.U32Sys(WinboxM2Protocol.SysKey.Command, 2001), M2.BoolSys(6, true),
                    M2.StringUser(8, pass), M2.StringUser(7, user), M2.RawUser(4, ParseMac(target)));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                byte[] reply = s.SendReceive(connect, 10000);
                Log($"connect status=0x{M2.ParseSysStatus(reply):X} in {sw.ElapsedMilliseconds} ms {M2.DescribeSysError(reply)}");
                Log("    " + M2.Describe(reply));
                if (M2.ParseSysStatus(reply) != 0) return;
                int link = M2.ParseSessionId(reply);
                Log($"    link id {link}");
                foreach (var (label, h, cmd) in new[] { ("sysinfo", new[] { 2, link, 13, 4 }, 7), ("identity", new[] { 2, link, 24, 1 }, 0xFE000D) })
                {
                    byte[] r = s.SendReceive(M2.BuildM2(M2.SysToArr(h), M2.SysFrom(1),
                        M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                        M2.U32Sys(WinboxM2Protocol.SysKey.Command, cmd)), 5000);
                    Log($"    {label}: status=0x{M2.ParseSysStatus(r):X} {M2.Describe(r)}");
                }
            }
        }

        /// <summary>
        /// The same link end, one layer down: every frame the agent sends on the raw relay channel once RoMON is switched
        /// off on the target, decoded, routed or not. Writes /tool romon enabled on the TARGET and restores it.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_WinboxRelay_LinkEndFrames()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var relayTarget = new tik4net.Cli.RomonRelayTarget(LabConfig.Get("romonTargetId"),
                LabConfig.Get("romonTargetUser"), LabConfig.Get("romonTargetPass") ?? "");
            using (var target = new TikConnectionSetup(LabConfig.Get("romonTargetHost"), LabConfig.Get("romonTargetUser"),
                       LabConfig.Get("romonTargetPass") ?? "").Create(TikConnectionType.Api))
            using (var ch = new WinboxRomonChannel(new WinboxM2Session(), relayTarget))
            {
                ch.Open(LabConfig.Get("host"), 8291, LabConfig.Get("user"), LabConfig.Get("pass") ?? "", 5000, 5000);
                Log($"link {ch.Link}");
                byte[] Identity() => M2.BuildM2(M2.SysToArr(24, 1), M2.SysFrom(),
                    M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), ch.NextReqIdField(),
                    M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton));
                Log("before: " + M2.Describe(ch.SendReceive(Identity(), 5000)));
                target.CreateCommandAndParameters("/tool/romon/set", "enabled", "no").ExecuteNonQuery();
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    ch.Send(Identity());
                    while (sw.ElapsedMilliseconds < 15000)
                    {
                        byte[] f;
                        try { f = ch.Receive(3000); }
                        catch (Exception ex) { Log($"{sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}"); continue; }
                        Log($"{sw.ElapsedMilliseconds} ms: " + (f == null ? "(null)" : M2.Describe(f) + "\n    hex " + BitConverter.ToString(f)));
                        if (f == null) break;
                    }
                }
                finally { target.CreateCommandAndParameters("/tool/romon/set", "enabled", "yes").ExecuteNonQuery(); }
            }
        }

        /// <summary>
        /// What ends the target's session when a relay channel closes: for each variant a fresh link is opened, the
        /// variant sent, the channel disposed, and the target's relayed WinBox sessions (/user/active by-romon) counted
        /// over its own API before and 5 s after. Read-only on both routers apart from the sessions themselves.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_WinboxRelay_CloseVariants()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var relayTarget = new tik4net.Cli.RomonRelayTarget(LabConfig.Get("romonTargetId"),
                LabConfig.Get("romonTargetUser"), LabConfig.Get("romonTargetPass") ?? "");
            using (var target = new TikConnectionSetup(LabConfig.Get("romonTargetHost"), LabConfig.Get("romonTargetUser"),
                       LabConfig.Get("romonTargetPass") ?? "").Create(TikConnectionType.Api))
            {
                int Relayed() => target.CreateCommand("/user/active/print").ExecuteList()
                    .Count(r => r.GetResponseFieldOrDefault("via", "") == "winbox"
                                && !string.IsNullOrEmpty(r.GetResponseFieldOrDefault("by-romon", "")));
                byte[] Logout(int[] to, bool reply, WinboxM2Session a, params byte[][] extra)
                {
                    var f = new System.Collections.Generic.List<byte[]> { M2.SysToArr(to), M2.SysFrom(),
                        M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.Logout) };
                    if (reply) { f.Add(M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true)); f.Add(a.NextReqIdField()); }
                    f.AddRange(extra);
                    return M2.BuildM2(f.ToArray());
                }
                var variants = new (string, Action<WinboxM2Session, WinboxRomonChannel>)[]
                {
                    ("nothing", (a, ch) => { }),
                    ("logout [2,link] reply expected", (a, ch) => a.Send(Logout(new[] { 2, ch.Link }, true, a))),
                    ("logout [0xFF0003,link]", (a, ch) => a.Send(Logout(new[] { 0xFF0003, ch.Link }, true, a))),
                    ("logout [2] reply expected", (a, ch) => a.Send(Logout(new[] { 2 }, true, a))),
                    ("logout [2,link,13,4]", (a, ch) => a.Send(Logout(new[] { 2, ch.Link, 13, 4 }, true, a))),
                };
                foreach (var (label, send) in variants)
                {
                    int before = Relayed();
                    var agent = new WinboxM2Session();
                    var ch = new WinboxRomonChannel(agent, relayTarget);
                    ch.Open(LabConfig.Get("host"), 8291, LabConfig.Get("user"), LabConfig.Get("pass") ?? "", 5000, 5000);
                    int open = Relayed();
                    try { send(agent, ch); } catch (Exception ex) { Log($"{label}: send threw {ex.GetType().Name}: {ex.Message}"); }
                    System.Threading.Thread.Sleep(1000);
                    string pushed = "";
                    try { while (agent.DataAvailable) pushed += " | " + M2.Describe(agent.Receive(1000)); } catch (Exception ex) { pushed += " | " + ex.GetType().Name; }
                    ch.Dispose();
                    System.Threading.Thread.Sleep(5000);
                    Log($"{label}: relayed sessions before {before}, open {open}, 5 s after close {Relayed()}; frames after send:{pushed}");
                }
            }
        }

        /// <summary>
        /// The msg-proxy's commands around the connect (2001), each sent with the open link's id and a reply expected,
        /// with the target's relayed-session count after each — which one, if any, ends a link.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_WinboxRelay_ProxyCommandScan()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var relayTarget = new tik4net.Cli.RomonRelayTarget(LabConfig.Get("romonTargetId"),
                LabConfig.Get("romonTargetUser"), LabConfig.Get("romonTargetPass") ?? "");
            using (var target = new TikConnectionSetup(LabConfig.Get("romonTargetHost"), LabConfig.Get("romonTargetUser"),
                       LabConfig.Get("romonTargetPass") ?? "").Create(TikConnectionType.Api))
            {
                int Relayed() => target.CreateCommand("/user/active/print").ExecuteList()
                    .Count(r => r.GetResponseFieldOrDefault("via", "") == "winbox"
                                && !string.IsNullOrEmpty(r.GetResponseFieldOrDefault("by-romon", "")));
                var agent = new WinboxM2Session();
                using (var ch = new WinboxRomonChannel(agent, relayTarget))
                {
                    ch.Open(LabConfig.Get("host"), 8291, LabConfig.Get("user"), LabConfig.Get("pass") ?? "", 5000, 5000);
                    Log($"link {ch.Link}, relayed sessions {Relayed()}");
                    foreach (int cmd in new[] { 2000, 2002, 2003, 2004, 2005, 2006, 2007, 2008, 2009, 2010 })
                    {
                        string answer;
                        try
                        {
                            byte[] r = agent.SendReceive(M2.BuildM2(M2.SysToArr(2), M2.SysFrom(),
                                M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), agent.NextReqIdField(),
                                M2.U32Sys(WinboxM2Protocol.SysKey.Command, cmd), M2.SessionIdField(ch.Link)), 7000);
                            answer = $"status=0x{M2.ParseSysStatus(r):X} {M2.Describe(r)}";
                        }
                        catch (Exception ex) { answer = ex.GetType().Name + ": " + ex.Message; }
                        System.Threading.Thread.Sleep(1500);
                        Log($"cmd {cmd}: {answer}; relayed sessions {Relayed()}");
                    }
                }
            }
        }

        /// <summary>
        /// The raw frames a relay channel's agent session receives when an unrelated Telnet session of the same user
        /// on the agent logs out, read off the carrier below the relay channel (so nothing is interpreted), followed by
        /// an identity read through the link. Read-only.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_WinboxRelay_FramesOnAnotherLogout()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var relayTarget = new tik4net.Cli.RomonRelayTarget(LabConfig.Get("romonTargetId"),
                LabConfig.Get("romonTargetUser"), LabConfig.Get("romonTargetPass") ?? "");
            var agent = new WinboxM2Session();
            using (var ch = new WinboxRomonChannel(agent, relayTarget))
            {
                ch.Open(LabConfig.Get("host"), 8291, LabConfig.Get("user"), LabConfig.Get("pass") ?? "", 5000, 5000);
                Log($"link {ch.Link}");
                using (var telnet = new TikConnectionSetup(LabConfig.Get("host"), LabConfig.Get("user"), LabConfig.Get("pass") ?? "")
                           .Create(TikConnectionType.Telnet))
                    telnet.LoadSingle<tik4net.Objects.System.SystemIdentity>();
                Log("telnet session closed");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 5000)
                {
                    if (!agent.DataAvailable) { System.Threading.Thread.Sleep(100); continue; }
                    Log($"{sw.ElapsedMilliseconds} ms raw: {M2.Describe(agent.Receive(2000))}");
                }
                try
                {
                    Log("identity through the link: " + M2.Describe(agent.SendReceive(M2.BuildM2(M2.SysToArr(2, ch.Link, 24, 1), M2.SysFrom(),
                        M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), agent.NextReqIdField(),
                        M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton)), 5000)));
                }
                catch (Exception ex) { Log("identity through the link: " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        /// <summary>
        /// An idle relay channel, nothing sent for 150 s: every frame the agent pushes meanwhile, with its arrival time.
        /// TIK4NET_ROMON_IDLE_CARRIER=mac uses the MAC-layer carrier (default TCP). Read-only.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_WinboxRelay_IdleFrames()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            bool mac = Environment.GetEnvironmentVariable("TIK4NET_ROMON_IDLE_CARRIER") == "mac";
            var relayTarget = new tik4net.Cli.RomonRelayTarget(LabConfig.Get("romonTargetId"),
                LabConfig.Get("romonTargetUser"), LabConfig.Get("romonTargetPass") ?? "");
            IWinboxM2Channel carrier = mac ? new WinboxMacM2Session(LabConfig.Get("routerMac")) : (IWinboxM2Channel)new WinboxM2Session();
            using (var ch = new WinboxRomonChannel(carrier, relayTarget))
            {
                ch.Open(LabConfig.Get("host"), mac ? 20561 : 8291, LabConfig.Get("user"), LabConfig.Get("pass") ?? "", 5000, 5000);
                Log($"{(mac ? "MAC" : "TCP")} link {ch.Link}");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 150000)
                {
                    if (!ch.DataAvailable) { System.Threading.Thread.Sleep(200); continue; }
                    try
                    {
                        byte[] f = ch.Receive(3000);
                        Log($"{sw.ElapsedMilliseconds} ms: " + (f == null ? "(null)" : M2.Describe(f)));
                        if (f == null) break;
                    }
                    catch (Exception ex) { Log($"{sw.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}"); break; }
                }
                Log($"{sw.ElapsedMilliseconds} ms: idle over, sendAbandoned={ch.SendAbandoned}");
                try
                {
                    Log("identity through the link: " + M2.Describe(ch.SendReceive(M2.BuildM2(M2.SysToArr(24, 1), M2.SysFrom(),
                        M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), ch.NextReqIdField(),
                        M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton)), 5000)));
                }
                catch (Exception ex) { Log("identity through the link: " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        /// <summary>
        /// WinBox.exe names a system key SYS_ROMON; its key-name switch places it at 0xFF0018, type 0x30
        /// (raw). If the agent routes by it, a read of the RoMON settings singleton answers with the
        /// TARGET's current-id rather than the agent's. Read-only: get-singleton and get-all only.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_SysRomonRouting()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target)) Assert.Inconclusive("Set TIK4NET_ROMON_TARGET=<romon id>.");
            byte[] id = ParseMac(target);
            const int SysRomon = 0xFF0018;
            var singleton = new[] { M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton),
                                    M2.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags) };
            using (var s = OpenAgent())
            {
                Probe(s, "local get-singleton", new[] { 127, 2 }, singleton);
                Probe(s, "SYS_ROMON get-singleton", new[] { 127, 2 }, Concat(singleton, M2.RawSys(SysRomon, id)));
                Probe(s, "SYS_ROMON sysinfo get-singleton", new[] { 13, 4 }, Concat(singleton, M2.RawSys(SysRomon, id)));
            }
        }

        /// <summary>
        /// WinBox.exe's first RoMON step (the "Getting RoMON settings" status) sends SYS_TO=[127,2],
        /// SYS_CMD=9 with an empty body and reports "Connected to RoMON" on the reply. This sends the same,
        /// then repeats the SYS_ROMON-routed reads on the same session.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_Cmd9ThenRoute()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target)) Assert.Inconclusive("Set TIK4NET_ROMON_TARGET=<romon id>.");
            byte[] id = ParseMac(target);
            const int SysRomon = 0xFF0018;
            var singleton = new[] { M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton),
                                    M2.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags) };
            using (var s = OpenAgent())
            {
                Probe(s, "cmd=9", new[] { 127, 2 }, M2.U32Sys(WinboxM2Protocol.SysKey.Command, 9));
                Probe(s, "SYS_ROMON get-singleton", new[] { 127, 2 }, Concat(singleton, M2.RawSys(SysRomon, id)));
                Probe(s, "SYS_ROMON sysinfo get-singleton", new[] { 13, 4 }, Concat(singleton, M2.RawSys(SysRomon, id)));
            }
        }

        /// <summary>
        /// One full RoMON ping reply record, every key, untruncated — which keys the 'RoMON Ping' window's
        /// rows really carry, against the names its .jg gives them.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_PingReplyKeys()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target)) Assert.Inconclusive("Set TIK4NET_ROMON_TARGET=<romon id>.");
            using (var s = OpenAgent())
            {
                var start = M2.BuildM2(M2.SysToArr(127, 2), M2.SysFrom(),
                    M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                    M2.U32Sys(WinboxM2Protocol.SysKey.Command, 7), M2.RawUser(1, ParseMac(target)), M2.U32User(3, 2));
                var started = s.SendReceive(start, 3000);
                int id = M2.ParseSessionId(started);
                System.Threading.Thread.Sleep(2500);
                var poll = M2.BuildM2(M2.SysToArr(127, 2), M2.SysFrom(),
                    M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                    M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                    M2.SessionIdField(id), M2.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags));
                var resp = s.SendReceive(poll, 3000);
                foreach (var rec in M2.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records))
                {
                    Log("record:");
                    foreach (var kv in rec)
                    {
                        object v = kv.Value.Item2;
                        string text = v is byte[] b ? BitConverter.ToString(b) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                        Log($"    0x{kv.Key:X} {kv.Value.Item1} = {text}");
                    }
                }
                s.SendReceive(M2.BuildM2(M2.SysToArr(127, 2), M2.SysFrom(),
                    M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                    M2.U32Sys(WinboxM2Protocol.SysKey.Command, 8), M2.SessionIdField(id)), 3000);
            }
        }

        /// <summary>The decoded words of a RoMON ping over the native transport, as the mapper receives them.</summary>
        [TestMethod]
        public void Probe_Romon_NativePingWords()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target) || Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1 and TIK4NET_ROMON_TARGET=<romon id>.");
            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.WinboxNative,
                LabConfig.Get("host"), LabConfig.Get("user"),
                LabConfig.Get("pass") ?? ""))
            {
                var cmd = conn.CreateCommandAndParameters("/tool/romon/ping", "id", target, "count", "1");
                foreach (var row in cmd.ExecuteList())
                    Log(string.Join(" ", row.Words.Select(w => w.Key + "=" + w.Value)));
            }
        }

        /// <summary>
        /// Single-echo RoMON pings over raw M2, N times with and N times without an explicit Interval (u4, the
        /// .jg default 1000) — whether an omitted interval is what makes the echo intermittently time out.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_PingIntervalTimeouts()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            if (string.IsNullOrEmpty(target)) Assert.Inconclusive("Set TIK4NET_ROMON_TARGET=<romon id>.");
            byte[] mac = ParseMac(target);
            foreach (bool withInterval in new[] { false, true })
            {
                int timeouts = 0, answered = 0;
                using (var s = OpenAgent())
                    for (int i = 0; i < 10; i++)
                    {
                        var fields = new System.Collections.Generic.List<byte[]>
                        {
                            M2.SysToArr(127, 2), M2.SysFrom(), M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                            s.NextReqIdField(), M2.U32Sys(WinboxM2Protocol.SysKey.Command, 7),
                            M2.RawUser(1, mac), M2.U32User(3, 1),
                        };
                        if (withInterval) fields.Add(M2.U32User(4, 1000));
                        int id = M2.ParseSessionId(s.SendReceive(M2.BuildM2(fields.ToArray()), 3000));
                        System.Threading.Thread.Sleep(1500);
                        var resp = s.SendReceive(M2.BuildM2(M2.SysToArr(127, 2), M2.SysFrom(),
                            M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                            M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                            M2.SessionIdField(id), M2.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags)), 3000);
                        foreach (var rec in M2.ParseRecords(resp, WinboxM2Protocol.RecordKey.Records))
                        {
                            string status = rec.TryGetValue(0x65, out var st) ? Convert.ToString(st.Item2) : "";
                            if (status == "timeout") timeouts++; else answered++;
                        }
                        s.SendReceive(M2.BuildM2(M2.SysToArr(127, 2), M2.SysFrom(),
                            M2.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true), s.NextReqIdField(),
                            M2.U32Sys(WinboxM2Protocol.SysKey.Command, 8), M2.SessionIdField(id)), 3000);
                    }
                Log($"interval {(withInterval ? "u4=1000" : "omitted")}: answered={answered} timeout={timeouts}");
            }
        }

        // ── SSH relay (Telnet to the agent → /tool romon ssh → target) ────────

        // TIK4NET_ROMON_AGENT_TRANSPORT = telnet (default) | ssh | mactelnet — how the probe reaches the agent. Built
        // through the public surface exactly as a caller writes it: the agent is the lab router from App.config.
        private static TikConnectionType AgentTransport()
        {
            string t = Environment.GetEnvironmentVariable("TIK4NET_ROMON_AGENT_TRANSPORT") ?? "";
            if (string.Equals(t, "ssh", StringComparison.OrdinalIgnoreCase)) return TikConnectionType.Ssh;
            if (string.Equals(t, "mactelnet", StringComparison.OrdinalIgnoreCase)) return TikConnectionType.MacTelnet;
            return TikConnectionType.Telnet;
        }

        // Over the MAC layer the agent is named by its MAC as well, so no MNDP lookup is needed.
        private static TikRouterAddress AgentAddress()
        {
            string host = LabConfig.Get("host");
            string mac = LabConfig.Get("routerMac");
            return AgentTransport() == TikConnectionType.MacTelnet && !string.IsNullOrEmpty(mac)
                ? TikRouterAddress.FromHostAndMac(host, mac)
                : TikRouterAddress.FromHost(host);
        }

        private static ITikConnection OpenRelay(string targetId, string user, string password, string agentUser = null)
        {
            var agentSetup = new TikRomonAgentSetup(AgentAddress(),
                agentUser ?? LabConfig.Get("user"), LabConfig.Get("pass") ?? "");
            var targetSetup = new TikConnectionSetup(TikRouterAddress.FromRomonId(targetId), user, password)
            {
                RomonAgentSetup = agentSetup,
            };
            return targetSetup.Create(AgentTransport());
        }

        /// <summary>A transport that does not carry the relay must refuse the target before it connects.</summary>
        [TestMethod]
        public void Probe_Romon_SshRelay_RefusedOnATransportWithoutIt()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var targetSetup = new TikConnectionSetup(TikRouterAddress.FromRomonId("AA:BB:CC:DD:EE:FF"), "nobody", "x")
            {
                RomonAgentSetup = new TikRomonAgentSetup(LabConfig.Get("host"), "u", "p"),
            };
            var ex = Assert.ThrowsException<NotSupportedException>(() => targetSetup.CreateUnopened(TikConnectionType.WinboxCli));
            Log("refused: " + ex.Message);
        }

        /// <summary>
        /// Read-only tour of the target through the relay: identity, resource, a wide table, a filtered read.
        /// Target credentials come from TIK4NET_ROMON_TARGET_USER / TIK4NET_ROMON_TARGET_PASS and are never
        /// stored. Run only against a target you may read.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_SshRelay_ReadsTheTarget()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            string user = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_USER");
            string pass = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_PASS");
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1" || string.IsNullOrEmpty(target)
                || string.IsNullOrEmpty(user) || pass == null)
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1, TIK4NET_ROMON_TARGET and the target's USER/PASS.");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var conn = OpenRelay(target, user, pass))
            {
                Log($"open: {sw.ElapsedMilliseconds} ms");
                var info = conn.GetRomonConnectionInfo();
                Log($"info: {info}");
                Log($"info agent: user={info?.Agent.User} type={info?.Agent.ConnectionType} romon-id-set={!string.IsNullOrEmpty(info?.Agent.RomonId)}");
                Assert.IsNotNull(info);
                Assert.AreEqual(target, info.Target.RomonId, true);
                var identity = conn.LoadSingle<tik4net.Objects.System.SystemIdentity>();
                Log($"identity: {identity.Name}");
                var resource = conn.LoadSingle<tik4net.Objects.System.SystemResource>();
                Log($"version: {resource.Version}  board: {resource.BoardName}");
                var interfaces = conn.LoadAll<tik4net.Objects.Interface.Interface>().ToList();
                Log($"interfaces: {interfaces.Count}, longest name '{interfaces.Select(i => i.Name.Value).OrderByDescending(n => n?.Length).FirstOrDefault()}'");
                foreach (var i in interfaces.Take(3)) Log($"    {i.Name} type={i.Type} mac={(string.IsNullOrEmpty(i.MacAddress.Value) ? "" : "set")} running={i.Running}");
                var addresses = conn.LoadAll<tik4net.Objects.Ip.IpAddress>().ToList();
                Log($"ip addresses: {addresses.Count}");
                var ethers = conn.LoadList<tik4net.Objects.Interface.Interface>(
                    conn.CreateParameter("type", "ether", TikCommandParameterFormat.Filter)).ToList();
                Log($"filtered type=ether: {ethers.Count} (all ether: {ethers.All(e => e.Type == "ether")})");
                var romon = conn.LoadSingle<tik4net.Objects.Tool.Romon.ToolRomon>();
                Log($"target current-id matches: {string.Equals(romon.CurrentId.Value, target, StringComparison.OrdinalIgnoreCase)}");
                Log($"total: {sw.ElapsedMilliseconds} ms");
            }
        }

        /// <summary>
        /// The raw CLI answer for one interface through the relay, next to what the mapper made of it — for
        /// fields that disagree with the binary API. TIK4NET_ROMON_IFACE names the interface.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_SshRelay_RawInterfaceRow()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            string user = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_USER");
            string pass = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_PASS");
            string iface = Environment.GetEnvironmentVariable("TIK4NET_ROMON_IFACE");
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1" || string.IsNullOrEmpty(target)
                || string.IsNullOrEmpty(user) || pass == null || string.IsNullOrEmpty(iface))
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1, TIK4NET_ROMON_TARGET, USER/PASS and TIK4NET_ROMON_IFACE.");

            using (var conn = OpenRelay(target, user, pass))
            {
                foreach (var s in ((tik4net.Cli.CliConnectionBase)conn).CallCommandSync(":put [/interface print as-value where name=\"" + iface + "\"]"))
                    Log("raw: " + s);
                var cmd = conn.CreateCommandAndParameters("/interface/print", "name", iface);
                foreach (var row in cmd.ExecuteList())
                    Log("words: " + string.Join(" ", row.Words.Select(w => w.Key + "=" + w.Value)));
            }
        }

        /// <summary>
        /// The relay ends while the connection is open (here: /quit typed on the target). The agent's session must end
        /// with it — the next command either fails (Telnet, SSH) or is relayed to the target again (MAC-Telnet, which
        /// reconnects a logged-out session). It must never answer from the agent. Then an idle past the MAC-Telnet
        /// console logout (~30 s) and one more read.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_SshRelay_WhenTheRelayEnds_TheAgentNeverAnswers()
        {
            string target = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET");
            string user = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_USER");
            string pass = Environment.GetEnvironmentVariable("TIK4NET_ROMON_TARGET_PASS");
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1" || string.IsNullOrEmpty(target)
                || string.IsNullOrEmpty(user) || pass == null)
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1, TIK4NET_ROMON_TARGET and the target's USER/PASS.");

            using (var conn = OpenRelay(target, user, pass))
            {
                var cli = (tik4net.Cli.CliConnectionBase)conn;
                Func<string> currentId = () => conn.LoadSingle<tik4net.Objects.Tool.Romon.ToolRomon>().CurrentId.Value;
                Assert.AreEqual(target, currentId(), true);

                try { cli.CallCommandSync("/quit"); Log("/quit on the target: returned"); }
                catch (Exception ex) { Log("/quit on the target: " + ex.GetType().Name + " — " + ex.Message); }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    string after = currentId();
                    Log($"after /quit ({sw.ElapsedMilliseconds} ms): answered by {(string.Equals(after, target, StringComparison.OrdinalIgnoreCase) ? "the TARGET" : "'" + after + "' — NOT the target")}");
                    Assert.AreEqual(target, after, true, "a command after the relay ended must never answer from the agent");
                }
                catch (AssertFailedException) { throw; }
                catch (Exception ex) { Log($"after /quit ({sw.ElapsedMilliseconds} ms): failed — {ex.GetType().Name}: {ex.Message}"); }

            }

            if (AgentTransport() != TikConnectionType.MacTelnet)
                return;

            // RouterOS logs an idle MAC-Telnet console out; the reconnect must relay again before it resends.
            using (var conn = OpenRelay(target, user, pass))
            {
                System.Threading.Thread.Sleep(35000);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string afterIdle = conn.LoadSingle<tik4net.Objects.Tool.Romon.ToolRomon>().CurrentId.Value;
                Log($"after a 35 s idle ({sw.ElapsedMilliseconds} ms): {(string.Equals(afterIdle, target, StringComparison.OrdinalIgnoreCase) ? "the TARGET" : "NOT the target")}");
                Assert.AreEqual(target, afterIdle, true);
            }
        }

        /// <summary>An id nobody answers to: the relay must fail fast, name the cause, and send no password.</summary>
        [TestMethod]
        public void Probe_Romon_SshRelay_UnknownIdFailsCleanly()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ex = Assert.ThrowsException<TikRomonRelayException>(() => OpenRelay("AA:BB:CC:DD:EE:FF", "nobody", "x"));
            Assert.AreEqual(TikRomonRelayFailure.TargetUnreachable, ex.Reason);
            Log($"unknown id: {sw.ElapsedMilliseconds} ms — {ex.Reason}: {ex.Message}");
        }

        /// <summary>
        /// A login the AGENT refuses: a plain login exception that says it was the agent. A nonexistent user, not a
        /// wrong password — over SSH RouterOS admits an empty-password account whatever password is offered.
        /// </summary>
        [TestMethod]
        public void Probe_Romon_SshRelay_RefusedAgentLoginNamesTheAgent()
        {
            if (Environment.GetEnvironmentVariable("TIK4NET_ROMON_PROBE") != "1")
                Assert.Inconclusive("Set TIK4NET_ROMON_PROBE=1.");
            var ex = Assert.ThrowsException<TikConnectionLoginException>(() =>
                OpenRelay("AA:BB:CC:DD:EE:FF", "nobody", "x", agentUser: "tik4net-no-such-user"));
            StringAssert.Contains(ex.Message, "RoMON agent");
            Log($"refused agent login: {ex.Message}");
        }

        private static byte[][] Concat(byte[][] a, params byte[][] b)
        {
            var r = new byte[a.Length + b.Length][];
            a.CopyTo(r, 0); b.CopyTo(r, a.Length);
            return r;
        }

        [TestMethod]
        public void Probe_Romon_HandlerWalk()
        {
            using (var s = OpenAgent())
            {
            foreach (var h in new[] { new[] { 127 }, new[] { 127, 0 }, new[] { 127, 1 }, new[] { 127, 2 }, new[] { 127, 3 },
                                      new[] { 127, 4 }, new[] { 127, 5 }, new[] { 127, 6 }, new[] { 127, 7 }, new[] { 127, 8 } })
            {
                Probe(s, "no-cmd", h);
                Probe(s, "getall", h, M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll),
                    M2.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags));
                Probe(s, "get-singleton", h, M2.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetSingleton),
                    M2.U32Sys(WinboxM2Protocol.RecordKey.Flags, WinboxM2Protocol.GetAllFlags));
            }
            }
        }
    }
}
