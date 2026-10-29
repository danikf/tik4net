#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Winbox;
using tik4net.WinboxCli;
using tik4net.unittests.Winbox;
using static tik4net.unittests.Golden.GoldenRender;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// T2 of the WinBox + MAC layer review, the request side: every request the WinBox transports build — the native
    /// operations, Safe Mode, the RoMON relay, the mepty terminal, the catalog's mproxy transfer and the legacy MD5
    /// login — captured from the real code over a recording channel with a fixed request id, plus what each operation
    /// makes of a reply.
    /// </summary>
    /// <remarks>
    /// C12 writes the request header once and C13 shares the status rule and the round-trip shape; both claim
    /// byte-identical requests, which <c>Golden/winbox-requests.txt</c> checks. The terminal and mproxy builders are
    /// private, so they are reached by reflection on their current names — a rename there updates this file, not the
    /// golden one.
    /// </remarks>
    [TestClass]
    [DoNotParallelize]
    public class WinboxRequestGoldenTests
    {
        private const byte FixedRequestId = 0x2A;
        private static readonly int[] Handler = { 24, 1 };

        /// <summary>
        /// Records every message sent and answers each with what <see cref="Answer"/> makes of it — by default a reply
        /// that echoes the request id and carries nothing else.
        /// </summary>
        private sealed class RecordingChannel : IWinboxM2Channel
        {
            private readonly Queue<byte[]> _inbound = new Queue<byte[]>();
            internal readonly List<byte[]> Sent = new List<byte[]>();
            internal Func<byte[], byte[]?> Answer = req => ReplyTo(req);

            public bool IsEncrypted => true;
            public bool DataAvailable => _inbound.Count > 0;
            public bool SupportsStaleDrain => false;
            public bool SendAbandoned => false;
            public bool SendStalled => false;
            public long BytesReceived => 0;
            public bool SupportsReaderLoop => false;
            public void Open(string host, int port, string user, string password, int connectTimeoutMs, int ioTimeoutMs, int sendTimeoutMs = 0) { }
            public byte[] NextReqIdField() => M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, FixedRequestId);
            public void Send(byte[] m2)
            {
                Sent.Add(m2);
                byte[]? reply = Answer(m2);
                if (reply != null) _inbound.Enqueue(reply);
            }
            public byte[] Receive(int timeoutMs) => _inbound.Count > 0 ? _inbound.Dequeue() : null!;
            public byte[] SendReceive(byte[] m2, int timeoutMs) { Send(m2); return Receive(timeoutMs); }
            public byte[] ReceiveNextFrame() => throw new NotSupportedException();
            public void StartIdleServicing() { }
            public void Dispose() { }

            internal string Take()
            {
                string s = string.Join(" | ", Sent.Select(Bytes));
                Sent.Clear();
                return s;
            }
        }

        private static byte[] ReplyTo(byte[] request, params byte[][] fields)
        {
            var all = new List<byte[]> { M2Message.SysToArr(), M2Message.SysFrom(24) };
            int? id = M2Message.ParseSysReqId(request);
            if (id.HasValue) all.Add(M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)id.Value));
            all.AddRange(fields);
            return M2Message.BuildM2(all.ToArray());
        }

        private static byte[] RecordsField(params byte[][][] records)
            => M2Message.MessageArraySys(WinboxM2Protocol.RecordKey.Records, records);

        private static readonly byte[][] TwoFields = { M2Message.StringUser(0x10, "t4n"), M2Message.U32Sys(WinboxM2Protocol.RecordKey.Disabled, 1) };

        [TestMethod]
        public void NativeOperations_MatchTheGoldenFile()
        {
            var lines = new List<string> { "# T2 - WinboxNativeM2Operations requests (fixed request id 0x2A) and their interpretation" };
            var ch = new RecordingChannel();
            var ops = new WinboxNativeM2Operations(ch, 5000);
            void Op(string name, Func<string> run)
            {
                string result = Try(run);
                lines.Add(name + " sent " + ch.Take());
                lines.Add(name + " result " + result);
            }

            ch.Answer = req => ReplyTo(req, RecordsField(new[] { M2Message.SessionIdField(1), M2Message.StringUser(0x10, "a") },
                new[] { M2Message.SessionIdField(2) }));
            Op("GetAll()", () => Records(ops.GetAll(Handler)));
            Op("GetAll(flags 0x12, maxObjs 1000)", () => Records(ops.GetAll(Handler, 0x12, 1000)));
            int page = 0;
            ch.Answer = req => page++ == 0
                ? ReplyTo(req, RecordsField(new[] { M2Message.SessionIdField(1) }), M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 77),
                    M2Message.RawSys(WinboxM2Protocol.RecordKey.ContinuationRaw, new byte[] { 1, 2, 3 }))
                : ReplyTo(req, RecordsField(new[] { M2Message.SessionIdField(2) }), M2Message.U32Sys(WinboxM2Protocol.SysKey.ErrorCode, WinboxM2Protocol.Error.ObjectNonexistent));
            Op("GetAll(two pages)", () => Records(ops.GetAll(Handler)));
            ch.Answer = req => ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.SysKey.ErrorCode, 0xFE0002), M2Message.StringSys(WinboxM2Protocol.SysKey.ErrorString, "not implemented"));
            Op("GetAll(error)", () => Records(ops.GetAll(Handler)));
            Op("GetSingleton(error)", () => Fields(ops.GetSingleton(Handler)));
            Op("Set(error)", () => { ops.Set(Handler, 5, TwoFields); return "ok"; });
            Op("Add(error)", () => Value(ops.Add(Handler, TwoFields)));
            Op("Remove(error)", () => { ops.Remove(Handler, 5); return "ok"; });
            Op("Move(error)", () => { ops.Move(Handler, 5, 7); return "ok"; });
            Op("InvokeAction(error)", () => Fields(ops.InvokeAction(Handler, 1, 5)));
            Op("SafeModeTake(error)", () => ops.SafeModeTake().ToString());
            Op("StartMonitor(error)", () => Value(ops.StartMonitor(Handler, 0x50001, TwoFields)));
            Op("PollMonitorRound(error)", () => Round(ops.PollMonitorRound(Handler, 0xFE0004, 3u, true, null)));
            Op("CancelMonitor(error swallowed)", () => { ops.CancelMonitor(Handler, 0xFE0006, 3u); return "ok"; });

            ch.Answer = req => ReplyTo(req, RecordsField(new[] { M2Message.SessionIdField(9), M2Message.StringUser(0x10, "one") }));
            Op("GetOne(9)", () => Fields(ops.GetOne(Handler, 9)));
            Op("GetOne(0x80000019)", () => Fields(ops.GetOne(Handler, unchecked((int)0x80000019))));
            ch.Answer = req => ReplyTo(req, M2Message.StringUser(0x16, "7.25"));
            Op("GetSingleton()", () => Fields(ops.GetSingleton(Handler)));
            Op("GetSingleton(flags 0, command 0x50003)", () => Fields(ops.GetSingleton(Handler, 0, 0x50003)));
            Op("GetRouterVersion()", () => Value(ops.GetRouterVersion()));
            lines.Add("BuildGetSingleton " + Bytes(ops.BuildGetSingleton(Handler, 5, null)));
            lines.Add("BuildSetSingleton(id -1) " + Bytes(ops.BuildSetSingleton(Handler, TwoFields, -1)));
            lines.Add("BuildSetSingleton(id 300, command 0x50004) " + Bytes(ops.BuildSetSingleton(Handler, TwoFields, 300, 0x50004)));

            ch.Answer = req => ReplyTo(req);
            Op("Set(5)", () => { ops.Set(Handler, 5, TwoFields); return "ok"; });
            Op("Set(5, no fields)", () => { ops.Set(Handler, 5, null!); return "ok"; });
            Op("SetSingleton()", () => { ops.SetSingleton(Handler, TwoFields); return "ok"; });
            Op("SetSingleton(id 2, command 0x50004)", () => { ops.SetSingleton(Handler, TwoFields, 2, 0x50004); return "ok"; });
            Op("Remove(5)", () => { ops.Remove(Handler, 5); return "ok"; });
            Op("Move(5, end)", () => { ops.Move(Handler, 5, -1); return "ok"; });
            Op("Move(5, before 7)", () => { ops.Move(Handler, 5, 7); return "ok"; });
            Op("Move(5, before 0xFFF0)", () => { ops.Move(Handler, 5, 0xFFF0); return "ok"; });
            Op("CancelMonitor(no id)", () => { ops.CancelMonitor(Handler, 0xFE0006, null); return "ok"; });
            Op("CancelMonitor(0xFFFFFFFD)", () => { ops.CancelMonitor(Handler, 0xFE0006, 0xFFFFFFFDu); return "ok"; });
            Op("SafeModeRelease(0x1234)", () => { ops.SafeModeRelease(0x1234); return "ok"; });

            ch.Answer = req => ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.RecordKey.Id, unchecked((int)0x80000019)));
            Op("Add()", () => Value(ops.Add(Handler, TwoFields)));
            Op("SafeModeTake()", () => ops.SafeModeTake().ToString());
            Op("StartMonitor()", () => Value(ops.StartMonitor(Handler, 0x50001, TwoFields)));
            Op("InvokeAction(cmd 1, id 5)", () => Fields(ops.InvokeAction(Handler, 1, 5)));
            Op("InvokeAction(cmd 0x50002, no id, fields)", () => Fields(ops.InvokeAction(Handler, 0x50002, -1, TwoFields)));
            ch.Answer = req => ReplyTo(req);
            Op("Add(no id in reply)", () => Value(ops.Add(Handler, null!)));
            Op("SafeModeTake(no id in reply)", () => ops.SafeModeTake().ToString());
            Op("StartMonitor(no id in reply)", () => Value(ops.StartMonitor(Handler, 0x50001, null!)));

            ch.Answer = req => ReplyTo(req, RecordsField(new[] { M2Message.U32Sys(0x10002, 64) }),
                M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 3));
            Op("PollMonitorRound(query, id 3)", () => Round(ops.PollMonitorRound(Handler, 0xFE0004, 3u, true, null)));
            var cont = WinboxM2Continuation.From(ReplyTo(M2Message.BuildM2(), M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 3)));
            Op("PollMonitorRound(query, no id, continuation, flags 0)", () => Round(ops.PollMonitorRound(Handler, 0xFE0004, null, true, cont, 0)));
            ch.Answer = req => ReplyTo(req, M2Message.U32Sys(0x10002, 64), M2Message.BoolSys(WinboxM2Protocol.RecordKey.Finished, true),
                M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 3));
            Op("PollMonitorRound(action, id 0xFFFFFFFD)", () => Round(ops.PollMonitorRound(Handler, 0x50002, 0xFFFFFFFDu, false, null)));
            ch.Answer = req => ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.SysKey.ErrorCode, WinboxM2Protocol.Error.ObjectNonexistent),
                M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 3));
            Op("PollMonitorRound(query, ended)", () => Round(ops.PollMonitorRound(Handler, 0xFE0004, 3u, true, null)));

            // The lockstep path reads past a reply to another request id (up to 16), then takes the one that matches.
            int answered = 0;
            ch.Answer = req => answered++ == 0
                ? M2Message.BuildM2(M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 0x11), M2Message.StringUser(0x16, "stale"))
                : null;
            Op("GetSingleton(stale reply first, no match)", () => Fields(ops.GetSingleton(Handler)));

            GoldenFile.AssertMatches("winbox-requests.txt", lines);
        }

        private static string Round((List<M2Record> records, bool done, WinboxM2Continuation? continuation) r)
        {
            var head = new List<byte[]>();
            r.continuation?.AppendTo(head);
            return "records=" + Records(r.records) + " done=" + r.done + " continuation=" + (r.continuation == null ? "null" : string.Join("+", head.Select(Bytes)));
        }

        [TestMethod]
        public void RomonMeptyMproxyAndLegacyRequests_MatchTheGoldenFile()
        {
            var lines = new List<string> { "# T2 - RoMON relay, mepty terminal, catalog mproxy and legacy MD5 requests (fixed request id 0x2A)" };

            // ── RoMON: settings read, link connect, link check, then a routed request and an unrouted reply ──
            var agent = new RecordingChannel();
            agent.Answer = req =>
            {
                int[] to = M2Message.ParseU32ArrayField(req, WinboxM2Protocol.SysKey.To)!;
                if (to[0] == 127) return ReplyTo(req, M2Message.BoolSys(0x1, true), M2Message.RawUser(0x65, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }));
                if (to.Length == 1) return ReplyTo(req, M2Message.SessionIdField(300));
                return ReplyTo(req, M2Message.StringUser(0x16, "3.42"));
            };
            var romon = new WinboxRomonChannel(agent, new RomonRelayTarget("AA:BB:CC:DD:EE:01", "admin", "pw"));
            romon.Open("192.168.4.1", 8291, "admin", "", 5000, 5000);
            foreach (var m in agent.Sent) lines.Add("romon open " + Bytes(m));
            agent.Sent.Clear();
            lines.Add("romon AgentRomonId " + romon.AgentRomonId + " Link " + romon.Link);
            agent.Answer = req => M2Message.BuildM2(M2Message.SysToArr(), M2Message.U32ArraySys(WinboxM2Protocol.SysKey.From, 2, 300, 24, 1),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, FixedRequestId));
            byte[] routed = M2Message.BuildM2(M2Message.SysToArr(24, 1), M2Message.SysFrom(), M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                romon.NextReqIdField(), M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Command.GetAll));
            lines.Add("romon SendReceive reply " + Bytes(romon.SendReceive(routed, 1000)));
            lines.Add("romon SendReceive sent " + agent.Take());

            // ── mepty: login, terminal ready, pull, input ──
            var term = new RecordingChannel();
            term.Answer = req => ReplyTo(req, M2Message.SessionIdField(265));
            using (var client = new WinboxCliClient(term, Encoding.UTF8, CliClientTimeouts.Fixed(1000, 1000)))
            {
                lines.Add("mepty OpenTerminalSession returns " + client.OpenTerminalSession("pw", "vt102", 80, 25));
                lines.Add("mepty login sent " + term.Take());
                term.Answer = _ => null;
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(WinboxCliClient).GetField("_sessionId", flags)!.SetValue(client, 265);
                typeof(WinboxCliClient).GetField("_ackBytes", flags)!.SetValue(client, 1234);
                typeof(WinboxCliClient).GetMethod("SendTerminalReady", flags)!.Invoke(client, new object[] { 265 });
                lines.Add("mepty ready sent " + term.Take());
                typeof(WinboxCliClient).GetMethod("SendPull", flags)!.Invoke(client, null);
                lines.Add("mepty pull sent " + term.Take());
                typeof(WinboxCliClient).GetMethod("SendInput", flags)!.Invoke(client, new object[] { Encoding.ASCII.GetBytes("/system/identity/print\r") });
                lines.Add("mepty input sent " + term.Take());
                typeof(WinboxCliClient).GetMethod("SendInput", flags)!.Invoke(client, new object[] { Enumerable.Repeat((byte)'x', 300).ToArray() });
                lines.Add("mepty input(300) sent " + term.Take());
            }

            // ── catalog: mproxy open (both commands) and a two-chunk read ──
            var cat = new RecordingChannel();
            var ops = new WinboxNativeM2Operations(cat, 5000);
            cat.Answer = req => ReplyTo(req, M2Message.SessionIdField(19));
            lines.Add("mproxy open(OpenStatic) returns " + WinboxMproxyClient.Open(ops, "roteros.jg.gz", WinboxM2Protocol.Mproxy.OpenStatic));
            lines.Add("mproxy open(OpenVarPkg) returns " + WinboxMproxyClient.Open(ops, "wlan6.jg", WinboxM2Protocol.Mproxy.OpenVarPkg));
            lines.Add("mproxy open sent " + cat.Take());
            cat.Answer = req => ReplyTo(req, M2Message.U32Sys(WinboxM2Protocol.SysKey.ErrorCode, 0xFE0006));
            lines.Add("mproxy open(refused) returns " + WinboxMproxyClient.Open(ops, "missing.jg", WinboxM2Protocol.Mproxy.OpenStatic));
            cat.Take();
            int chunk = 0;
            cat.Answer = req => ReplyTo(req, M2Message.RawUser(3, chunk++ == 0 ? Enumerable.Repeat((byte)0x41, WinboxM2Protocol.Mproxy.ChunkSize).ToArray() : new byte[] { 0x42, 0x43 }));
            lines.Add("mproxy read returns " + Bytes(WinboxMproxyClient.Read(ops, 19)!));
            lines.Add("mproxy read sent " + cat.Take());

            // ── legacy MD5 (pre-6.43): the four requests of the login, as FakeWinboxServer receives them ──
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() => server.RunFullLoginSequence(265));
                session.Open("127.0.0.1", server.Port, "admin+t", "pw", 5000, 5000);
                serverTask.GetAwaiter().GetResult();
                Assert.IsFalse(session.IsEncrypted);
                foreach (var m in server.ReceivedMessages) lines.Add("legacy-md5 " + Bytes(m));
            }

            GoldenFile.AssertMatches("winbox-requests-other.txt", lines);
        }
    }
}
