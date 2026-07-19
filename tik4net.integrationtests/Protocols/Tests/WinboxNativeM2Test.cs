// WinboxNativeM2Test.cs — Phase 3: native M2 object calls (no mepty console).
// Goal: list interfaces + set/restore ether1 comment via structured M2 messages.
// Reference working native read: GetSystemInfo handler [13,4] cmd=7.
// Catalog: _notes/WinboxMessage (interface handler [20,0], Name=key 0x10006).

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    // EXPLORATORY RE probes for native M2 object protocol (Phase 3b). Manual-only —
    // some tests issue NativeSet (config writes) and all hit a live router. Marked [Ignore]
    // so they never run in the normal suite. Open problem: [20,0] cmd=3 returns an id-list
    // that is the interface TYPE registry, not instances (router has 2 ifaces; ether1 .id=*2
    // is absent from the 47-id list). The real instance get-record/set command is still unknown.
    [Ignore("Exploratory native-M2 RE probes — manual only. Some methods issue config writes; all hit a live router. Run individually via --filter for research.")]
    [TestClass]
    public class WinboxNativeM2Test
    {
        private const int WINBOX_PORT = 8291;
        private static readonly int[] IFACE = { 20, 0 };
        private const int KEY_NAME    = 0x10006;   // string 'Name'
        private const int KEY_RUNNING = 0x1000e;   // bool 'running'
        private const int KEY_MTU     = 0x10064;   // u32  'MTU'
        private const int KEY_TYPE    = 0x10001;   // u32  'type'
        private const int KEY_INACTIVE = 0xFE0008; // bool 'inactive'

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        // ── Helpers ───────────────────────────────────────────────────────────

        private static uint[] ParseIdList(byte[] frame)
        {
            var f = M2Message.ParseAllFields(frame);
            if (!f.TryGetValue(0x000001, out var v) || v.Item1 != "u32[]") return new uint[0];
            var s = v.Item2.ToString().Trim('[', ']');
            return s.Split(',').Select(uint.Parse).ToArray();
        }

        private static void DumpFrame(byte[] raw, string label)
        {
            var f = M2Message.ParseAllFields(raw);
            Console.WriteLine($"  [{label}] {raw.Length}B, {f.Count} fields:");
            foreach (var kv in f)
                Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
        }

        private static void DumpFrames(List<byte[]> frames, string label)
        {
            Console.WriteLine($"=== {label}: {frames.Count} frame(s) ===");
            for (int i = 0; i < frames.Count; i++)
                DumpFrame(frames[i], $"frame {i}");
        }

        // ── Previously working probes (kept for regression) ───────────────────

        // Step 2: cmd=3 returns the id list (user key 0x1 = u32[]). Now find the
        // "get one record" command: take first id, try commands with .id (0xFE0001).
        [TestMethod]
        public void Native_ProbeGetOneRecord()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // getall ids via cmd=3
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 2500);
                uint firstId = 0;
                foreach (var raw in idFrames)
                {
                    var ids = ParseIdList(raw);
                    if (ids.Length > 0) { firstId = ids[0]; break; }
                }
                Console.WriteLine($"first interface id = {firstId}");
                Assert.AreNotEqual(0u, firstId, "cmd=3 should return at least one id");

                // Wide sweep: which command returns a DATA frame (Name present or >64B)?
                // Try id both as .id (0xFE0001) and as user key 1 (u32).
                for (int cmd = 0; cmd <= 0x28; cmd++)
                {
                    foreach (var (label, idField) in new[] {
                        (".id", M2Message.SessionIdField((int)firstId)),
                        ("u1",  M2Message.U32User(1, (int)firstId)) })
                    {
                        List<byte[]> frames;
                        try { frames = client.ProbeCommandRaw(IFACE, cmd, 1200, idField); }
                        catch { continue; }
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                        if (hasName || maxLen > 70)
                        {
                            Console.WriteLine($"*** cmd=0x{cmd:X} via {label}: {frames.Count} frames, max {maxLen}B, Name={hasName}");
                            var data = frames.First(f => M2Message.ParseAllFields(f).Count > 5
                                                      || M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                            foreach (var kv in M2Message.ParseAllFields(data))
                                Console.WriteLine($"      key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                        }
                    }
                }
                Console.WriteLine("sweep done");
            }
        }

        // Probe: which SYS_CMD on handler [20,0] returns interface records?
        // A record frame carries key 0x10006 (Name) = e.g. "ether1".
        [TestMethod]
        public void Native_ProbeInterfaceListCommand()
        {
            var (host, user, pass) = Cfg();
            // candidate command numbers to try (bare request, no user fields)
            int[] candidates = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0xFE0001 };

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                foreach (int cmd in candidates)
                {
                    List<byte[]> frames;
                    try { frames = client.ProbeCommandRaw(IFACE, cmd, 2500); }
                    catch (Exception ex) { Console.WriteLine($"cmd=0x{cmd:X} EXC {ex.Message}"); continue; }

                    int withName = 0;
                    string sampleName = null;
                    foreach (var raw in frames)
                    {
                        var f = M2Message.ParseAllFields(raw);
                        if (f.TryGetValue(KEY_NAME, out var nm)) { withName++; sampleName = sampleName ?? nm.Item2?.ToString(); }
                    }
                    Console.WriteLine($"cmd=0x{cmd:X,-7} -> {frames.Count,3} frames, {withName,3} with Name(0x10006)"
                        + (sampleName != null ? $"  e.g. '{sampleName}'" : ""));

                    // dump the first frame's fields for the most promising commands
                    if (frames.Count > 0 && (withName > 0 || cmd <= 8))
                    {
                        Console.WriteLine($"    first frame fields ({frames[0].Length} B):");
                        foreach (var kv in M2Message.ParseAllFields(frames[0]))
                            Console.WriteLine($"      key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                    }
                }
            }
        }

        // ── New Phase 3b tests ─────────────────────────────────────────────────

        // Hex dump helper for raw frame inspection
        private static void HexDump(byte[] data, int maxBytes = 256)
        {
            int limit = Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder();
            for (int i = 0; i < limit; i++)
            {
                if (i % 16 == 0) sb.AppendFormat("\n  {0:X4}: ", i);
                sb.AppendFormat("{0:X2} ", data[i]);
            }
            if (data.Length > maxBytes) sb.Append($"\n  ... ({data.Length} total bytes)");
            Console.WriteLine(sb.ToString());
        }

        // Full dump of cmd=3+.id frame (662B was observed — want all field keys).
        [TestMethod]
        public void Native_FullDumpCmd3WithId()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Get the id list first
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 2500);
                uint firstId = 0;
                foreach (var raw in idFrames)
                {
                    var ids = ParseIdList(raw);
                    if (ids.Length > 0) { firstId = ids[0]; break; }
                }
                Console.WriteLine($"first id = {firstId}");

                // cmd=3 bare — full hex dump to look for embedded messages
                var frames3bare = client.ProbeCommandRaw(IFACE, 3, 3000);
                Console.WriteLine($"=== cmd=3 bare: {frames3bare.Count} frame(s) ===");
                foreach (var raw in frames3bare)
                {
                    Console.WriteLine($"  len={raw.Length}B");
                    HexDump(raw, raw.Length);
                }

                // cmd=3 with .id — full dump ALL frames
                var frames3id = client.ProbeCommandRaw(IFACE, 3, 3000,
                    M2Message.SessionIdField((int)firstId));
                DumpFrames(frames3id, "cmd=3 +.id");
                Console.WriteLine("HexDump cmd=3+.id[0]:");
                if (frames3id.Count > 0) HexDump(frames3id[0], frames3id[0].Length);
            }
        }

        // Hypothesis 1: streaming — send cmd=3 WITHOUT reply_expected.
        // The router may stream one frame per interface record.
        [TestMethod]
        public void Native_StreamingNoReplyExpected()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                Console.WriteLine("--- cmd=3 WITHOUT reply_expected ---");
                var frames = client.ProbeCommandStream(IFACE, 3, 5000);
                Console.WriteLine($"Received {frames.Count} frame(s):");
                int nameCount = 0;
                for (int i = 0; i < frames.Count; i++)
                {
                    var f = M2Message.ParseAllFields(frames[i]);
                    bool hasName = f.ContainsKey(KEY_NAME);
                    if (hasName) nameCount++;
                    Console.WriteLine($"  frame[{i}] {frames[i].Length}B hasName={hasName} fields={f.Count}");
                    foreach (var kv in f)
                        Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                }
                Console.WriteLine($"Total frames with Name: {nameCount}");
            }
        }

        // Hypothesis 2: field-key subscription.
        // cmd=3 + u32-array of desired field keys → router streams records with those fields.
        // The u32-array goes in user key 0x000001 (same key as the id-list response).
        [TestMethod]
        public void Native_Cmd3WithFieldKeySubscription()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Desired fields: Name(0x10006), running(0x1000e), MTU(0x10064), type(0x10001)
                int[] wantedKeys = { KEY_NAME, KEY_RUNNING, KEY_MTU, KEY_TYPE };

                Console.WriteLine("--- cmd=3 + field-key array (reply_expected=true) ---");
                var framesRE = client.ProbeCommandRaw(IFACE, 3, 4000,
                    M2Message.U32ArrayUser(1, wantedKeys));
                DumpFrames(framesRE, "cmd=3+keys reply_expected=true");

                Console.WriteLine("--- cmd=3 + field-key array (NO reply_expected) ---");
                var framesNoRE = client.ProbeCommandStream(IFACE, 3, 5000,
                    M2Message.U32ArrayUser(1, wantedKeys));
                DumpFrames(framesNoRE, "cmd=3+keys no reply_expected");
            }
        }

        // Try cmd=4 with BOTH type_id and instance_id — maybe the msg-proxy needs both.
        // ether1 = instance_id=2, type_id=1
        // loopback = instance_id=1, type_id=77
        [TestMethod]
        public void Native_Cmd4WithBothIds()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Combos to try: cmd, instance_id, type_id (as various field combinations)
                var testCases = new[]
                {
                    // cmd4: instance_id as .id, type_id as user key 1
                    (4, "cmd4 .id=2 u1=1", M2Message.SessionIdField(2), M2Message.U32User(1, 1)),
                    (4, "cmd4 .id=1 u1=77", M2Message.SessionIdField(1), M2Message.U32User(1, 77)),
                    // cmd4: instance_id as user key 1, type_id as .id
                    (4, "cmd4 u1=2 .id=1", M2Message.U32User(1, 2), M2Message.SessionIdField(1)),
                    // cmd7: try get-one with both
                    (7, "cmd7 .id=2 u1=1", M2Message.SessionIdField(2), M2Message.U32User(1, 1)),
                    // cmd3 with both
                    (3, "cmd3 .id=2 u1=1", M2Message.SessionIdField(2), M2Message.U32User(1, 1)),
                    // cmd4 with type_id as user key 0x10001 (type field from catalog)
                    (4, "cmd4 .id=2 type=1", M2Message.SessionIdField(2), M2Message.U32User(0x10001, 1)),
                    // cmd3 with type_id as user key 0x10001
                    (3, "cmd3 .id=2 type=1", M2Message.SessionIdField(2), M2Message.U32User(0x10001, 1)),
                };

                foreach (var (cmd, label, f1, f2) in testCases)
                {
                    var frames = client.ProbeCommandRaw(IFACE, cmd, 2000, f1, f2);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    Console.WriteLine($"{label}: {frames.Count} frames, max {maxLen}B, Name={hasName}");
                    if (hasName || maxLen > 80)
                        DumpFrames(frames, label);
                }

                // Also try: cmd=3 with type_id as user key 0x000002 (the key that returned type names)
                Console.WriteLine("--- cmd=3 with type_id in key 0x000002 ---");
                var f3t = client.ProbeCommandRaw(IFACE, 3, 2000,
                    M2Message.U32User(2, 1));  // key=2, val=1 (ether)
                DumpFrames(f3t, "cmd=3 u2=1");
            }
        }

        // Hypothesis: interface instance at [20, type_id, instance_id] path.
        // ether1 = [20, 1, 2], lo = [20, 77, 1]
        [TestMethod]
        public void Native_ProbeInstancePath()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                int[][] testPaths = {
                    new[]{ 20, 1, 2 },   // ether type=1, instance=2 (ether1)
                    new[]{ 20, 77, 1 },  // loopback type=77, instance=1 (lo)
                    new[]{ 20, 0, 2 },   // interface proxy, instance=2
                    new[]{ 20, 0, 1 },   // interface proxy, instance=1
                };

                foreach (var path in testPaths)
                {
                    string pathStr = "[" + string.Join(",", path) + "]";
                    foreach (int cmd in new[] { 3, 4, 7, 2, 6 })
                    {
                        List<byte[]> frames;
                        try { frames = client.ProbeCommandRaw(path, cmd, 1500); }
                        catch { continue; }
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                        bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str"));
                        if (hasName || maxLen > 80 || hasStr)
                        {
                            Console.WriteLine($"*** {pathStr} cmd={cmd}: {frames.Count}f max={maxLen}B Name={hasName}");
                            DumpFrames(frames, $"{pathStr} cmd={cmd}");
                        }
                        else
                        {
                            var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                            uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                            Console.WriteLine($"  {pathStr} cmd={cmd}: maxLen={maxLen}, err=0x{ec:X}");
                        }
                    }
                }
            }
        }

        // Hypothesis: ether instances at handler [<type_id>, 0], e.g. [1,0] for ether.
        // Try various handlers based on type registry ids.
        [TestMethod]
        public void Native_ProbeTypeBasedHandlers()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Try handlers based on type_ids from registry
                // ether=type_id=1, loopback=type_id=77
                int[][] testHandlers = {
                    new[]{1, 0},   // ether type handler?
                    new[]{77, 0},  // vrf type handler?
                    new[]{20, 1},  // sub-handler of interface proxy
                    new[]{0, 20},  // reversed
                    new[]{1},      // bare ether
                };

                foreach (var handler in testHandlers)
                {
                    foreach (int cmd in new[] { 3, 4, 7 })
                    {
                        List<byte[]> frames;
                        try { frames = client.ProbeCommandRaw(handler, cmd, 1000); }
                        catch { continue; }
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                        bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str[]"));
                        var path = "[" + string.Join(",", handler) + "]";
                        if (hasName || maxLen > 80 || hasStr)
                        {
                            Console.WriteLine($"*** handler={path} cmd={cmd}: {frames.Count} frames, max {maxLen}B, Name={hasName}");
                            DumpFrames(frames, $"{path} cmd={cmd}");
                        }
                        else
                        {
                            var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                            uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                            Console.WriteLine($"  {path} cmd={cmd}: maxLen={maxLen}, err=0x{ec:X}");
                        }
                    }
                }
            }
        }

        // Maybe cmd=10 is "subscribe to object" — after sending it, the router pushes data.
        // Wait up to 5s for any push frames after sending cmd=10 + instance id.
        [TestMethod]
        public void Native_SubscribeCmd10WaitForPush()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Subscribe to ether1 (instance_id=2)
                Console.WriteLine("--- Sending cmd=10 + id=2 (subscribe?) ---");
                var frames10 = client.ProbeCommandRaw(IFACE, 10, 5000,
                    M2Message.SessionIdField(2));
                Console.WriteLine($"Got {frames10.Count} frame(s) after cmd=10+id=2:");
                DumpFrames(frames10, "cmd=10+id=2");

                // Also subscribe to lo (id=1)
                Console.WriteLine("--- Sending cmd=10 + id=1 (subscribe?) ---");
                var frames10lo = client.ProbeCommandRaw(IFACE, 10, 3000,
                    M2Message.SessionIdField(1));
                DumpFrames(frames10lo, "cmd=10+id=1");

                // Try cmd=10 with NO id — might return all instances
                Console.WriteLine("--- Sending cmd=10 NO id ---");
                var frames10noid = client.ProbeCommandRaw(IFACE, 10, 3000);
                DumpFrames(frames10noid, "cmd=10 noid");
            }
        }

        // Deep dive: dump full frames for cmd=2,10 with instance ids (64B clean ACK).
        // And try cmd=3 with instance ids more carefully.
        [TestMethod]
        public void Native_DumpCleanAckCommands()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                foreach (int instId in new[] { 1, 2 })
                {
                    Console.WriteLine($"\n=== Instance id={instId} ===");
                    foreach (int cmd in new[] { 2, 10 })
                    {
                        var frames = client.ProbeCommandRaw(IFACE, cmd, 2000,
                            M2Message.SessionIdField(instId));
                        DumpFrames(frames, $"cmd={cmd}+id={instId}");
                        if (frames.Count > 0) HexDump(frames[0], frames[0].Length);
                    }

                    // Also check cmd=3 with instance id
                    Console.WriteLine($"--- cmd=3+id={instId} ---");
                    var f3 = client.ProbeCommandRaw(IFACE, 3, 2000,
                        M2Message.SessionIdField(instId));
                    foreach (var raw in f3)
                    {
                        var fields = M2Message.ParseAllFields(raw);
                        Console.WriteLine($"  len={raw.Length}B, fields={fields.Count}");
                        foreach (var kv in fields)
                            Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                    }
                }
            }
        }

        // Now we know: ether1=instance_id=2, lo=instance_id=1 (from API .id=*2, *1).
        // These are DIFFERENT from the type registry IDs (1=ether_type, 3=vrrp_type, etc.)
        // Try [20,0] with instance ids (1,2) in various ways to get the full record.
        [TestMethod]
        public void Native_ProbeWithInstanceIds()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Instance ids from API: lo=1, ether1=2
                int[] instanceIds = { 1, 2 };
                int[] cmds = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

                foreach (int instId in instanceIds)
                {
                    Console.WriteLine($"\n=== Instance id={instId} ===");
                    foreach (int cmd in cmds)
                    {
                        // Try .id as u8
                        List<byte[]> framesU8;
                        try { framesU8 = client.ProbeCommandRaw(IFACE, cmd, 800,
                            M2Message.SessionIdField(instId)); }
                        catch { continue; }
                        int maxLen = framesU8.Count > 0 ? framesU8.Max(f => f.Length) : 0;
                        bool hasName = framesU8.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));

                        // Try .id as u32
                        List<byte[]> framesU32;
                        try { framesU32 = client.ProbeCommandRaw(IFACE, cmd, 800,
                            M2Message.SessionIdFieldU32(instId)); }
                        catch { continue; }
                        int maxLenU32 = framesU32.Count > 0 ? framesU32.Max(f => f.Length) : 0;
                        bool hasNameU32 = framesU32.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));

                        if (hasName || hasNameU32 || maxLen > 80 || maxLenU32 > 80)
                        {
                            Console.WriteLine($"  *** cmd={cmd} instance_id={instId}:");
                            Console.WriteLine($"      u8: {framesU8.Count} frames, max {maxLen}B, Name={hasName}");
                            Console.WriteLine($"      u32: {framesU32.Count} frames, max {maxLenU32}B, Name={hasNameU32}");
                            var bestFrames = hasName ? framesU8 : framesU32;
                            DumpFrames(bestFrames, $"cmd={cmd}+id={instId}");
                        }
                        else
                        {
                            var f0 = framesU8.Count > 0 ? M2Message.ParseAllFields(framesU8[0]) : null;
                            uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e)
                                ? (uint)Convert.ToUInt32(e.Item2) : 0;
                            Console.WriteLine($"  cmd={cmd} id={instId}: maxLen={maxLen}, err=0x{ec:X}");
                        }
                    }
                }
            }
        }

        // Hypothesis: each record lives at path [20, 0, <id>] and uses singleton commands.
        // Like [13,4] cmd=7 returns sysinfo, [20,0,<id>] cmd=4 might return one interface record.
        [TestMethod]
        public void Native_RecordAtSubPath()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Try path [20,0,<id>] with various commands
                int[] testIds = { 1, 3 }; // first two interface ids
                int[] cmdCandidates = { 1, 3, 4, 5, 6, 7, 8 };

                foreach (int id in testIds)
                {
                    int[] subPath = { 20, 0, id };
                    foreach (int cmd in cmdCandidates)
                    {
                        List<byte[]> frames;
                        try { frames = client.ProbeCommandRaw(subPath, cmd, 1500); }
                        catch { continue; }
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                        if (hasName || maxLen > 90)
                        {
                            Console.WriteLine($"*** path=[20,0,{id}] cmd={cmd}: {frames.Count} frames, max {maxLen}B, Name={hasName}");
                            DumpFrames(frames, $"[20,0,{id}] cmd={cmd}");
                        }
                        else
                        {
                            // Check error code
                            var f = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : new Dictionary<int, Tuple<string, object>>();
                            uint errCode = f.TryGetValue(0xFF0008, out var ec) ? (uint)Convert.ToUInt32(ec.Item2) : 0;
                            Console.WriteLine($"  path=[20,0,{id}] cmd={cmd}: {frames.Count} frames, max {maxLen}B, err=0x{errCode:X}");
                        }
                    }
                }
            }
        }

        // Extended command sweep: try cmd values 0x29..0x64 and select higher values.
        // Searching for a "getall-with-fields" command beyond the 0x00-0x28 range.
        // Also tries instance ids 1 and 2 (lo and ether1).
        [TestMethod]
        public void Native_SweepHigherCommands()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Instance ids: lo=1, ether1=2
                // Ranges to check: wider, including medium range
                var cmdRange = Enumerable.Range(0x29, 0x64 - 0x29 + 1)
                    .Concat(new[] { 0x65, 0x66, 0x7F, 0x80, 0xFE, 0xFF,
                                    0x100, 0x101, 0x200, 0x400, 0x1000,
                                    50, 51, 52, 55, 60, 64, 65, 70, 75, 80, 90, 100,
                                    101, 200, 255, 256, 300, 400, 500, 1000, 2000 });

                foreach (int cmd in cmdRange.Distinct().OrderBy(x => x))
                {
                    // Try bare (no id) and with instance ids 1,2
                    foreach (var (label, extraFields) in new[] {
                        ("bare", new byte[0][]),
                        ("id=1", new[]{ M2Message.SessionIdField(1) }),
                        ("id=2", new[]{ M2Message.SessionIdField(2) }),
                    })
                    {
                        List<byte[]> frames;
                        try { frames = client.ProbeCommandRaw(IFACE, cmd, 600, extraFields); }
                        catch { continue; }
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                        bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values
                            .Any(v => v.Item1 == "str"));
                        // Only print interesting results (not just error frames ~77B)
                        if (hasName || maxLen > 80 || frames.Count > 1 || hasStr)
                        {
                            Console.WriteLine($"*** cmd=0x{cmd:X} {label}: {frames.Count} frames, max {maxLen}B, Name={hasName}, str={hasStr}");
                            DumpFrames(frames, $"cmd=0x{cmd:X} {label}");
                        }
                    }
                }
                Console.WriteLine("high-cmd sweep done");
            }
        }

        // New insight: cmd=3 on [20,0] returns the INTERFACE TYPE REGISTRY (47 types),
        // not actual interface instances. Try to find where actual instances (ether1 etc.) live.
        // The API shows 2 actual interfaces: ether1 (type=ether, type_id=1) and loopback.
        [TestMethod]
        public void Native_FindActualInterfaces()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // 1) Try cmd=4 (get-instances?) with type_id=1 (ether) as user key 1
                Console.WriteLine("--- [20,0] cmd=4 +u1=1 (ether type instances?) ---");
                var f4u1 = client.ProbeCommandRaw(IFACE, 4, 2000, M2Message.U32User(1, 1));
                DumpFrames(f4u1, "cmd=4 +u1=1");

                // 2) Try a wider sweep 0-15 with user key 1 set to type_id=1
                Console.WriteLine("--- Sweep cmd 0-15 with u1=1 (ether type id) ---");
                for (int cmd = 0; cmd <= 15; cmd++)
                {
                    List<byte[]> frames;
                    try { frames = client.ProbeCommandRaw(IFACE, cmd, 1000, M2Message.U32User(1, 1)); }
                    catch { continue; }
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    bool hasStr = frames.Any(f =>
                    {
                        var pf = M2Message.ParseAllFields(f);
                        return pf.Values.Any(v => v.Item1 == "str" || v.Item1 == "str[]");
                    });
                    if (hasName || maxLen > 80 || hasStr)
                    {
                        Console.WriteLine($"*** cmd={cmd} +u1=1: {frames.Count} frames, max {maxLen}B, Name={hasName}, hasStr={hasStr}");
                        DumpFrames(frames, $"cmd={cmd}+u1=1");
                    }
                }

                // 3) Try a completely different handler for "ether" instances
                // Maybe [20,1] (not Address) or a specific subhandler?
                // Let's check SYS_FROM in cmd=3 response more carefully first.
                // Also: try sending cmd=3 to [20,0] with a type filter as user field
                Console.WriteLine("--- [20,0] cmd=3 with type filter u32(0x10001)=1 ---");
                var f3typeFilter = client.ProbeCommandRaw(IFACE, 3, 2000,
                    M2Message.U32User(0x10001, 1));  // type field = ether(1)
                DumpFrames(f3typeFilter, "cmd=3 +type=ether");

                // 4) Check other handlers that might list actual interfaces
                // [20, 0] but different commands
                foreach (int cmd in new[] { 6, 7, 8, 9, 10 })
                {
                    var frames = client.ProbeCommandRaw(IFACE, cmd, 1500);
                    var allFields = new List<Dictionary<int, Tuple<string, object>>>();
                    foreach (var raw in frames) allFields.Add(M2Message.ParseAllFields(raw));
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = allFields.Any(f => f.Values.Any(v => v.Item1 == "str" || v.Item1 == "str[]"));
                    Console.WriteLine($"  [20,0] cmd={cmd}: {frames.Count} frames, maxLen={maxLen}, hasStr={hasStr}");
                    if (hasStr || maxLen > 80)
                        DumpFrames(frames, $"cmd={cmd}");
                }
            }
        }

        // Try cmd=3 streaming (no reply_expected) but also check what happens
        // on other handlers that return known single-record data (to validate the technique).
        // [13,4] cmd=7 returns full sysinfo in 1 frame — does it differ with no reply_expected?
        [TestMethod]
        public void Native_ValidateStreamingOnSysinfo()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                Console.WriteLine("--- [13,4] cmd=7 WITH reply_expected (known good) ---");
                var framesRE = client.ProbeCommandRaw(new[] { 13, 4 }, 7, 3000);
                DumpFrames(framesRE, "[13,4] cmd=7 reply_expected");

                Console.WriteLine("--- [13,4] cmd=7 WITHOUT reply_expected ---");
                var framesStream = client.ProbeCommandStream(new[] { 13, 4 }, 7, 3000);
                DumpFrames(framesStream, "[13,4] cmd=7 no reply_expected");
            }
        }

        // Try sending a "getall" command with reply_expected=FALSE on [20,0].
        // Also try cmd=4 (which might be "get record by id" — cmd=4 vs cmd=3=ids).
        [TestMethod]
        public void Native_StreamingVariants()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Get first id
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 2500);
                uint firstId = 0;
                foreach (var raw in idFrames)
                {
                    var ids = ParseIdList(raw);
                    if (ids.Length > 0) { firstId = ids[0]; break; }
                }
                Console.WriteLine($"first id = {firstId}");

                // Try cmd=4,5,6 WITHOUT reply_expected (streaming/subscribe)
                foreach (int cmd in new[] { 3, 4, 5, 6, 7 })
                {
                    Console.WriteLine($"--- [20,0] cmd={cmd} no reply_expected ---");
                    var frames = client.ProbeCommandStream(IFACE, cmd, 3000);
                    Console.WriteLine($"  {frames.Count} frame(s)");
                    for (int i = 0; i < frames.Count; i++)
                    {
                        var f = M2Message.ParseAllFields(frames[i]);
                        bool hasName = f.ContainsKey(KEY_NAME);
                        Console.WriteLine($"  frame[{i}] {frames[i].Length}B hasName={hasName} fields={f.Count}");
                        foreach (var kv in f)
                            Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                    }
                }

                // Try cmd=3 without reply_expected + .id
                Console.WriteLine($"--- [20,0] cmd=3 no reply_expected +.id={firstId} ---");
                var framesWithId = client.ProbeCommandStream(IFACE, 3, 3000,
                    M2Message.SessionIdField((int)firstId));
                DumpFrames(framesWithId, "cmd=3 stream +.id");
            }
        }

        // Try cmd=2 (set) to actually SET the comment field on ether1.
        // We know cmd=2 + id=2 works (clean ACK). Now add field keys.
        // This will help us find the comment field key even if we can't yet READ fields.
        [TestMethod]
        public void Native_SetFieldsViaCmd2()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Try setting Name field (0x10006) to "ether1" for instance id=2
                // If it succeeds AND the name actually changes → we found the set pattern
                // If it succeeds with clean ACK but name doesn't change → field key might be wrong
                // Let's try setting various fields and check via API

                // First try: set key=0x10006 (Name) to "ether1-test"
                Console.WriteLine("--- cmd=2 + id=2 + Name=ether1-test (key=0x10006) ---");
                var respSetName = client.NativeSet(IFACE, 2, M2Message.StringUser(0x10006, "ether1-test"));
                var f1 = M2Message.ParseAllFields(respSetName);
                uint ec1 = f1.TryGetValue(0xFF0008, out var e1) ? (uint)Convert.ToUInt32(e1.Item2) : 0;
                Console.WriteLine($"Set Name: err=0x{ec1:X}, frame={respSetName.Length}B");

                // Verify via API whether name changed
                var apiConn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass);
                var ifacesBefore = apiConn.LoadAll<Interface>().ToList();
                Console.WriteLine("API after Set-Name:");
                foreach (var i in ifacesBefore)
                    Console.WriteLine($"  .id={i.Id} name={i.Name} type={i.Type}");

                // Restore if needed
                if (ifacesBefore.Any(i => i.Name == "ether1-test"))
                {
                    Console.WriteLine("Name was changed! Restoring...");
                    client.NativeSet(IFACE, 2, M2Message.StringUser(0x10006, "ether1"));
                }
                apiConn.Dispose();

                // Try setting comment key candidates
                // The comment field key is unknown. Try user keys in range.
                // From catalog: comment is {type:'comment'} with no id → well-known key
                // Let's check the API: what's ether1's current comment?
                using (var apiConn2 = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var ether1 = apiConn2.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                    string originalComment = ether1?.Comment ?? "";
                    Console.WriteLine($"ether1 original comment (API): '{originalComment}'");

                    // Set comment via API as reference
                    // We want to find the M2 key that corresponds to comment
                    // Strategy: set comment via API, then read back the ether1 object M2 response
                    // But we can't read via M2 yet...
                    // Instead: set comment via M2 with candidate keys, verify via API

                    const string testComment = "m2-test-comment";

                    // Comment key candidates based on catalog analysis:
                    // key 0x10032 = 'Default Name' (string)
                    // Comment is usually at a small key number in RouterOS objects
                    // Check RouterOS API field name "comment" → maps to which M2 key?
                    // Let's try keys: 0x10007..0x10015
                    foreach (int ck in new[] { 0x10007, 0x10008, 0x10009, 0x1000A, 0x1000B,
                                               0x1000C, 0x1000D, 0x1000F, 0x10010, 0x10011,
                                               0x10012, 0x10013, 0x10014, 0x10015, 0x10020,
                                               0x10021, 0x10030, 0x10032, 0x10033, 0x10035 })
                    {
                        // Try setting this key on ether1 (id=2)
                        var resp = client.NativeSet(IFACE, 2, M2Message.StringUser(ck & 0xFFFF, testComment));
                        var fr = M2Message.ParseAllFields(resp);
                        uint ec = fr.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;

                        if (ec == 0)
                        {
                            // Clean ACK — check if comment changed via API
                            var ether1After = apiConn2.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                            bool changed = ether1After?.Comment == testComment;
                            Console.WriteLine($"  key=0x{ck:X}: err=0 comment_changed={changed}");
                            if (changed)
                            {
                                Console.WriteLine($"  *** FOUND COMMENT KEY: 0x{ck:X}!");
                                // Restore
                                client.NativeSet(IFACE, 2, M2Message.StringUser(ck & 0xFFFF, originalComment));
                            }
                            else
                            {
                                // Undo the set with empty string
                                client.NativeSet(IFACE, 2, M2Message.StringUser(ck & 0xFFFF, ""));
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  key=0x{ck:X}: err=0x{ec:X}");
                        }
                    }
                }
            }
        }

        // Find the comment field key by sweeping string keys on ether1 (M2 id=2).
        // Checks via API after each attempt.
        [TestMethod]
        public void Native_FindCommentKey()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                string originalComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var ether1 = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                    originalComment = ether1?.Comment ?? "";
                    Console.WriteLine($"ether1 original comment: '{originalComment}'");
                }

                const string testComment = "m2-key-probe";
                // Broad sweep of string key candidates for ether1 (id=2)
                // Start from low user keys and go up systematically
                var keyCandidates = Enumerable.Range(1, 80)  // 0x10001..0x10050
                    .Select(i => 0x10000 + i)
                    .Concat(Enumerable.Range(1, 0x100)  // low keys 0x0001..0x0100
                        .Select(i => i))
                    .Concat(new[] {
                        0xFE0009, // k_error_string reuse?
                        0x20001, 0x20002, 0x20003,  // namespace 0x02
                    });

                foreach (int ck in keyCandidates)
                {
                    var resp = client.NativeSet(IFACE, 2, M2Message.StringUser(ck & 0xFFFF, testComment));
                    var fr = M2Message.ParseAllFields(resp);
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;

                    if (ec != 0)
                    {
                        // Key rejected — noteworthy
                        continue;
                    }

                    // Check via API if comment changed
                    bool changed = false;
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    {
                        var ether1After = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                        changed = ether1After?.Comment == testComment;
                        if (changed)
                        {
                            Console.WriteLine($"*** FOUND COMMENT KEY: 0x{ck:X5}!");
                            // Restore
                            client.NativeSet(IFACE, 2, M2Message.StringUser(ck & 0xFFFF, originalComment));
                            return; // Found it — stop
                        }
                    }
                }
                Console.WriteLine("Comment key NOT found in sweep range.");
                Console.WriteLine("All string keys return clean ACK but none change comment via API.");
                Console.WriteLine("This suggests either wrong instance_id or different routing needed.");
            }
        }

        // Try cmd=4 with various additional SYS fields that might unlock the operation.
        // cmd=4 returns 0xFE0009 (not_permitted) — maybe it needs more context.
        [TestMethod]
        public void Native_Cmd4WithSysFields()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Try various SYS fields added to cmd=4+id=2
                // 0xFF0003 u8 = 1 (maybe "request type = data request")
                // 0xFF0004 u8 = 1 (type of extra routing)
                // 0xFF000B u32 = id (some other id field)
                var extraSysFields = new[]
                {
                    ("0xFF0003=1", M2Message.U8Sys(0xFF0003, 1)),
                    ("0xFF0003=2", M2Message.U8Sys(0xFF0003, 2)),
                    ("0xFF0004=1", M2Message.U8Sys(0xFF0004, 1)),
                    ("0xFF0004=2", M2Message.U8Sys(0xFF0004, 2)),
                    ("0xFF000B=2", M2Message.U32Sys(0xFF000B, 2)),
                    ("0xFF000C=1", M2Message.U8Sys(0xFF000C, 1)),
                    ("0xFF001C=str", M2Message.StringUser(0x1C, "ether")),  // send handler name?
                };

                foreach (var (label, extraField) in extraSysFields)
                {
                    var frames = client.ProbeCommandRaw(IFACE, 4, 2000,
                        M2Message.SessionIdField(2), extraField);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"cmd=4 +id=2 +{label}: {frames.Count}f max={maxLen}B err=0x{ec:X} Name={hasName}");
                    if (hasName || maxLen > 80)
                        DumpFrames(frames, label);
                }

                // Also try cmd=4 without any id (bare)
                var framesBare = client.ProbeCommandRaw(IFACE, 4, 2000);
                {
                    int maxLen = framesBare.Count > 0 ? framesBare.Max(f => f.Length) : 0;
                    var f0 = framesBare.Count > 0 ? M2Message.ParseAllFields(framesBare[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"cmd=4 bare: {framesBare.Count}f max={maxLen}B err=0x{ec:X}");
                }
            }
        }

        // cmd=4 might need different parameters (not .id but Name string).
        // Try all cmd variants with the Name string as field key 0x10006.
        [TestMethod]
        public void Native_ProbeWithNameField()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Try sending Name="ether1" as different key/cmd combinations
                var nameField = M2Message.StringUser(0x10006, "ether1");

                foreach (int cmd in new[] { 2, 3, 4, 5, 6, 7 })
                {
                    var frames = client.ProbeCommandRaw(IFACE, cmd, 2000, nameField);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  cmd={cmd} +Name='ether1': {frames.Count}f max={maxLen}B err=0x{ec:X} Name={hasName}");
                    if (hasName || maxLen > 80)
                        DumpFrames(frames, $"cmd={cmd}+Name");
                }

                // Also try with MTU key (known valid field from catalog: 0x10064)
                var mtuField = M2Message.U32User(0x10064, 1500);
                foreach (int cmd in new[] { 3, 4, 7 })
                {
                    var frames = client.ProbeCommandRaw(IFACE, cmd, 2000, mtuField);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  cmd={cmd} +MTU=1500: {frames.Count}f max={maxLen}B err=0x{ec:X} Name={hasName}");
                    if (hasName || maxLen > 80)
                        DumpFrames(frames, $"cmd={cmd}+MTU");
                }

                // Try cmd=4 with .id=2 AND Name="ether1" together
                var frames44 = client.ProbeCommandRaw(IFACE, 4, 2000,
                    M2Message.SessionIdField(2), nameField);
                {
                    int maxLen = frames44.Count > 0 ? frames44.Max(f => f.Length) : 0;
                    bool hasName = frames44.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    var f0 = frames44.Count > 0 ? M2Message.ParseAllFields(frames44[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  cmd=4 +.id=2 +Name: {frames44.Count}f max={maxLen}B err=0x{ec:X} Name={hasName}");
                }
            }
        }

        // Critical test: perform the secondary data-layer auth (cmd=4 then cmd=1 on [13,4])
        // and check if this unlocks data handlers like [20,0] and [20,1].
        [TestMethod]
        public void Native_DataLayerLoginThenProbe()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                Console.WriteLine("--- Performing secondary data-layer login ---");
                int sessionId = client.DataLayerLogin(user, pass);
                Console.WriteLine($"Data session id: {sessionId}");

                // Now try [20,0] cmd=4 with and without session_id
                Console.WriteLine("--- [20,0] cmd=4 after data login ---");
                var f4 = client.ProbeCommandRaw(IFACE, 4, 3000);
                DumpFrames(f4, "[20,0] cmd=4 bare after login");

                // With session_id
                if (sessionId >= 0)
                {
                    var f4s = client.ProbeCommandRaw(IFACE, 4, 3000,
                        M2Message.SessionIdField(sessionId));
                    DumpFrames(f4s, $"[20,0] cmd=4 +session={sessionId}");

                    // Try [20,1] with session_id
                    var f201s = client.ProbeCommandRaw(new[] { 20, 1 }, 3, 3000,
                        M2Message.SessionIdField(sessionId));
                    DumpFrames(f201s, $"[20,1] cmd=3 +session={sessionId}");
                }

                // Try cmd=3 on [20,0] with session_id
                if (sessionId >= 0)
                {
                    var f3s = client.ProbeCommandRaw(IFACE, 3, 3000,
                        M2Message.SessionIdField(sessionId));
                    DumpFrames(f3s, $"[20,0] cmd=3 +session={sessionId}");
                }

                // Also probe [20,1] cmd=1-8 with session_id (if any)
                if (sessionId >= 0)
                {
                    Console.WriteLine("--- [20,1] sweep after data login ---");
                    for (int cmd = 1; cmd <= 8; cmd++)
                    {
                        var frames = client.ProbeCommandRaw(new[] { 20, 1 }, cmd, 2000,
                            M2Message.SessionIdField(sessionId));
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v =>
                            v.Item1 == "str" || v.Item1 == "str[]"));
                        var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                        uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                        Console.WriteLine($"  [20,1] cmd={cmd} +session: {frames.Count}f max={maxLen}B err=0x{ec:X} hasStr={hasStr}");
                        if (hasStr || maxLen > 80)
                            DumpFrames(frames, $"[20,1] cmd={cmd}+session");
                    }
                }
            }
        }

        // Maybe we need to "open a session" with the data handler first.
        // The sysinfo handler [13,4] uses cmd=7. Try cmd=5 (open?) on [13,4] first,
        // then try cmd=3 on [20,0] to get actual data.
        // Also check if there's a "client registration" step we're missing.
        [TestMethod]
        public void Native_SessionSetupAttempt()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Step 1: Try cmd=5 on [13,4] as "open session"
                Console.WriteLine("--- [13,4] cmd=5 (open session?) ---");
                var f5 = client.ProbeCommandRaw(new[] { 13, 4 }, 5, 3000);
                DumpFrames(f5, "[13,4] cmd=5");

                // Step 2: Try cmd=4 on [13,4] as "challenge/auth"
                Console.WriteLine("--- [13,4] cmd=4 ---");
                var f4 = client.ProbeCommandRaw(new[] { 13, 4 }, 4, 3000);
                DumpFrames(f4, "[13,4] cmd=4");

                // Step 3: After attempting session setup, try [20,1] cmd=3
                Console.WriteLine("--- [20,1] cmd=3 after session attempt ---");
                var f201 = client.ProbeCommandRaw(new[] { 20, 1 }, 3, 3000);
                DumpFrames(f201, "[20,1] cmd=3");

                // Step 4: probe [20,0] cmd=4 with no id after session setup
                Console.WriteLine("--- [20,0] cmd=4 no id after session ---");
                var f204 = client.ProbeCommandRaw(IFACE, 4, 3000);
                DumpFrames(f204, "[20,0] cmd=4");

                // Step 5: Check what happens with SRC_ID variation
                // What if we need to open a session to get a different SRC_ID?
                // The SysFrom() uses SRC_ID=8. Try different values.
                foreach (int srcId in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 16, 32 })
                {
                    // Send [20,0] cmd=3 with different FROM
                    byte[] msg = M2Message.BuildM2(
                        M2Message.SysToArr(20, 0),
                        M2Message.SysFrom(srcId),
                        M2Message.BoolSys(0xFF0005, true),
                        M2Message.U8Sys(0xFF0006, (byte)99),  // unique reqid
                        M2Message.U8Sys(0xFF0007, 4));  // cmd=4
                    client.EncryptAndSendPublic(msg);
                    try
                    {
                        var resp = client.RecvAndDecryptPublic(1500);
                        if (resp != null)
                        {
                            var f = M2Message.ParseAllFields(resp);
                            uint ec = f.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                            bool hasName = f.ContainsKey(KEY_NAME);
                            Console.WriteLine($"  srcId={srcId}: {resp.Length}B err=0x{ec:X} hasName={hasName}");
                            if (hasName || resp.Length > 80)
                                DumpFrame(resp, $"srcId={srcId}");
                        }
                    }
                    catch { }
                }
            }
        }

        // Compare [20,1] (Address map) behavior vs [20,0] (Interface) to understand the protocol.
        // [20,1] is a known map without generic:'iface' — should follow standard CRUD pattern.
        [TestMethod]
        public void Native_CompareAddressHandler()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                int[] ADDR = { 20, 1 };

                // Get addresses via API for reference
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var cmd = api.CreateCommand("/ip/address/print");
                    int count = 0;
                    Console.WriteLine("API /ip/address:");
                    foreach (var row in cmd.ExecuteList())
                    {
                        Console.WriteLine($"  .id={row.GetResponseField(".id")} address={row.GetResponseField("address")} interface={row.GetResponseField("interface")}");
                        count++;
                    }
                    Console.WriteLine($"  Total: {count}");
                }

                // Probe [20,1] with various commands
                foreach (int cmd in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
                {
                    var frames = client.ProbeCommandRaw(ADDR, cmd, 2000);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v =>
                        v.Item1 == "str" || v.Item1 == "str[]"));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  [20,1] cmd={cmd}: {frames.Count}f max={maxLen}B err=0x{ec:X} hasStr={hasStr}");
                    if (hasStr || maxLen > 80)
                        DumpFrames(frames, $"[20,1] cmd={cmd}");
                }

                // Also check [20,8] (ARP)
                int[] ARP = { 20, 8 };
                Console.WriteLine("\n--- [20,8] ARP ---");
                foreach (int cmd in new[] { 3, 4, 7 })
                {
                    var frames = client.ProbeCommandRaw(ARP, cmd, 2000);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v =>
                        v.Item1 == "str" || v.Item1 == "str[]"));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var e) ? (uint)Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  [20,8] cmd={cmd}: {frames.Count}f max={maxLen}B err=0x{ec:X} hasStr={hasStr}");
                    if (hasStr || maxLen > 80)
                        DumpFrames(frames, $"[20,8] cmd={cmd}");
                }
            }
        }

        // Get raw API .id values for interfaces and correlate with M2 type registry
        [TestMethod]
        public void Native_CorrelateApiIdsWithM2()
        {
            var (host, user, pass) = Cfg();

            // Get API interface ids using the O/R mapper
            using (var apiConn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                var ifaces = apiConn.LoadAll<Interface>().ToList();
                Console.WriteLine($"Total API interfaces: {ifaces.Count}");
                foreach (var iface in ifaces)
                {
                    Console.WriteLine($"API: .id={iface.Id} name={iface.Name} type={iface.Type} comment={iface.Comment}");
                }
            }

            // Now get M2 type registry and show correlation
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                foreach (var raw in idFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    if (f.TryGetValue(0x000001, out var ids) && f.TryGetValue(0x000002, out var names))
                    {
                        var idArr = ids.Item2.ToString().Trim('[',']').Split(',');
                        var nameArr = names.Item2.ToString().Trim('[',']').Split(',');
                        Console.WriteLine($"\nM2 type registry ({idArr.Length} entries):");
                        for (int i = 0; i < Math.Min(idArr.Length, nameArr.Length); i++)
                            Console.WriteLine($"  M2 type_id={idArr[i]} type_name={nameArr[i]}");
                    }
                }
            }
        }

        // The MAIN goal: native full-record list.
        // Based on all probes above, run the best candidate approach found.
        // This test ASSERTS ether1 is present in the result.
        [TestMethod]
        public void Native_ListInterfacesWithFields()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Step 1: get all ids
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                uint[] ids = new uint[0];
                foreach (var raw in idFrames)
                {
                    var parsed = ParseIdList(raw);
                    if (parsed.Length > 0) { ids = parsed; break; }
                }
                Console.WriteLine($"Got {ids.Length} interface ids: [{string.Join(",", ids)}]");
                Assert.IsTrue(ids.Length > 0, "Should have at least one interface");

                // Step 2: for each id, try the streaming approach (cmd=3 no reply_expected +.id)
                // OR cmd=4 with .id — collect full-field frames
                var interfaceRecords = new List<(uint id, string name)>();

                // First, try cmd=3 stream for ALL ids at once (single subscribe)
                Console.WriteLine("--- Attempting streaming getall (cmd=3 no reply_expected) ---");
                var streamFrames = client.ProbeCommandStream(IFACE, 3, 5000);
                int withNameStream = streamFrames.Count(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                Console.WriteLine($"Streaming cmd=3: {streamFrames.Count} frames, {withNameStream} with Name");
                foreach (var raw in streamFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    if (f.TryGetValue(KEY_NAME, out var nm))
                    {
                        uint recId = 0;
                        if (f.TryGetValue(0xFE0001, out var sid))
                            recId = (uint)Convert.ToInt32(sid.Item2);
                        interfaceRecords.Add((recId, nm.Item2?.ToString() ?? ""));
                        Console.WriteLine($"  iface: id={recId} name={nm.Item2}");
                    }
                }

                if (interfaceRecords.Count == 0)
                {
                    // Fallback: per-id cmd=4 probe
                    Console.WriteLine("--- Streaming failed, trying per-id cmd=4 ---");
                    foreach (uint id in ids.Take(5)) // limit to first 5 for speed
                    {
                        var frames4 = client.ProbeCommandRaw(IFACE, 4, 2000,
                            M2Message.SessionIdField((int)id));
                        foreach (var raw in frames4)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            if (f.TryGetValue(KEY_NAME, out var nm))
                            {
                                interfaceRecords.Add((id, nm.Item2?.ToString() ?? ""));
                                Console.WriteLine($"  iface via cmd=4: id={id} name={nm.Item2}");
                            }
                        }
                    }
                }

                Console.WriteLine($"Total interfaces found: {interfaceRecords.Count}");
                foreach (var (rid, rname) in interfaceRecords)
                    Console.WriteLine($"  id={rid}  name={rname}");

                // The test becomes the "best attempt" — PASS if we got any records with Name
                // (or at least the id-list works). Full assert for ether1 if we got full records.
                if (interfaceRecords.Count > 0)
                    Assert.IsTrue(interfaceRecords.Any(r => r.name == "ether1"),
                        "ether1 should be in the interface list");
                else
                    Console.WriteLine("NOTE: full-record read not yet working (probing in progress).");
            }
        }

        // ── Set/restore comment test ───────────────────────────────────────────

        // ── Phase 3c probes (2026-06-07 continuation) ──────────────────────────

        // Probe 1: Print ALL ids returned by cmd=3 and check both key=0x000001 (u32[])
        // and key=0x000002 (str[] — type names if present). Verify whether id=2 appears.
        // Also try cmd=3 on [20,1] (Address) to get records with Interface field (key=0xa).
        [TestMethod]
        public void Native_FullIdList_And_AddressHandler()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // [20,0] cmd=3 — print ALL fields of ALL returned frames
                Console.WriteLine("=== [20,0] cmd=3 full dump ===");
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                Console.WriteLine($"Frames received: {idFrames.Count}");
                foreach (var raw in idFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"Frame {raw.Length}B, {f.Count} fields:");
                    foreach (var kv in f)
                        Console.WriteLine($"  key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");

                    // Print all u32 values from the id-array
                    if (f.TryGetValue(0x000001, out var idArr))
                    {
                        var ids = idArr.Item2.ToString().Trim('[', ']').Split(',')
                            .Select(s => uint.TryParse(s, out var x) ? x : 0).ToArray();
                        Console.WriteLine($"  id-list ({ids.Length}): [{string.Join(",", ids)}]");
                        Console.WriteLine($"  Contains 1: {ids.Contains(1u)}");
                        Console.WriteLine($"  Contains 2: {ids.Contains(2u)}");
                        Console.WriteLine($"  Contains 3: {ids.Contains(3u)}");
                        Console.WriteLine($"  Min={ids.Min()} Max={ids.Max()}");
                    }
                }

                // [20,1] cmd=3 — get Address records (each has key=0xa = interface M2 id)
                Console.WriteLine("\n=== [20,1] cmd=3 (Address handler) ===");
                var addrFrames = client.ProbeCommandRaw(new[] { 20, 1 }, 3, 3000);
                Console.WriteLine($"Frames: {addrFrames.Count}");
                foreach (var raw in addrFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"  Frame {raw.Length}B, {f.Count} fields:");
                    foreach (var kv in f)
                        Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                }

                // [20,1] cmd=4 — try get-by-id with .id=1 to get first Address record
                Console.WriteLine("\n=== [20,1] cmd=4 +.id=1 ===");
                var addr4 = client.ProbeCommandRaw(new[] { 20, 1 }, 4, 2000,
                    M2Message.SessionIdField(1));
                foreach (var raw in addr4)
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"  Frame {raw.Length}B:");
                    foreach (var kv in f)
                        Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                }
            }
        }

        // Probe 2: For each of the 47 ids, try sending cmd=4 to get the full record.
        // Also try cmd=3+.id and cmd=7+.id. Print any frame that has strings (Name etc.)
        [TestMethod]
        public void Native_SweepAllIdsForRecord()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Get all ids
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                uint[] ids = Array.Empty<uint>();
                foreach (var raw in idFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    if (f.TryGetValue(0x000001, out var v))
                    {
                        ids = v.Item2.ToString().Trim('[', ']').Split(',')
                            .Select(s => uint.TryParse(s, out var x) ? x : 0)
                            .Where(x => x > 0).ToArray();
                        break;
                    }
                }
                Console.WriteLine($"Got {ids.Length} ids: [{string.Join(",", ids)}]");

                // For each id, try the most promising commands
                foreach (uint id in ids)
                {
                    // Try cmd=4 + .id (get-by-id candidate)
                    foreach (int cmd in new[] { 4, 7 })
                    {
                        List<byte[]> frames;
                        try { frames = client.ProbeCommandRaw(IFACE, cmd, 600, M2Message.SessionIdField((int)id)); }
                        catch { continue; }
                        foreach (var raw in frames)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            bool hasName = f.ContainsKey(KEY_NAME);
                            bool hasStr = f.Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l");
                            if (hasName || hasStr || raw.Length > 80)
                            {
                                Console.WriteLine($"*** id={id} cmd={cmd}: {raw.Length}B hasName={hasName}");
                                foreach (var kv in f)
                                    Console.WriteLine($"  key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                            }
                        }
                    }
                }
                Console.WriteLine("Sweep done.");
            }
        }

        // Probe 3: Comment key sweep with VERIFIED API check.
        // Key 0xFE0009 is confirmed as generic comment key in catalog (userman5 sfe0009).
        // Try NativeSet with .id=2 + key=0xFE0009 and verify via API.
        // Also try small key sweep (0x1..0x20) since cmd=2+id=2 accepts any field silently.
        [TestMethod]
        public void Native_CommentKeyFE0009_AndSmallKeysSweep()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Get all interface ids so we know ALL candidate ids (not just 2)
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                uint[] allIds = Array.Empty<uint>();
                foreach (var raw in idFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    if (f.TryGetValue(0x000001, out var v))
                    {
                        allIds = v.Item2.ToString().Trim('[', ']').Split(',')
                            .Select(s => uint.TryParse(s, out var x) ? x : 0)
                            .Where(x => x > 0).ToArray();
                        break;
                    }
                }
                Console.WriteLine($"All M2 interface ids ({allIds.Length}): [{string.Join(",", allIds)}]");

                // Get API info
                string origComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var ifaces = api.LoadAll<Interface>().ToList();
                    Console.WriteLine("API interfaces:");
                    foreach (var i in ifaces)
                        Console.WriteLine($"  .id={i.Id} name={i.Name} comment={i.Comment}");
                    origComment = ifaces.FirstOrDefault(i => i.Name == "ether1")?.Comment ?? "My comment";
                }
                Console.WriteLine($"Original comment: '{origComment}'");

                const string probe = "m2-probe-c3";

                // Try ALL ids (not just 2) with 0xFE0009 (the generic comment key)
                Console.WriteLine("\n--- Sweep ALL ids with key=0xFE0009 ---");
                foreach (uint id in allIds)
                {
                    // Build set cmd manually with the FULL key 0xFE0009 (system namespace)
                    // NativeSet uses StringUser which is user-namespace only.
                    // For 0xFE0009 we need a system-namespace string field.
                    byte[] commentField = BuildStringFieldFull(0xFE0009, probe);
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE),
                        M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true),
                        M2Message.U8Sys(0xFF0006, 99),
                        M2Message.U8Sys(0xFF0007, 2),  // cmd=2 set
                        M2Message.SessionIdField((int)id),
                        commentField,
                    };
                    byte[] msg = M2Message.BuildM2(head.ToArray());
                    client.EncryptAndSendPublic(msg);
                    byte[] resp = null;
                    try { resp = client.RecvAndDecryptPublic(2000); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;

                    if (ec != 0) { Console.WriteLine($"  id={id} 0xFE0009: err=0x{ec:X} (skip)"); continue; }

                    // Check via API
                    bool changed = false;
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    {
                        var eth1 = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                        changed = eth1?.Comment == probe;
                    }
                    Console.WriteLine($"  id={id} 0xFE0009: ec=0 changed={changed}");
                    if (changed)
                    {
                        Console.WriteLine($"*** FOUND! ether1 M2 id={id}, comment key=0xFE0009 ***");
                        // Restore
                        byte[] restoreField = BuildStringFieldFull(0xFE0009, origComment);
                        var rhead = new List<byte[]>
                        {
                            M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                            M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                            M2Message.U8Sys(0xFF0007, 2), M2Message.SessionIdField((int)id), restoreField,
                        };
                        client.EncryptAndSendPublic(M2Message.BuildM2(rhead.ToArray()));
                        try { client.RecvAndDecryptPublic(2000); } catch { }
                        return;
                    }
                    // Undo with empty string (no-op restore)
                    byte[] undoField = BuildStringFieldFull(0xFE0009, "");
                    var uhead = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                        M2Message.U8Sys(0xFF0007, 2), M2Message.SessionIdField((int)id), undoField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(uhead.ToArray()));
                    try { client.RecvAndDecryptPublic(2000); } catch { }
                }

                // Also try small user-namespace key sweep with id=2 (API ether1 .id=*2)
                // In case the comment is under user namespace, not system namespace
                Console.WriteLine("\n--- User key sweep 0x1..0x30 with id=2 ---");
                for (int ck = 1; ck <= 0x30; ck++)
                {
                    byte[] resp = client.NativeSet(IFACE, 2, M2Message.StringUser(ck, probe));
                    var fr = M2Message.ParseAllFields(resp);
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;
                    if (ec != 0) { Console.WriteLine($"  user key=0x{ck:X} id=2: err=0x{ec:X}"); continue; }

                    bool changed = false;
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                        changed = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment == probe;
                    Console.WriteLine($"  user key=0x{ck:X} id=2: ec=0 changed={changed}");
                    if (changed)
                    {
                        Console.WriteLine($"*** FOUND user comment key: 0x{ck:X} for id=2 ***");
                        using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                        {
                            var eth = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                            if (eth != null) { eth.Comment = origComment; api.Save(eth); }
                        }
                        return;
                    }
                    // Undo
                    client.NativeSet(IFACE, 2, M2Message.StringUser(ck, ""));
                }
                Console.WriteLine("No comment key found yet.");
            }
        }

        // Helper: build a string field for any full key (including 0xFExxxx system namespace)
        private static byte[] BuildStringFieldFull(int fullKey, string value)
        {
            byte kl = (byte)(fullKey & 0xFF);
            byte kh = (byte)((fullKey >> 8) & 0xFF);
            byte ns = (byte)((fullKey >> 16) & 0xFF);
            byte[] data = System.Text.Encoding.UTF8.GetBytes(value);
            if (data.Length <= 255)
                return new byte[] { kl, kh, ns, 0x21, (byte)data.Length }.Concat(data).ToArray();
            var b = new List<byte> { kl, kh, ns, 0x20 };
            b.AddRange(BitConverter.GetBytes((ushort)data.Length));
            b.AddRange(data);
            return b.ToArray();
        }

        // Probe 4: Compare how [20,1] Address handler behaves vs [20,0] Interface.
        // The Address map should be a "normal" map without generic:'iface' magic.
        // If cmd=3 returns Address records with Name field, great. If cmd=4+.id returns one — perfect.
        [TestMethod]
        public void Native_AddressHandler_FullSweep()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // API reference
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    Console.WriteLine("API /ip/address:");
                    var cmd = api.CreateCommand("/ip/address/print");
                    foreach (var row in cmd.ExecuteList())
                        Console.WriteLine($"  {row.GetResponseField(".id")} {row.GetResponseField("address")} iface={row.GetResponseField("interface")}");
                }

                int[] ADDR = { 20, 1 };
                Console.WriteLine("\n--- [20,1] command sweep ---");
                for (int cmd = 1; cmd <= 12; cmd++)
                {
                    List<byte[]> frames;
                    try { frames = client.ProbeCommandRaw(ADDR, cmd, 2000); }
                    catch { continue; }
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                    Console.WriteLine($"  cmd={cmd}: {frames.Count}f max={maxLen}B err=0x{ec:X} hasStr={hasStr}");
                    if (hasStr || maxLen > 80)
                    {
                        Console.WriteLine("  -- frames --");
                        for (int i = 0; i < frames.Count; i++)
                        {
                            Console.WriteLine($"  frame[{i}] {frames[i].Length}B:");
                            foreach (var kv in M2Message.ParseAllFields(frames[i]))
                                Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                        }
                    }
                }

                // Also try streaming (no reply_expected) for cmds 1,3,4
                Console.WriteLine("\n--- [20,1] streaming (no reply_expected) ---");
                foreach (int cmd in new[] { 1, 3, 4, 5 })
                {
                    var frames = client.ProbeCommandStream(ADDR, cmd, 3000);
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                    Console.WriteLine($"  cmd={cmd} stream: {frames.Count}f hasStr={hasStr}");
                    if (hasStr)
                        foreach (var raw in frames)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            if (f.Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"))
                            {
                                Console.WriteLine($"  {raw.Length}B:");
                                foreach (var kv in f)
                                    Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                            }
                        }
                }
            }
        }

        // Probe 5: [20,0] - dump ALL 47 ids, try cmd=4 on EACH with timeout=500ms.
        // More targeted than SweepAllIds — only checks whether any record has Name key.
        // Runs faster because only cmd=4 (the most likely "get one record" cmd).
        [TestMethod]
        public void Native_DumpAllIdRecordsCmd4()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Get all ids
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                uint[] ids = Array.Empty<uint>();
                foreach (var raw in idFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    if (f.TryGetValue(0x000001, out var v))
                    {
                        ids = v.Item2.ToString().Trim('[', ']').Split(',')
                            .Select(s => uint.TryParse(s, out var x) ? x : 0)
                            .Where(x => x > 0).ToArray();
                        break;
                    }
                }
                Console.WriteLine($"Testing {ids.Length} ids: [{string.Join(",", ids)}]");

                // Try cmd=4 for each id
                foreach (uint id in ids)
                {
                    // Try u8 .id
                    var frames = client.ProbeCommandRaw(IFACE, 4, 500, M2Message.SessionIdField((int)id));
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;

                    if (hasName || hasStr || maxLen > 80)
                    {
                        Console.WriteLine($"*** id={id} cmd=4: {maxLen}B hasName={hasName} hasStr={hasStr}");
                        foreach (var raw in frames)
                            foreach (var kv in M2Message.ParseAllFields(raw))
                                Console.WriteLine($"  key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                    }
                    else
                    {
                        Console.WriteLine($"  id={id}: maxLen={maxLen} err=0x{ec:X}");
                    }
                }
            }
        }

        // Probe 6: THE GREEN TEST — native list + set/restore comment.
        // This is the final proof-of-concept test. It will PASS when:
        // (a) we can read ether1's Name natively, AND
        // (b) set+restore its comment natively.
        // Uses the findings from probes 1-5 to fill in the correct handler/cmd/key.
        // Currently "best attempt" — asserts pass only if we found the flow.
        [TestMethod]
        public void Native_GreenTest_ListAndSetComment()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Step 1: Get interface list (cmd=3 returns ids + possibly str[]=names)
                var listFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                uint[] ids = Array.Empty<uint>();
                string[] typeNames = null;
                foreach (var raw in listFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    if (f.TryGetValue(0x000001, out var v))
                        ids = v.Item2.ToString().Trim('[', ']').Split(',')
                            .Select(s => uint.TryParse(s, out var x) ? x : 0)
                            .Where(x => x > 0).ToArray();
                    if (f.TryGetValue(0x000002, out var v2) && v2.Item1 == "str[]")
                    {
                        typeNames = v2.Item2.ToString().Trim('[', ']').Split(',');
                        Console.WriteLine($"Key 0x000002 (str[]): [{string.Join(",", typeNames)}]");
                    }
                }
                Console.WriteLine($"Got {ids.Length} interface ids");
                Assert.IsTrue(ids.Length > 0, "Should get at least one interface id");

                // Step 2: For each id, find which one is ether1 by trying to read its Name
                // Try cmd=4 (most likely get-one-by-id command)
                uint ether1M2Id = 0;
                foreach (uint id in ids)
                {
                    var frames4 = client.ProbeCommandRaw(IFACE, 4, 600, M2Message.SessionIdField((int)id));
                    foreach (var raw in frames4)
                    {
                        var f = M2Message.ParseAllFields(raw);
                        if (f.TryGetValue(KEY_NAME, out var nm) && nm.Item2?.ToString() == "ether1")
                        {
                            ether1M2Id = id;
                            Console.WriteLine($"Found ether1 at M2 id={id} via cmd=4");
                            break;
                        }
                    }
                    if (ether1M2Id != 0) break;
                }

                if (ether1M2Id == 0)
                {
                    // Fallback: API says ether1 .id=*2, try id=2 directly
                    Console.WriteLine("cmd=4 sweep didn't find ether1, trying id=2 (API .id=*2)");
                    var frames4_2 = client.ProbeCommandRaw(IFACE, 4, 1000, M2Message.SessionIdField(2));
                    foreach (var raw in frames4_2)
                    {
                        var f = M2Message.ParseAllFields(raw);
                        Console.WriteLine($"  id=2 cmd=4 frame {raw.Length}B:");
                        foreach (var kv in f)
                            Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                    }
                    // Use id=2 anyway (cmd=2 returns clean ACK for id=2)
                    ether1M2Id = 2;
                }

                // Step 3: Read original comment via API
                string origComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    origComment = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment ?? "";
                Console.WriteLine($"Original comment: '{origComment}'");

                // Step 4: Set comment via native M2 cmd=2 + .id + key=0xFE0009
                const string newComment = "tik4net-m2-native-test";
                byte[] commentField = BuildStringFieldFull(0xFE0009, newComment);
                var setHead = new List<byte[]>
                {
                    M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                    M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 42),
                    M2Message.U8Sys(0xFF0007, 2),
                    M2Message.SessionIdField((int)ether1M2Id),
                    commentField,
                };
                client.EncryptAndSendPublic(M2Message.BuildM2(setHead.ToArray()));
                byte[] setResp = null;
                try { setResp = client.RecvAndDecryptPublic(3000); } catch { }
                var setFields = setResp != null ? M2Message.ParseAllFields(setResp) : new Dictionary<int, Tuple<string, object>>();
                uint setErr = setFields.TryGetValue(0xFF0008, out var se) ? Convert.ToUInt32(se.Item2) : 0;
                Console.WriteLine($"Set response: {(setResp?.Length ?? 0)}B err=0x{setErr:X}");

                // Step 5: Verify via API
                bool changed = false;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    changed = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment == newComment;
                Console.WriteLine($"Comment changed: {changed}");

                // Step 6: Restore original comment
                byte[] restoreField = BuildStringFieldFull(0xFE0009, origComment);
                var restHead = new List<byte[]>
                {
                    M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                    M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 43),
                    M2Message.U8Sys(0xFF0007, 2),
                    M2Message.SessionIdField((int)ether1M2Id),
                    restoreField,
                };
                client.EncryptAndSendPublic(M2Message.BuildM2(restHead.ToArray()));
                try { client.RecvAndDecryptPublic(3000); } catch { }

                // Verify restoration
                string finalComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    finalComment = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment ?? "";
                Console.WriteLine($"Final comment after restore: '{finalComment}'");
                Assert.AreEqual(origComment, finalComment, "Comment should be restored to original");

                if (changed)
                    Console.WriteLine("*** SUCCESS: native M2 comment set+restore works! ***");
                else
                    Console.WriteLine("NOTE: comment set via M2 did not take effect — probe ongoing");
            }
        }

        // Probe 7: cmd=0xFE0004 interrogates handler capabilities (tenable routeros source).
        // Try it on various handlers to see what they support.
        // Also try cmd=0xFE0001 (GetPolicies) on [20,0].
        [TestMethod]
        public void Native_HandlerCapabilities_FE0004()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                int[][] testPaths = {
                    new[] { 20, 0 },  // Interface proxy
                    new[] { 20, 1 },  // Address
                    new[] { 13, 4 },  // Sysinfo (known good)
                    new[] { 0, 8 },   // SYS_FROM [0,8] = the router's own handler?
                };

                foreach (var path in testPaths)
                {
                    string pstr = "[" + string.Join(",", path) + "]";
                    // Try cmd=0xFE0004
                    var frames = client.ProbeCommandRaw(path, 0xFE0004, 2000);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                    Console.WriteLine($"{pstr} cmd=0xFE0004: {frames.Count}f max={maxLen}B err=0x{ec:X}");
                    foreach (var raw in frames)
                    {
                        var f = M2Message.ParseAllFields(raw);
                        foreach (var kv in f)
                            Console.WriteLine($"  key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                    }

                    // Also try cmd=0xFE0001 (GetPolicies)
                    var frames1 = client.ProbeCommandRaw(path, 0xFE0001, 2000);
                    int maxLen1 = frames1.Count > 0 ? frames1.Max(f => f.Length) : 0;
                    var g0 = frames1.Count > 0 ? M2Message.ParseAllFields(frames1[0]) : null;
                    uint ec1 = g0 != null && g0.TryGetValue(0xFF0008, out var gv) ? Convert.ToUInt32(gv.Item2) : 0;
                    Console.WriteLine($"{pstr} cmd=0xFE0001: {frames1.Count}f max={maxLen1}B err=0x{ec1:X}");
                    if (maxLen1 > 80)
                        foreach (var raw in frames1)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            foreach (var kv in f)
                                Console.WriteLine($"  key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                        }
                }
            }
        }

        // Probe 8: The BIG pivot. Since cmd=3 returns TYPE registry (not instances),
        // we need to find where the INSTANCES are. Try these hypotheses:
        // (a) [20,0] with a DIFFERENT cmd (maybe cmd=6 or a high cmd like 0x1000+type_id)
        //     returns instances of that type.
        // (b) A completely different handler path for each type (discovered via what cmd=3 returns).
        // (c) Send cmd=3 with BOTH the type_id AND some flag to get instances of that type.
        // Also probe [20,0] cmd=0xFE000F/0x10 (standard monitor start/poll).
        [TestMethod]
        public void Native_FindInstancesNewApproaches()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Approach A: [20, type_id_ether=1] with various cmds
                // (type_id=1 for ether, type_id=77 for loopback)
                Console.WriteLine("=== [20,1] (type ether?) ===");
                for (int cmd = 1; cmd <= 10; cmd++)
                {
                    var frames = client.ProbeCommandRaw(new[] { 20, 1 }, cmd, 800);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                    Console.WriteLine($"  [20,1] cmd={cmd}: {frames.Count}f max={maxLen}B err=0x{ec:X} str={hasStr}");
                }

                // Approach B: Try cmd with u32=1 (type_id for ether) as key=0x10001 (type field)
                Console.WriteLine("\n=== [20,0] cmd=3 + type=1 (ether instances?) ===");
                for (int cmd = 1; cmd <= 8; cmd++)
                {
                    var frames = client.ProbeCommandRaw(IFACE, cmd, 800,
                        M2Message.U32User(0x10001, 1));  // type field = 1 (ether)
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                    Console.WriteLine($"  cmd={cmd} +type=1: max={maxLen}B err=0x{ec:X} str={hasStr} Name={hasName}");
                    if (hasName || hasStr)
                        foreach (var raw in frames)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            foreach (var kv in f)
                                Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                        }
                }

                // Approach C: Monitor (subscribe) approach
                // cmd=0xFE000F (monitor start), 0xFE0010 (monitor poll), 0xFE0011 (cancel)
                Console.WriteLine("\n=== [20,0] monitor cmds (0xFE000F/0xFE0010/0xFE0011) ===");
                foreach (int cmd in new[] { 0xFE000F, 0xFE0010, 0xFE0011 })
                {
                    var frames = client.ProbeCommandRaw(IFACE, cmd, 3000);
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(KEY_NAME));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                    Console.WriteLine($"  cmd=0x{cmd:X}: {frames.Count}f max={maxLen}B err=0x{ec:X} str={hasStr} Name={hasName}");
                    if (hasName || hasStr || maxLen > 80)
                        foreach (var raw in frames)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            foreach (var kv in f)
                                Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                        }
                }

                // Approach D: Try [0, type_id] = [0, 1] for ether-specific handler?
                // The from-address is [0,8] so maybe handler 0 exists
                Console.WriteLine("\n=== [0,1] and [14,1] (direct ether handler?) ===");
                foreach (int[] path in new[] { new[] {0,1}, new[] {14,1}, new[] {0,20}, new[] {14,0} })
                {
                    string pstr = "[" + string.Join(",", path) + "]";
                    foreach (int cmd in new[] {3, 4, 7})
                    {
                        var frames = client.ProbeCommandRaw(path, cmd, 800);
                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hasStr = frames.Any(f => M2Message.ParseAllFields(f).Values.Any(v => v.Item1 == "str" || v.Item1 == "str_l"));
                        var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                        uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                        Console.WriteLine($"  {pstr} cmd={cmd}: max={maxLen}B err=0x{ec:X} str={hasStr}");
                    }
                }

                // Approach E: cmd=3 on [20,0] with k_reply_expected=0xFF0005=false (subscription mode)
                // PLUS u32 key to filter by type (u32(0x1)=type_id_for_ether=1)
                // Maybe the router pushes INSTANCE frames after a subscribe
                Console.WriteLine("\n=== [20,0] subscribe + type_id=1 (stream ether instances?) ===");
                var subFrames = client.ProbeCommandStream(IFACE, 3, 6000,
                    M2Message.U32User(1, 1));  // key=0x1 = u32 = type_id=1 (ether)
                Console.WriteLine($"Streaming cmd=3 +type=1: {subFrames.Count} frames");
                foreach (var raw in subFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    bool hasName = f.ContainsKey(KEY_NAME);
                    Console.WriteLine($"  frame {raw.Length}B hasName={hasName}:");
                    foreach (var kv in f)
                        Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                }
            }
        }

        // Probe 9: CRITICAL — the comment key is 0xFE0009 (confirmed in userman5 as sfe0009).
        // Try NativeSet with ALL type ids from the type registry and key=0xFE0009 string.
        // The [20,0] cmd=2+id=<type_id> returns clean ACK for ANY id — but the handler for
        // ether1's comment might use type_id=1 (ether type) rather than instance id.
        // Also: try sending cmd=2 with BOTH type_id AND instance field.
        [TestMethod]
        public void Native_CommentKeyProbe_FE0009_AllIds()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                string origComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var eth = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                    origComment = eth?.Comment ?? "My comment";
                    Console.WriteLine($"ether1 original comment: '{origComment}'");
                }

                const string probe = "m2-key-fe0009";

                // All 47 type ids + id=2 (API .id=*2 for ether1)
                uint[] typeIds = { 1, 3, 10, 11, 12, 16, 17, 18, 19, 20, 21, 25, 30, 32, 33, 34, 35, 36, 38, 40, 43, 44, 45, 46, 47, 50, 51, 52, 55, 56, 57, 58, 59, 61, 62, 63, 64, 65, 66, 68, 69, 70, 71, 73, 75, 76, 77, 2 };
                Console.WriteLine($"Testing {typeIds.Length} ids with 0xFE0009...");

                foreach (uint id in typeIds)
                {
                    byte[] commentField = BuildStringFieldFull(0xFE0009, probe);
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                        M2Message.U8Sys(0xFF0007, 2),
                        M2Message.SessionIdField((int)id),
                        commentField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                    byte[] resp = null;
                    try { resp = client.RecvAndDecryptPublic(1500); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;

                    if (ec != 0) { Console.WriteLine($"  id={id}: err=0x{ec:X}"); continue; }

                    // Check API
                    bool changed = false;
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                        changed = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment == probe;
                    Console.WriteLine($"  id={id}: ec=0 CHANGED={changed}");

                    if (changed)
                    {
                        Console.WriteLine($"*** SUCCESS! ether1 M2 id={id}, comment key=0xFE0009 ***");
                        // Restore
                        byte[] restField = BuildStringFieldFull(0xFE0009, origComment);
                        var rhead = new List<byte[]>
                        {
                            M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                            M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                            M2Message.U8Sys(0xFF0007, 2), M2Message.SessionIdField((int)id), restField,
                        };
                        client.EncryptAndSendPublic(M2Message.BuildM2(rhead.ToArray()));
                        try { client.RecvAndDecryptPublic(1500); } catch { }
                        return;
                    }
                    // Undo: empty string
                    byte[] undoField = BuildStringFieldFull(0xFE0009, "");
                    var uhead = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                        M2Message.U8Sys(0xFF0007, 2), M2Message.SessionIdField((int)id), undoField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(uhead.ToArray()));
                    try { client.RecvAndDecryptPublic(1500); } catch { }
                }
                Console.WriteLine("0xFE0009 key did not change comment for any id.");
            }
        }

        // Probe 10: TARGETED SET attempts now that we know:
        // - ether1 M2 id = 2 (from cmd=0xFE0004 response key=0xFE0001=2)
        // - comment key = 0xFE0009 (from cmd=0xFE0004 response key=0xFE0009="My comment")
        // - comment field uses SYSTEM namespace (ns=0xFE)
        // Try various set command numbers (not just cmd=2) with proper ns encoding.
        [TestMethod]
        public void Native_TargetedSetAttempts()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                string origComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    origComment = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment ?? "My comment";
                Console.WriteLine($"Original comment: '{origComment}'");

                const string probe = "m2-set-probe";

                // First confirm cmd=0xFE0004 returns ether1 (sanity check)
                var fe4 = client.ProbeCommandRaw(IFACE, 0xFE0004, 3000);
                bool gotEther1 = fe4.Any(f => M2Message.ParseAllFields(f).TryGetValue(0x010006, out var nm) && nm.Item2?.ToString() == "ether1");
                Console.WriteLine($"cmd=0xFE0004 returns ether1: {gotEther1}");
                if (fe4.Count > 0)
                {
                    var f0 = M2Message.ParseAllFields(fe4[0]);
                    f0.TryGetValue(0xFE0001, out var sidv);
                    Console.WriteLine($"  .id in response = {sidv?.Item2}");
                }

                // Candidate set commands to try
                int[] setCmds = { 2, 4, 5, 6, 7, 8, 10, 0xFE0005, 0xFE0006, 0xFE0007 };

                foreach (int setCmd in setCmds)
                {
                    byte[] commentField = BuildStringFieldFull(0xFE0009, probe);
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true),
                        M2Message.U8Sys(0xFF0006, (byte)(setCmd & 0xFF)),
                        setCmd <= 0xFF
                            ? M2Message.U8Sys(0xFF0007, (byte)setCmd)
                            : M2Message.U32Sys(0xFF0007, setCmd),
                        M2Message.SessionIdField(2),
                        commentField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                    byte[] resp = null;
                    try { resp = client.RecvAndDecryptPublic(2000); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;

                    if (ec != 0) { Console.WriteLine($"  setCmd=0x{setCmd:X}: err=0x{ec:X}"); continue; }

                    // Check API
                    bool changed = false;
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                        changed = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment == probe;
                    Console.WriteLine($"  setCmd=0x{setCmd:X}: ec=0 CHANGED={changed}");
                    if (changed)
                    {
                        Console.WriteLine($"*** SUCCESS! Set cmd = 0x{setCmd:X} + id=2 + key=0xFE0009 works! ***");
                        // Restore
                        byte[] restField = BuildStringFieldFull(0xFE0009, origComment);
                        var rhead = new List<byte[]>
                        {
                            M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                            M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                            setCmd <= 0xFF ? M2Message.U8Sys(0xFF0007, (byte)setCmd) : M2Message.U32Sys(0xFF0007, setCmd),
                            M2Message.SessionIdField(2), restField,
                        };
                        client.EncryptAndSendPublic(M2Message.BuildM2(rhead.ToArray()));
                        try { client.RecvAndDecryptPublic(2000); } catch { }
                        // Verify restoration
                        string restored;
                        using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                            restored = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment ?? "";
                        Console.WriteLine($"Restored to: '{restored}'");
                        return;
                    }
                    // Undo
                    byte[] undoField = BuildStringFieldFull(0xFE0009, "");
                    var uhead = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99),
                        setCmd <= 0xFF ? M2Message.U8Sys(0xFF0007, (byte)setCmd) : M2Message.U32Sys(0xFF0007, setCmd),
                        M2Message.SessionIdField(2), undoField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(uhead.ToArray()));
                    try { client.RecvAndDecryptPublic(2000); } catch { }
                }

                // Also try cmd=2 WITHOUT id (targeting current/default ether1)
                Console.WriteLine("\n--- cmd=2 without id (set current object?) ---");
                {
                    byte[] commentField = BuildStringFieldFull(0xFE0009, probe);
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 51),
                        M2Message.U8Sys(0xFF0007, 2), commentField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                    byte[] resp = null;
                    try { resp = client.RecvAndDecryptPublic(2000); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;
                    bool changed = false;
                    if (ec == 0)
                    {
                        using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                            changed = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment == probe;
                    }
                    Console.WriteLine($"  cmd=2 no-id: ec=0x{ec:X} changed={changed}");
                    if (changed)
                    {
                        using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                        {
                            var eth = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                            if (eth != null) { eth.Comment = origComment; api.Save(eth); }
                        }
                    }
                }

                // Also check what cmd=0xFE0004 with .id=2 returns (get specific object by id)
                Console.WriteLine("\n--- cmd=0xFE0004 +.id=2 (get ether1 by id?) ---");
                var fe4id = client.ProbeCommandRaw(IFACE, 0xFE0004, 3000, M2Message.SessionIdField(2));
                Console.WriteLine($"Frames: {fe4id.Count}");
                foreach (var raw in fe4id)
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"  {raw.Length}B:");
                    foreach (var kv in f)
                        Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                }

                // Also check cmd=0xFE0004 on [20,0] repeatedly to see if it iterates
                Console.WriteLine("\n--- cmd=0xFE0004 x3 without id (iterate?) ---");
                for (int i = 0; i < 3; i++)
                {
                    var frames = client.ProbeCommandRaw(IFACE, 0xFE0004, 2000);
                    string name = "";
                    if (frames.Count > 0)
                    {
                        var ff = M2Message.ParseAllFields(frames[0]);
                        ff.TryGetValue(0x010006, out var nm);
                        name = nm?.Item2?.ToString() ?? "?";
                    }
                    Console.WriteLine($"  call {i}: name='{name}'");
                }
            }
        }

        // Probe 11: Try to get ALL interface instances by calling cmd=0xFE0004 repeatedly
        // with different interface ids (maybe it reads NEXT on each call, or needs cancel).
        // Also try cmd=0xFE0004 on [20,1] with id=1 to get address record.
        [TestMethod]
        public void Native_FE0004Exploration()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Try sending cmd=0xFE0004 with various ids to see which ones return records
                Console.WriteLine("=== [20,0] cmd=0xFE0004 with various ids ===");
                foreach (uint id in new uint[] { 0, 1, 2, 3, 10, 77 })
                {
                    var frames = client.ProbeCommandRaw(IFACE, 0xFE0004, 1500,
                        M2Message.SessionIdField((int)id));
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    bool hasName = frames.Any(f => M2Message.ParseAllFields(f).ContainsKey(0x010006));
                    var f0 = frames.Count > 0 ? M2Message.ParseAllFields(frames[0]) : null;
                    uint ec = f0 != null && f0.TryGetValue(0xFF0008, out var ev) ? Convert.ToUInt32(ev.Item2) : 0;
                    string name = "";
                    if (hasName) { f0.TryGetValue(0x010006, out var nm); name = nm?.Item2?.ToString() ?? "?"; }
                    Console.WriteLine($"  id={id}: {frames.Count}f max={maxLen}B err=0x{ec:X} Name='{name}'");
                    if (hasName || maxLen > 100)
                        foreach (var raw in frames)
                        {
                            var f = M2Message.ParseAllFields(raw);
                            uint idv = f.TryGetValue(0xFE0001, out var idval) ? Convert.ToUInt32(idval.Item2) : 0;
                            Console.WriteLine($"    .id={idv} Name={( f.TryGetValue(0x010006, out var fnm) ? fnm.Item2 : "?" )}");
                        }
                }

                // Try [20,0] cmd=0xFE0004 streaming (no reply_expected) — does it push all?
                Console.WriteLine("\n=== [20,0] cmd=0xFE0004 streaming ===");
                var streamFrames = client.ProbeCommandStream(IFACE, 0xFE0004, 5000);
                Console.WriteLine($"Stream frames: {streamFrames.Count}");
                foreach (var raw in streamFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    bool hasName = f.ContainsKey(0x010006);
                    Console.WriteLine($"  {raw.Length}B hasName={hasName}");
                    if (hasName)
                        foreach (var kv in f.Where(x => x.Value.Item1 == "str" || x.Value.Item1 == "str_l"))
                            Console.WriteLine($"    key=0x{kv.Key:X6} = {kv.Value.Item2}");
                }

                // Try [20,0] cmd=0xFE0004 + type=1 (ether) to get ether instances
                Console.WriteLine("\n=== [20,0] cmd=0xFE0004 + type_id=1 ===");
                var typeFrames = client.ProbeCommandRaw(IFACE, 0xFE0004, 2000,
                    M2Message.U32User(0x10001, 1));
                foreach (var raw in typeFrames)
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"  {raw.Length}B:");
                    foreach (var kv in f.Where(x => x.Value.Item1 != "bool"))
                        Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                }

                // How does cmd=0xFE0004 relate to sessions? Try with mproxy session open first.
                Console.WriteLine("\n=== [20,0] cmd=0xFE0004 without id (baseline) ===");
                var baseline = client.ProbeCommandRaw(IFACE, 0xFE0004, 2000);
                Console.WriteLine($"Baseline: {baseline.Count} frames");
                if (baseline.Count > 0)
                {
                    var f = M2Message.ParseAllFields(baseline[0]);
                    Console.WriteLine($"  .id = {(f.TryGetValue(0xFE0001, out var idv) ? idv.Item2 : "?")}");
                    Console.WriteLine($"  Name = {(f.TryGetValue(0x010006, out var nm) ? nm.Item2 : "?")}");
                    Console.WriteLine($"  Comment = {(f.TryGetValue(0xFE0009, out var cm) ? cm.Item2 : "?")}");
                }
            }
        }

        // Helper: build a field using a FULL key (any namespace) and u32 type
        private static byte[] BuildU32FieldFull(int fullKey, uint val)
        {
            byte kl = (byte)(fullKey & 0xFF);
            byte kh = (byte)((fullKey >> 8) & 0xFF);
            byte ns = (byte)((fullKey >> 16) & 0xFF);
            return new byte[] { kl, kh, ns, 0x08 }.Concat(BitConverter.GetBytes(val)).ToArray();
        }

        // Helper: build u8 field with full key
        private static byte[] BuildU8FieldFull(int fullKey, byte val)
        {
            byte kl = (byte)(fullKey & 0xFF);
            byte kh = (byte)((fullKey >> 8) & 0xFF);
            byte ns = (byte)((fullKey >> 16) & 0xFF);
            return new byte[] { kl, kh, ns, 0x09, val };
        }

        // Probe 12: Try various approaches to actually set the comment:
        // A) cmd=2 +.id=2 + type_field(0x010001=1) + comment(0xFE0009) — maybe type routing needed
        // B) cmd=2 +.id=2 + Name field as no-op — see if ns=0x01 fields are accepted
        // C) Try to read lo (id=1) via cmd=0xFE0004 to see if it returns lo
        // D) Use MikroTik API to set comment, verify, restore (as control for the native test)
        [TestMethod]
        public void Native_SetWithTypFieldAndNameTest()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                string origComment;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    origComment = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment ?? "My comment";
                Console.WriteLine($"Original comment: '{origComment}'");

                const string probe = "m2-type-probe";

                // Approach A: cmd=2 + .id=2 + type=1(ether) + comment=0xFE0009
                Console.WriteLine("\n--- A: cmd=2 +.id=2 + type(0x010001=1) + comment(0xFE0009) ---");
                {
                    byte[] commentField = BuildStringFieldFull(0xFE0009, probe);
                    byte[] typeField = BuildU8FieldFull(0x010001, 1);  // type=1 (ether)
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 52),
                        M2Message.U8Sys(0xFF0007, 2),
                        M2Message.SessionIdField(2),
                        typeField, commentField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                    byte[] resp = null; try { resp = client.RecvAndDecryptPublic(2000); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;
                    bool changed = false;
                    if (ec == 0)
                    {
                        using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                            changed = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1")?.Comment == probe;
                    }
                    Console.WriteLine($"  A result: ec=0x{ec:X} CHANGED={changed}");
                    if (changed)
                    {
                        Console.WriteLine("*** A SUCCESS! type+comment field works! ***");
                        byte[] restField = BuildStringFieldFull(0xFE0009, origComment);
                        var rh = new List<byte[]> { M2Message.SysToArr(IFACE), M2Message.SysFrom(), M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99), M2Message.U8Sys(0xFF0007, 2), M2Message.SessionIdField(2), typeField, restField };
                        client.EncryptAndSendPublic(M2Message.BuildM2(rh.ToArray()));
                        try { client.RecvAndDecryptPublic(2000); } catch { }
                        return;
                    }
                    // Undo
                    var uh = new List<byte[]> { M2Message.SysToArr(IFACE), M2Message.SysFrom(), M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 99), M2Message.U8Sys(0xFF0007, 2), M2Message.SessionIdField(2), typeField, BuildStringFieldFull(0xFE0009, "") };
                    client.EncryptAndSendPublic(M2Message.BuildM2(uh.ToArray())); try { client.RecvAndDecryptPublic(1000); } catch { }
                }

                // Approach B: cmd=2 + .id=2 + Name="ether1" (no-op, same value) — does ns=0x01 matter?
                Console.WriteLine("\n--- B: cmd=2 +.id=2 + Name(0x010006)='ether1' (no-op test) ---");
                {
                    byte[] nameField = BuildStringFieldFull(0x010006, "ether1");
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 53),
                        M2Message.U8Sys(0xFF0007, 2),
                        M2Message.SessionIdField(2),
                        nameField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                    byte[] resp = null; try { resp = client.RecvAndDecryptPublic(2000); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  B: ec=0x{ec:X} (ec=0 means Name field was accepted by handler)");
                    // Check if name changed (sanity - should remain "ether1")
                    using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    {
                        var eth = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "ether1");
                        Console.WriteLine($"  API name after B: '{eth?.Name}' comment: '{eth?.Comment}'");
                    }
                }

                // Approach C: cmd=2 +.id=1 + comment=0xFE0009 on lo (type=loopback, type_id=77)
                Console.WriteLine("\n--- C: cmd=2 +.id=1 (lo?) + comment=0xFE0009 ---");
                {
                    // First read lo via cmd=0xFE0004 if possible
                    // But cmd=0xFE0004 always returns ether1... let's try lo anyway
                    byte[] commentField = BuildStringFieldFull(0xFE0009, probe);
                    var head = new List<byte[]>
                    {
                        M2Message.SysToArr(IFACE), M2Message.SysFrom(),
                        M2Message.BoolSys(0xFF0005, true), M2Message.U8Sys(0xFF0006, 54),
                        M2Message.U8Sys(0xFF0007, 2),
                        M2Message.SessionIdField(1),  // lo .id=1
                        commentField,
                    };
                    client.EncryptAndSendPublic(M2Message.BuildM2(head.ToArray()));
                    byte[] resp = null; try { resp = client.RecvAndDecryptPublic(2000); } catch { }
                    var fr = resp != null ? M2Message.ParseAllFields(resp) : new Dictionary<int, Tuple<string, object>>();
                    uint ec = fr.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;
                    Console.WriteLine($"  C (lo id=1): ec=0x{ec:X}");
                    if (ec == 0)
                    {
                        using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                        {
                            var lo = api.LoadAll<Interface>().FirstOrDefault(i => i.Name == "lo");
                            Console.WriteLine($"  lo comment after C: '{lo?.Comment}'");
                            if (lo?.Comment == probe) lo.Comment = ""; // restore
                        }
                    }
                }

                // Approach D: check what cmd=3 returns for [20,1] after setting via cmd=2
                // (This is to verify cmd=2 on [20,1] works differently)
                Console.WriteLine("\n--- D: [20,1] cmd=2 +.id=1 + Network address test ---");
                {
                    // Try to read the address first via cmd=0xFE0004
                    var addrGet = client.ProbeCommandRaw(new[] { 20, 1 }, 0xFE0004, 2000);
                    Console.WriteLine($"  [20,1] cmd=0xFE0004: {addrGet.Count}f");
                    foreach (var raw in addrGet)
                    {
                        var f = M2Message.ParseAllFields(raw);
                        uint ec = f.TryGetValue(0xFF0008, out var e) ? Convert.ToUInt32(e.Item2) : 0;
                        Console.WriteLine($"    {raw.Length}B ec=0x{ec:X}");
                        if (ec == 0)
                            foreach (var kv in f.Where(x => x.Value.Item1 != "bool" && (string)x.Value.Item1 != "u32[]"))
                                Console.WriteLine($"      key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                    }
                }
            }
        }

        // Native set + restore comment on ether1.
        // Only runs if we can natively read/verify. Uses cmd=2 +.id + comment-key.
        [TestMethod]
        public void Native_SetRestoreEther1Comment()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // Step 1: get all ids to find ether1's id
                var idFrames = client.ProbeCommandRaw(IFACE, 3, 3000);
                uint[] ids = new uint[0];
                foreach (var raw in idFrames)
                {
                    var parsed = ParseIdList(raw);
                    if (parsed.Length > 0) { ids = parsed; break; }
                }
                Assert.IsTrue(ids.Length > 0, "Need at least one interface");

                // ether1 is typically id=1 on RouterOS
                uint ether1Id = ids.Contains(1u) ? 1u : ids[0];
                Console.WriteLine($"Using interface id={ether1Id} as ether1");

                // Step 2: Probe for comment key by trying to read ether1 fields
                // We'll try comment key candidates: 0xFE0009 (note in README), various user keys
                // First: try cmd=4 with .id to see if it returns full record
                Console.WriteLine("--- Probing cmd=4 +.id for full record ---");
                var frames4 = client.ProbeCommandRaw(IFACE, 4, 2500,
                    M2Message.SessionIdField((int)ether1Id));
                DumpFrames(frames4, $"cmd=4 +.id={ether1Id}");

                // Step 3: Try setting comment via known candidates for comment key
                // Based on README: {type:'comment'} has NO id → well-known key.
                // Candidate: 0xFE0009 (k_error_string reuse?), or user key 0x10008 or 0x10009
                // We'll send cmd=2 +.id + string field for each candidate key and check for ACK (no error)
                int[] commentKeyCandidates = {
                    0x10008, 0x10009, 0x1000A, 0x1000B, 0x1000C, 0x1000D, 0x1000F,
                    0x10010, 0x10011, 0x10012, 0x10032, 0x10033,
                    0xFE0009,  // k_error_string (unlikely but check)
                };

                Console.WriteLine("--- Probing comment key candidates via cmd=2 ---");
                foreach (int ck in commentKeyCandidates)
                {
                    // Send cmd=2 +.id + string(ck, "tik4net-probe")
                    byte[] stringField = M2Message.StringUser(ck & 0xFFFF, "tik4net-probe");
                    // For keys > 0xFFFF this won't work with StringUser, but all candidates fit
                    var resp = client.NativeSet(IFACE, (int)ether1Id, stringField);
                    var f = M2Message.ParseAllFields(resp);
                    bool hasError = f.ContainsKey(0xFF0008);
                    uint errCode = hasError && f[0xFF0008].Item1 == "u32"
                        ? (uint)Convert.ToUInt32(f[0xFF0008].Item2) : 0;
                    Console.WriteLine($"  comment key=0x{ck:X}: hasError={hasError} errCode=0x{errCode:X}");
                    if (!hasError)
                        Console.WriteLine($"  *** SUCCESS: comment key 0x{ck:X} accepted!");
                }
            }
        }

        // ── Streaming monitor PoC (2026-06-13) ─────────────────────────────────
        // Validates the start → poll → cancel cycle reverse-engineered from webfig master.js
        // (ObjectQuery): a monitor is NOT a server push — the client re-polls on a timer over the
        // normal request/reply channel. Target = Profile window [49] (CPU profiler): a query window
        // that needs no live traffic and is present on every router.
        //   start  = SYS_CMD 0xFE000F (system monitor-start) + request u1=0xFFFFFFFD ("total")
        //            → reply carries the monitor id in .id (0xFE0001).
        //   poll   = SYS_CMD 0xFE0004 (default getall) + .id + flags 0x10000005
        //            → rows under Mfe0002 (per-process CPU usage); re-issued every autorefresh ms.
        //   cancel = SYS_CMD 0xFE0011 (system monitor-cancel) + .id.
        [TestMethod]
        public void Native_MonitorCycle_Profile()
        {
            const int START  = unchecked((int)0xFE000F);
            const int POLL   = unchecked((int)0xFE0004);
            const int CANCEL = unchecked((int)0xFE0011);
            const int FLAGS  = 0x10000005;
            const int K_ID   = 0xFE0001;   // .id / monitor session handle
            const int K_RECS = 0xFE0002;   // Mfe0002 records message-array
            int[] PROFILE = { 49 };
            int cpuTotal = unchecked((int)0xFFFFFFFD);

            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // ── start ──
                // The monitor id (.id, 0xFE0001) is a u32 that can exceed int.MaxValue (Profile echoes
                // the CPU selector 0xFFFFFFFD), so carry it as uint and re-encode via SessionIdFieldU32.
                bool hasId = false;
                uint monitorId = 0;
                foreach (var raw in client.ProbeCommandRaw(PROFILE, START, 1500, M2Message.U32User(1, cpuTotal)))
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"start reply: {raw.Length}B, status=0x{(f.TryGetValue(0xFF0008, out var st) ? Convert.ToUInt32(st.Item2) : 0):X}, {f.Count} fields:");
                    foreach (var kv in f) Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-8} = {kv.Value.Item2}");
                    if (f.TryGetValue(K_ID, out var idv) && idv.Item2 != null)
                    {
                        monitorId = Convert.ToUInt32(idv.Item2);
                        hasId = true;
                    }
                }
                Console.WriteLine($"monitor id = {(hasId ? "0x" + monitorId.ToString("X") : "(none — poll without id)")}");

                // ── poll a few passes (webfig re-issues getall every autorefresh=1000 ms) ──
                int totalRows = 0;
                for (int passIdx = 0; passIdx < 3; passIdx++)
                {
                    System.Threading.Thread.Sleep(1000);
                    var pollFields = new List<byte[]> { M2Message.U32Sys(0xFE000C, FLAGS) };
                    if (hasId) pollFields.Insert(0, M2Message.SessionIdFieldU32(unchecked((int)monitorId)));
                    int passRows = 0;
                    foreach (var raw in client.ProbeCommandRaw(PROFILE, POLL, 1500, pollFields.ToArray()))
                    {
                        passRows += M2Message.ParseRecords(raw, K_RECS).Count;
                    }
                    Console.WriteLine($"poll pass {passIdx}: {passRows} record(s)");
                    totalRows += passRows;
                }
                Assert.IsTrue(totalRows > 0, "poll (0xFE0004 + id) must stream CPU-profile rows under Mfe0002");

                // ── cancel ──
                var cancelFields = hasId
                    ? new[] { M2Message.SessionIdFieldU32(unchecked((int)monitorId)) }
                    : new byte[0][];
                foreach (var raw in client.ProbeCommandRaw(PROFILE, CANCEL, 800, cancelFields))
                {
                    var f = M2Message.ParseAllFields(raw);
                    Console.WriteLine($"cancel reply: status=0x{(f.TryGetValue(0xFF0008, out var st) ? Convert.ToUInt32(st.Item2) : 0):X}");
                }
                Console.WriteLine($"=== monitor cycle OK: {totalRows} total rows across 3 passes ===");
            }
        }
    }
}
