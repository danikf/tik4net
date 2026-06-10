// WinboxInstanceProbeTest.cs — locate the handler/command that returns real interface
// INSTANCES (ether1, lo), not the type registry. [20,0] cmd=3 proved to be the interface
// TYPE registry (key 0x2 str[] = 47 type names). Decisive method: byte-scan responses for
// the ASCII "ether1" across handlers/commands/modes.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;

namespace tik4net.tests
{
    [Ignore("Exploratory RE probes (read-only sweeps) — manual only, hits a live router. Run via --filter.")]
    [TestClass]
    public class WinboxInstanceProbeTest
    {
        private const int WINBOX_PORT = 8291;

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        private static bool Contains(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }

        [TestMethod]
        public void Probe_ScanForEther1Instance()
        {
            var (host, user, pass) = Cfg();
            byte[] needle = Encoding.ASCII.GetBytes("ether1");

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                void Scan(int[] handler, int cmd, bool stream, params byte[][] extra)
                {
                    List<byte[]> frames;
                    try
                    {
                        frames = stream
                            ? client.ProbeCommandStream(handler, cmd, 1500, extra)
                            : client.ProbeCommandRaw(handler, cmd, 1500, extra);
                    }
                    catch (Exception ex) { Console.WriteLine($"[{string.Join(",", handler)}] cmd={cmd} EXC {ex.Message}"); return; }

                    bool hit = frames.Any(f => Contains(f, needle));
                    int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                    if (hit)
                    {
                        Console.WriteLine($"*** HIT [{string.Join(",", handler)}] cmd={cmd} stream={stream} extra={extra.Length}: {frames.Count}f max={maxLen}B");
                        foreach (var f in frames.Where(fr => Contains(fr, needle)))
                            foreach (var kv in M2Message.ParseAllFields(f))
                                Console.WriteLine($"      key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                    }
                    else if (maxLen > 80)
                        Console.WriteLine($"    [{string.Join(",", handler)}] cmd={cmd} stream={stream} extra={extra.Length}: {frames.Count}f max={maxLen}B (no ether1)");
                }

                // 1) [20,0] all commands, reply + stream
                Console.WriteLine("=== [20,0] cmd sweep ===");
                for (int cmd = 0; cmd <= 0x14; cmd++) { Scan(IFACE0, cmd, false); Scan(IFACE0, cmd, true); }

                // 2) [20,0] cmd=3 with a type filter (ether = type id 1, key 0x10001)
                Console.WriteLine("=== [20,0] cmd=3 + type filter ===");
                Scan(IFACE0, 3, false, M2Message.U32User(0x10001, 1));
                Scan(IFACE0, 3, false, M2Message.U32ArrayUser(1, 1));

                // 3) sub-handlers [20,1]..[20,8] cmd=3 (per-type instance lists?)
                Console.WriteLine("=== [20,x] sub-handler sweep ===");
                for (int sub = 1; sub <= 8; sub++) { Scan(new[] { 20, sub }, 3, false); Scan(new[] { 20, sub }, 3, true); }

                // 4) a few neighbor top-handlers cmd=3 (interface-ish candidates)
                Console.WriteLine("=== neighbor handler sweep ===");
                foreach (int h in new[] { 19, 21, 22, 23, 24, 18 })
                { Scan(new[] { h, 0 }, 3, false); Scan(new[] { h, 1 }, 3, false); }

                Console.WriteLine("scan done");
            }
        }

        // Cross-check on a KNOWN instance: IP address handler [20,1]. Router IP=192.168.4.236.
        // Find the command that returns IP instances → same command lists interfaces on [20,0].
        [TestMethod]
        public void Probe_ScanForIpAddressInstance()
        {
            var (host, user, pass) = Cfg();
            byte[] needle = Encoding.ASCII.GetBytes("192.168.4");
            int[] IPADDR = { 20, 1 };

            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                for (int cmd = 0; cmd <= 0x14; cmd++)
                {
                    foreach (bool stream in new[] { false, true })
                    {
                        List<byte[]> frames;
                        try
                        {
                            frames = stream
                                ? client.ProbeCommandStream(IPADDR, cmd, 1500)
                                : client.ProbeCommandRaw(IPADDR, cmd, 1500);
                        }
                        catch (Exception ex) { Console.WriteLine($"cmd={cmd} EXC {ex.Message}"); continue; }

                        int maxLen = frames.Count > 0 ? frames.Max(f => f.Length) : 0;
                        bool hit = frames.Any(f => Contains(f, needle));
                        // dump field keys for any non-trivial response
                        if (hit || maxLen > 80)
                        {
                            Console.WriteLine($"{(hit ? "*** HIT" : "    big")} [20,1] cmd={cmd} stream={stream}: {frames.Count}f max={maxLen}B");
                            foreach (var f in frames)
                            {
                                var fields = M2Message.ParseAllFields(f);
                                Console.WriteLine($"      frame {f.Length}B: " + string.Join(" ", fields.Select(kv => $"0x{kv.Key:X}:{kv.Value.Item1}")));
                                if (Contains(f, needle))
                                    foreach (var kv in fields)
                                        Console.WriteLine($"        key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
                            }
                        }
                    }
                }
                Console.WriteLine("ip scan done");
            }
        }

        private static readonly int[] IFACE0 = { 20, 0 };
    }
}
