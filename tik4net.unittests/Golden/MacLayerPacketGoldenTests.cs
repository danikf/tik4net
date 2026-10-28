#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.MacTelnet;
using tik4net.Winbox;
using static tik4net.unittests.Golden.GoldenRender;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// T5 of the WinBox + MAC layer review: the MAC-layer packet in golden bytes — every packet type the client builds
    /// and how each of the three parsers reads every type — and the triage each receive loop applies to each type.
    /// </summary>
    /// <remarks>
    /// C5 builds the 22-byte header once and parses it once, C6 writes the "ACK / PING / duplicate / control" triage
    /// once, C7 moves MAC-Telnet's receiving onto the base. Each claims the wire does not change; <c>Golden/mac-packets.txt</c>
    /// and <c>Golden/mac-triage.txt</c> are what check it. The triage is driven over loopback UDP through the real loops
    /// — WinBox-MAC's poll (<c>DataAvailable</c>) and read (<c>Receive</c>), MAC-Telnet's login drain (<c>DrainSync</c>)
    /// and its pump — and pinned as what the client sends back plus the state it is left in.
    /// <para><b>X7.</b> The <c>DrainSync</c> rows for ACK and END show today's behaviour: the login drain notes no ACK
    /// and does not see END, unlike the pump. That is review item X7's defect, fixed in Phase 2 with C6/C7 — those rows
    /// change then, on purpose.</para>
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class MacLayerPacketGoldenTests
    {
        private static readonly byte[] ClientMac = { 0x02, 0, 0, 0, 0, 0x01 };
        private static readonly byte[] RouterMac = { 0x02, 0, 0, 0, 0, 0x02 };
        private const ushort SessionKey = 0x1234;
        private const ushort MacTelnetClientType = 0x0015;

        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>Exposes the protected send and control-packet surface of the base.</summary>
        private sealed class Probe : MacLayerTransport
        {
            internal void SendRaw(byte type, byte[]? payload) => Send(type, payload);
            internal void Ack(uint counter) => SendAck(counter);
            internal void Pong(uint counter) => SendPong(counter);
            internal static byte[] Ctrl(byte type, byte[] data) => BuildCtrl(type, data);
            internal static (byte ctrlType, byte[] data)[] Parse(byte[] payload) => ParseCtrl(payload);
            internal static bool IsCtrl(byte[] payload) => IsControlPacket(payload);
        }

        /// <summary>Gives <paramref name="t"/> a loopback socket aimed at <paramref name="router"/>.</summary>
        private static void Attach(MacLayerTransport t, UdpClient router, ushort clientType)
        {
            var self = (IPEndPoint)router.Client.LocalEndPoint!;
            Set(t, "_udp", new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));
            Set(t, "_routerEp", self);
            Set(t, "_routerUnicastEp", self);
            Set(t, "_localMac", ClientMac);
            Set(t, "_routerMac", RouterMac);
            Set(t, "_clientType", clientType);
            Set(t, "_sessionKey", SessionKey);
        }

        private static void Set(MacLayerTransport t, string field, object value)
            => typeof(MacLayerTransport).GetField(field, Inst)!.SetValue(t, value);

        private static object? Get(object t, Type declaring, string field) => declaring.GetField(field, Inst)!.GetValue(t);

        private static IPEndPoint ClientEndPoint(MacLayerTransport t)
            => (IPEndPoint)((UdpClient)Get(t, typeof(MacLayerTransport), "_udp")!).Client.LocalEndPoint!;

        private static UdpClient Router()
        {
            var r = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            r.Client.ReceiveTimeout = 150;   // everything a case sends back is sent before the drain starts
            return r;
        }

        /// <summary>Everything the client sent the router within the receive timeout, one hex datagram each.</summary>
        private static string Drain(UdpClient router)
        {
            var got = new List<string>();
            while (true)
            {
                try { var ep = new IPEndPoint(IPAddress.Any, 0); got.Add(GoldenFile.Hex(router.Receive(ref ep))); }
                catch (SocketException) { break; }
            }
            return got.Count == 0 ? "nothing" : string.Join(" ", got);
        }

        /// <summary>A packet as the router sends it: its MAC as source, ours as destination, the client type at [14] and
        /// the session key at [16] — the reverse of a request.</summary>
        private static byte[] FromRouter(byte type, uint counter, byte[]? payload = null, byte[]? srcMac = null, bool keyAt14 = false)
        {
            byte[] pkt = new byte[22 + (payload?.Length ?? 0)];
            pkt[0] = 1; pkt[1] = type;
            Buffer.BlockCopy(srcMac ?? RouterMac, 0, pkt, 2, 6);
            Buffer.BlockCopy(ClientMac, 0, pkt, 8, 6);
            ushort at14 = keyAt14 ? SessionKey : MacTelnetClientType, at16 = keyAt14 ? MacTelnetClientType : SessionKey;
            pkt[14] = (byte)(at14 >> 8); pkt[15] = (byte)at14;
            pkt[16] = (byte)(at16 >> 8); pkt[17] = (byte)at16;
            pkt[18] = (byte)(counter >> 24); pkt[19] = (byte)(counter >> 16); pkt[20] = (byte)(counter >> 8); pkt[21] = (byte)counter;
            if (payload != null) Buffer.BlockCopy(payload, 0, pkt, 22, payload.Length);
            return pkt;
        }

        private static readonly byte[] Hello = Encoding.ASCII.GetBytes("hello");
        private static readonly byte[] CtrlPayload = Probe.Ctrl(9, new byte[0]);

        /// <summary>The inbound cases: (name, datagrams the router sends — the last one is the case under test).</summary>
        private static IEnumerable<(string name, byte[][] packets)> Inbound()
        {
            yield return ("SESSIONSTART", new[] { FromRouter(0, 0) });
            yield return ("DATA", new[] { FromRouter(1, 0, Hello) });
            yield return ("DATA control", new[] { FromRouter(1, 0, CtrlPayload) });
            yield return ("DATA duplicate", new[] { FromRouter(1, 0, Hello), FromRouter(1, 0, Hello) });
            yield return ("DATA past a hole", new[] { FromRouter(1, 0, Hello), FromRouter(1, 4000, Hello) });
            yield return ("ACK", new[] { FromRouter(2, 7) });
            yield return ("PING", new[] { FromRouter(4, 9) });
            yield return ("PONG", new[] { FromRouter(5, 9) });
            yield return ("END", new[] { FromRouter(255, 0) });
            yield return ("own echo DATA", new[] { FromRouter(1, 0, Hello, srcMac: ClientMac) });
            yield return ("short datagram", new[] { new byte[] { 1, 1, 2, 3 } });
        }

        // ── the packets ───────────────────────────────────────────────────────

        [TestMethod]
        public void PacketsAndParsers_MatchTheGoldenFile()
        {
            var lines = new List<string> { "# T5 - MAC-layer packets: what the client builds, and how each parser reads each type",
                "# client " + GoldenFile.Hex(ClientMac) + " router " + GoldenFile.Hex(RouterMac) + " key 0x1234 client type 0x0015" };

            using (var router = Router())
            using (var probe = new Probe())
            {
                Attach(probe, router, MacTelnetClientType);
                probe.SendRaw(0, null); lines.Add("send SESSIONSTART " + Drain(router));
                probe.SendRaw(1, Encoding.ASCII.GetBytes("abc")); lines.Add("send DATA 'abc' " + Drain(router));
                probe.SendRaw(1, Encoding.ASCII.GetBytes("defg")); lines.Add("send DATA 'defg' " + Drain(router));
                probe.SendRaw(1, new byte[0]); lines.Add("send DATA empty " + Drain(router));
                probe.Ack(77); lines.Add("send ACK 77 " + Drain(router));
                probe.Pong(0x01020304); lines.Add("send PONG 0x01020304 " + Drain(router));
                probe.SendRaw(255, null); lines.Add("send END " + Drain(router));
            }

            // The NIC probe's own SESSIONSTART and its ACK check (the key is at [16] in a reply).
            var probeType = typeof(MacLayerTransport).GetNestedType("CandidateProbe", BindingFlags.NonPublic)!;
            using (var router = Router())
            {
                var candidate = (IDisposable)Activator.CreateInstance(probeType, nonPublic: true)!;
                using (candidate)
                {
                    var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    probeType.GetField("Udp", Inst)!.SetValue(candidate, udp);
                    probeType.GetField("LocalMac", Inst)!.SetValue(candidate, ClientMac);
                    probeType.GetField("Broadcast", Inst)!.SetValue(candidate, (IPEndPoint)router.Client.LocalEndPoint!);
                    probeType.GetField("SessionKey", Inst)!.SetValue(candidate, SessionKey);
                    probeType.GetMethod("SendSessionStart", Inst)!.Invoke(candidate, new object[] { RouterMac, MacTelnetClientType });
                    lines.Add("probe SESSIONSTART " + Drain(router));

                    var tryReadAck = probeType.GetMethod("TryReadAck", Inst)!;
                    var to = (IPEndPoint)udp.Client.LocalEndPoint!;
                    foreach (var (name, pkt) in new[]
                    {
                        ("ACK", FromRouter(2, 5)),
                        ("ACK key at [14]", FromRouter(2, 5, keyAt14: true)),
                        ("ACK from another MAC", FromRouter(2, 5, srcMac: new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF })),
                        ("DATA", FromRouter(1, 0, Hello)),
                        ("short", new byte[] { 1, 2 }),
                    })
                    {
                        router.Send(pkt, pkt.Length, to);
                        WaitFor(() => udp.Available > 0);
                        var args = new object?[] { RouterMac, 0u };
                        bool ok = (bool)tryReadAck.Invoke(candidate, args)!;
                        lines.Add("probe TryReadAck " + name + " " + ok + " counter=" + args[1]);
                    }
                }
            }

            // The two parsers of a received datagram.
            var parsePacket = typeof(MacLayerTransport).GetMethod("ParsePacket", Stat)!;
            using (var client = new MacTelnetUdpClient(Encoding.UTF8, CliClientTimeouts.Fixed(1000, 1000), null))
            {
                Set(client, "_localMac", ClientMac);
                var tryParse = typeof(MacTelnetUdpClient).GetMethod("TryParsePacket", Inst)!;
                foreach (var (name, packets) in Inbound())
                {
                    byte[] pkt = packets.Last();
                    lines.Add("parse " + name + " " + Bytes(pkt));
                    lines.Add("  ParsePacket " + Try(() => RenderTuple(parsePacket.Invoke(null, new object[] { pkt }))));
                    var args = new object?[] { pkt, null, null, null };
                    lines.Add("  MacTelnetUdpClient.TryParsePacket " + Try(() =>
                        tryParse.Invoke(client, args) + " type=" + args[1] + " counter=" + args[2] + " payload=" + Bytes((byte[]?)args[3])));
                }
            }

            // Control packets: framing, walking and recognition.
            foreach (byte ct in new byte[] { 0, 1, 2, 3, 4, 5, 6, 9 })
                lines.Add("ctrl BuildCtrl(" + ct + ", 'ab') " + GoldenFile.Hex(Probe.Ctrl(ct, Encoding.ASCII.GetBytes("ab"))));
            lines.Add("ctrl BuildCtrl(1, empty) " + GoldenFile.Hex(Probe.Ctrl(1, new byte[0])));
            byte[] two = Probe.Ctrl(0, new byte[0]).Concat(Probe.Ctrl(1, new byte[] { 7, 8, 9 })).ToArray();
            lines.Add("ctrl ParseCtrl(two) " + RenderCtrl(Probe.Parse(two)));
            lines.Add("ctrl ParseCtrl(truncated) " + RenderCtrl(Probe.Parse(two.Take(two.Length - 1).ToArray())));
            lines.Add("ctrl ParseCtrl(not ctrl) " + RenderCtrl(Probe.Parse(Hello)));
            lines.Add("ctrl IsControlPacket " + Probe.IsCtrl(two) + " " + Probe.IsCtrl(Hello) + " " + Probe.IsCtrl(new byte[] { 0x56, 0x34 }));

            GoldenFile.AssertMatches("mac-packets.txt", lines);
        }

        private static string RenderTuple(object? parsed)
        {
            if (parsed == null) return "null";
            var t = parsed.GetType();
            object?[] items = t.GetFields().OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => f.GetValue(parsed)).ToArray();
            return "(" + string.Join(", ", items.Select(i => i is byte[] b ? Bytes(b) : Value(i))) + ")";
        }

        private static string RenderCtrl((byte ctrlType, byte[] data)[] ctrl)
            => "[" + string.Join(", ", ctrl.Select(c => c.ctrlType + ":" + GoldenFile.Hex(c.data))) + "]";

        private static void WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(5);
        }

        // ── the triage ────────────────────────────────────────────────────────

        [TestMethod]
        public void TriageOfEachPacketType_MatchesTheGoldenFile()
        {
            var lines = new List<string> { "# T5 - what each receive loop does with each packet type: what it sends back, and the state left",
                "# X7: the DrainSync rows for ACK and END are TODAY's behaviour (review item X7, fixed in Phase 2)" };

            foreach (var (name, packets) in Inbound())
            {
                lines.Add("== " + name);
                lines.Add("  WinboxMac.DataAvailable " + RunWinboxMac(packets, s => "available=" + s.DataAvailable));
                lines.Add("  WinboxMac.Receive " + RunWinboxMac(packets, s => "frame=" + Bytes(s.Receive(300))));
                lines.Add("  MacTelnet.DrainSync " + RunMacTelnet(packets, c =>
                {
                    typeof(MacTelnetUdpClient).GetMethod("DrainSync", Inst)!.Invoke(c, new object[] { 150 });
                    return "";
                }));
                lines.Add("  MacTelnet.pump " + RunMacTelnet(packets, c =>
                {
                    typeof(MacTelnetUdpClient).GetMethod("StartPump", Inst)!.Invoke(c, null);
                    Thread.Sleep(400);
                    typeof(MacTelnetUdpClient).GetMethod("StopPump", Inst)!.Invoke(c, null);
                    return "";
                }));
            }

            GoldenFile.AssertMatches("mac-triage.txt", lines);
        }

        private static string RunWinboxMac(byte[][] packets, Func<WinboxMacM2Session, string> act)
        {
            using (var router = Router())
            {
                var session = new WinboxMacM2Session(null!);
                Attach(session, router, 0x0f90);
                string observed = Deliver(router, session, packets, () => act(session));
                string state = State(session) + " rxBuf=" + ((List<byte>)Get(session, typeof(WinboxMacM2Session), "_rxBuf")!).Count
                    + " bytesReceived=" + session.BytesReceived;
                Set(session, "_udp", new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));   // so Dispose's PKT_END goes nowhere
                session.Dispose();
                return observed + " | " + state;
            }
        }

        private static string RunMacTelnet(byte[][] packets, Func<MacTelnetUdpClient, string> act)
        {
            using (var router = Router())
            {
                var client = new MacTelnetUdpClient(Encoding.UTF8, CliClientTimeouts.Fixed(1000, 1000), null);
                Attach(client, router, MacTelnetClientType);
                string observed = Deliver(router, client, packets, () => act(client));
                string rx = Get(client, typeof(MacTelnetUdpClient), "_rx")!.ToString()!;
                string state = State(client) + " rx='" + rx + "' routerEnded=" + Get(client, typeof(MacTelnetUdpClient), "_routerEnded");
                Set(client, "_udp", new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));
                client.Dispose();
                return observed + " | " + state;
            }
        }

        /// <summary>Sends the packets, lets them land, runs the loop, and reports what it sent back.</summary>
        private static string Deliver(UdpClient router, MacLayerTransport t, byte[][] packets, Func<string> act)
        {
            var to = ClientEndPoint(t);
            var udp = (UdpClient)Get(t, typeof(MacLayerTransport), "_udp")!;
            foreach (var p in packets) router.Send(p, p.Length, to);
            WaitFor(() => udp.Available > 0);
            Thread.Sleep(50);   // loopback delivery is prompt but not synchronous; let every datagram land
            string result = act();
            return (result.Length > 0 ? result + " " : "") + "sent-back " + Drain(router);
        }

        private static string State(MacLayerTransport t)
            => "haveAck=" + Get(t, typeof(MacLayerTransport), "_haveAck") + " highestAck=" + Get(t, typeof(MacLayerTransport), "_highestAck")
               + " inCounter=" + Get(t, typeof(MacLayerTransport), "_inCounter");
    }
}
