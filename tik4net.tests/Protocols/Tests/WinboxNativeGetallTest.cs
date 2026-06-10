// WinboxNativeGetallTest.cs — Phase 3 BREAKTHROUGH: native M2 CRUD via the
// webfig (/jsproxy) protocol, reverse-engineered from master-d53cd8ec58cb.js.
//
// Earlier RE failed for three reasons (all fixed here):
//   1. Wrong command. getall is 0xfe0004 (webfig default getallcmd), NOT a small
//      number. cmd=3 returns the interface TYPE registry, not instances.
//   2. Missing flag field. getall requires ufe000c (0xFE000C) = 0x10000005.
//   3. Records are a MESSAGE-ARRAY under key 0xFE0002 (webfig 'Mfe0002'); the old
//      parser had no case for type 0xA8 and stopped at it → records never seen.
//
// Field keys (from webfig): .id=ufe0001 (0xFE0001), Name=s10006 (0x10006),
//   comment=sfe0009 (0xFE0009, types.comment.get/put). set = cmd 0xfe0003.
//
// All tests hit a live router and the comment test writes config (restored after).
// [Ignore] keeps them out of the normal suite — run individually via --filter.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.tests
{
    [Ignore("Native-M2 CRUD (webfig protocol) — manual only; hits a live router, comment test writes config. Run via --filter.")]
    [TestClass]
    public class WinboxNativeGetallTest
    {
        private const int WINBOX_PORT = 8291;
        private static readonly int[] IFACE   = { 20, 0 };   // /interface
        private static readonly int[] IPADDR  = { 20, 1 };   // /ip/address
        private const int KEY_NAME    = 0x10006;   // s10006  — Name
        private const int KEY_COMMENT = 0xFE0009;  // sfe0009 — comment (well-known)
        private const int KEY_ID      = 0xFE0001;  // ufe0001 — record .id

        private static (string host, string user, string pass) Cfg() => (
            ConfigurationManager.AppSettings["host"],
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["pass"] ?? "");

        private static string Str(Dictionary<int, Tuple<string, object>> rec, int key)
            => rec.TryGetValue(key, out var t) ? t.Item2?.ToString() ?? "" : null;

        private static int IdOf(Dictionary<int, Tuple<string, object>> rec)
            => rec.TryGetValue(KEY_ID, out var t) ? Convert.ToInt32(t.Item2) : -1;

        private static void DumpRecord(Dictionary<int, Tuple<string, object>> rec, string label)
        {
            Console.WriteLine($"  [{label}] {rec.Count} fields:");
            foreach (var kv in rec.OrderBy(k => k.Key))
                Console.WriteLine($"    key=0x{kv.Key:X6} {kv.Value.Item1,-6} = {kv.Value.Item2}");
        }

        // ── 1. getall interfaces natively, validate against the API ───────────
        [TestMethod]
        public void Native_GetAllInterfaces()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                var records = client.NativeGetAll(IFACE);
                Console.WriteLine($"=== [20,0] getall (cmd=0xfe0004): {records.Count} records ===");
                foreach (var rec in records)
                    DumpRecord(rec, $"id={IdOf(rec)} name={Str(rec, KEY_NAME)}");

                var m2Names = records.Select(r => Str(r, KEY_NAME))
                                     .Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => n).ToList();

                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    var apiNames = api.LoadAll<Interface>().Select(i => i.Name).OrderBy(n => n).ToList();
                    Console.WriteLine($"M2 names : {string.Join(", ", m2Names)}");
                    Console.WriteLine($"API names: {string.Join(", ", apiNames)}");
                    CollectionAssert.AreEqual(apiNames, m2Names,
                        "Native getall interface names must match the API.");
                }
            }
        }

        // ── 2. getall IP addresses natively (proves [20,1] table works too) ───
        [TestMethod]
        public void Native_GetAllIpAddresses()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                var records = client.NativeGetAll(IPADDR);
                Console.WriteLine($"=== [20,1] getall: {records.Count} records ===");
                foreach (var rec in records)
                    DumpRecord(rec, $"id={IdOf(rec)}");
            }
        }

        // ── 2b. Field-name introspection probe ────────────────────────────────
        // Hypothesis (user): the router exposes field NAMES like it exposes type
        // names (cmd=3 → key 0x2 str[] = ether/vlan/...). Earlier dumps ran on the
        // OLD parser that stopped at the first 0xA8/msg-array, so any name registry
        // was truncated and invisible. Re-probe with the fixed parser; dump EVERYTHING
        // (recursing into msg / msg[]) and flag any str[] that looks like field names.
        [TestMethod]
        public void Native_ProbeFieldNameRegistry()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // sweep small commands + a couple of flag/arg variants, dump deep
                var probes = new (string label, byte[][] extra, int cmd)[]
                {
                    ("[20,0] cmd=3 bare", new byte[0][], 3),
                    ("[20,0] cmd=3 +type=1(ether)", new[]{ M2Message.U32User(0x10001, 1) }, 3),
                    ("[20,0] cmd=3 +flags=0x10000005", new[]{ M2Message.U32Sys(0xFE000C, 0x10000005) }, 3),
                    ("[20,0] cmd=0xfe0004 +flags (getall, deep)", new[]{ M2Message.U32Sys(0xFE000C, 0x10000005) }, 0xFE0004),
                    ("[20,0] cmd=1", new byte[0][], 1),
                    ("[20,0] cmd=4", new byte[0][], 4),
                    ("[20,0] cmd=5", new byte[0][], 5),
                };

                foreach (var (label, extra, cmd) in probes)
                {
                    Console.WriteLine($"\n========== {label} ==========");
                    List<byte[]> frames;
                    try { frames = client.ProbeCommandRaw(IFACE, cmd, 2500, extra); }
                    catch (Exception ex) { Console.WriteLine($"  EXC {ex.Message}"); continue; }
                    foreach (var raw in frames)
                    {
                        var fields = M2Message.ParseAllFields(raw);
                        Console.WriteLine($"  --- frame {raw.Length}B, {fields.Count} top-level fields ---");
                        DumpDeep(fields, "    ");
                    }
                }
            }
        }

        // Recursive dump that descends into msg (Dictionary) and msg[] (List) and
        // highlights string arrays that resemble a name registry.
        private static void DumpDeep(Dictionary<int, Tuple<string, object>> fields, string indent)
        {
            foreach (var kv in fields.OrderBy(k => k.Key))
            {
                string t = kv.Value.Item1;
                object v = kv.Value.Item2;
                if (v is Dictionary<int, Tuple<string, object>> sub)
                {
                    Console.WriteLine($"{indent}key=0x{kv.Key:X6} msg:");
                    DumpDeep(sub, indent + "  ");
                }
                else if (v is List<Dictionary<int, Tuple<string, object>>> list)
                {
                    Console.WriteLine($"{indent}key=0x{kv.Key:X6} msg[] ({list.Count}):");
                    for (int i = 0; i < list.Count; i++)
                    {
                        Console.WriteLine($"{indent}  [{i}]:");
                        DumpDeep(list[i], indent + "    ");
                    }
                }
                else
                {
                    string flag = (t == "str[]") ? "  <-- STRING ARRAY (name registry?)" : "";
                    Console.WriteLine($"{indent}key=0x{kv.Key:X6} {t,-6} = {v}{flag}");
                }
            }
        }

        // ── 3. get-one + set/restore comment on ether1, verified via API ──────
        [TestMethod]
        public void Native_SetAndRestoreEther1Comment()
        {
            var (host, user, pass) = Cfg();
            using (var client = new WinboxM2Client())
            {
                client.Connect(host, WINBOX_PORT);
                client.Authenticate(host, WINBOX_PORT, user, pass);

                // find ether1's M2 .id via getall
                var records = client.NativeGetAll(IFACE);
                var ether1 = records.FirstOrDefault(r => Str(r, KEY_NAME) == "ether1");
                Assert.IsNotNull(ether1, "ether1 not found in native getall");
                int id = IdOf(ether1);
                Console.WriteLine($"ether1 M2 .id = {id}");

                // read the full record (get-one) and show the comment field
                var full = client.NativeGetOne(IFACE, id);
                DumpRecord(full, "ether1 get-one");
                string m2CommentBefore = Str(full, KEY_COMMENT) ?? "";
                Console.WriteLine($"comment (sfe0009) before = '{m2CommentBefore}'");

                string original;
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    original = api.LoadAll<Interface>().First(i => i.Name == "ether1").Comment ?? "";
                Console.WriteLine($"comment (API) before = '{original}'");

                const string test = "native-m2-ok";
                int st = client.NativeSetRecord(IFACE, id, M2Message.StringSys(KEY_COMMENT, test));
                Console.WriteLine($"set status = 0x{st:X}");
                Assert.AreEqual(0, st, "set should return status 0");

                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    string after = api.LoadAll<Interface>().First(i => i.Name == "ether1").Comment ?? "";
                    Console.WriteLine($"comment (API) after set = '{after}'");
                    // restore before asserting so the router is left clean either way
                    client.NativeSetRecord(IFACE, id, M2Message.StringSys(KEY_COMMENT, original));
                    Assert.AreEqual(test, after, "Native set must change the comment seen by the API.");
                }

                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                {
                    string restored = api.LoadAll<Interface>().First(i => i.Name == "ether1").Comment ?? "";
                    Console.WriteLine($"comment (API) restored = '{restored}'");
                    Assert.AreEqual(original, restored, "comment must be restored");
                }
            }
        }
    }
}
