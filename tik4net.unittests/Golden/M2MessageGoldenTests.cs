#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;
using static tik4net.unittests.Golden.GoldenRender;

namespace tik4net.unittests.Golden
{
    /// <summary>
    /// T2 of the WinBox + MAC layer review, the message layer: every <see cref="M2Message"/> builder in each of its
    /// length classes, every parser over a hand-built frame per type byte, and the chunk framing both carriers share.
    /// </summary>
    /// <remarks>
    /// C10/C11 rewrite the builders over one field writer and the parsers over one TLV walker, and C3 moves the chunk
    /// framing; those commits claim byte-identical output and this is what checks the claim. The tracked results are
    /// <c>Golden/m2-builders.txt</c>, <c>Golden/m2-parsers.txt</c> and <c>Golden/m2-framing.txt</c>.
    /// <para><b>X5.</b> The parser rows for type bytes <c>0x30</c> (raw, 2-byte length — decoded as the text
    /// <c>"[nB]"</c>), <c>0x22</c>/<c>0x32</c> (4-byte length), <c>0x89</c>/<c>0x8A</c>, <c>0xA1</c>/<c>0xA2</c>,
    /// <c>0x80</c>–<c>0x82</c> and <c>0x99</c>/<c>0x9A</c> (skipped by <c>ParseAllFields</c> although
    /// <c>SkipTypeBytes</c> knows them) assert TODAY's behaviour, which is review item X5's defect. Phase 3 fixes X5 and
    /// changes exactly those lines, on purpose.</para>
    /// </remarks>
    [TestClass]
    public class M2MessageGoldenTests
    {
        private const int Sys = 0xFE0009;        // a full key in the record namespace (comment)
        private const int Sentinel = WinboxM2Protocol.SysKey.RequestId;

        private static byte[] Data(int n, int seed = 0) => Enumerable.Range(0, n).Select(i => (byte)(i * 13 + seed)).ToArray();
        private static string Text(int n) => new string(Enumerable.Range(0, n).Select(i => (char)('a' + i % 26)).ToArray());

        // ── builders ──────────────────────────────────────────────────────────

        [TestMethod]
        public void Builders_MatchTheGoldenFile()
        {
            var lines = new List<string> { "# T2 - M2Message builders, each length class" };
            void B(string name, Func<byte[]> build) => lines.Add(name + " " + Try(() => Bytes(build())));

            B("BuildM2()", () => M2Message.BuildM2());
            B("BuildM2(u8,str)", () => M2Message.BuildM2(M2Message.U8Sys(Sentinel, 1), M2Message.StringUser(1, "x")));
            B("SysToArr()", () => M2Message.SysToArr());
            B("SysToArr(24,1)", () => M2Message.SysToArr(24, 1));
            B("SysToArr(2,77,13,4)", () => M2Message.SysToArr(2, 77, 13, 4));
            B("SysFrom()", () => M2Message.SysFrom());
            B("SysFrom(76)", () => M2Message.SysFrom(76));
            B("BoolSys(true)", () => M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true));
            B("BoolSys(false)", () => M2Message.BoolSys(WinboxM2Protocol.RecordKey.Disabled, false));
            B("U8Sys(0)", () => M2Message.U8Sys(Sentinel, 0));
            B("U8Sys(255)", () => M2Message.U8Sys(Sentinel, 255));
            B("U32Sys(7)", () => M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, 7));
            B("U32Sys(0x10000005)", () => M2Message.U32Sys(WinboxM2Protocol.RecordKey.Flags, 0x10000005));
            B("U32Sys(-1)", () => M2Message.U32Sys(0x10006, -1));
            B("U32Sys(user key 5)", () => M2Message.U32Sys(5, 1234567));
            B("U64Sys(0)", () => M2Message.U64Sys(0x20003, 0));
            B("U64Sys(max)", () => M2Message.U64Sys(0x20003, ulong.MaxValue));
            B("U64Sys(1e12)", () => M2Message.U64Sys(0x20003, 1000000000000UL));
            B("MessageSys(empty)", () => M2Message.MessageSys(0x10007));
            B("MessageSys(short)", () => M2Message.MessageSys(0x10007, M2Message.U32Sys(0xFEFF20, 0x0104A8C0)));
            B("MessageSys(null field skipped)", () => M2Message.MessageSys(0x10007, null!, M2Message.U8Sys(1, 2)));
            B("MessageSys(normal 300)", () => M2Message.MessageSys(0x10007, M2Message.RawSys(1, Data(290))));
            B("MessageSys(long 70000)", () => M2Message.MessageSys(0x10007,
                Enumerable.Range(0, 300).Select(i => M2Message.RawSys(i + 1, Data(230, i))).ToArray()));
            B("SessionIdField(int 0)", () => M2Message.SessionIdField(0));
            B("SessionIdField(int 255)", () => M2Message.SessionIdField(255));
            B("SessionIdField(int 256)", () => M2Message.SessionIdField(256));
            B("SessionIdField(int 265)", () => M2Message.SessionIdField(265));
            B("SessionIdField(int -1)", () => M2Message.SessionIdField(-1));
            B("SessionIdField(uint 0xFFFFFFFD)", () => M2Message.SessionIdField(0xFFFFFFFDu));
            B("SessionIdField(uint 0x80000019)", () => M2Message.SessionIdField(0x80000019u));
            B("StringUser(empty)", () => M2Message.StringUser(1, ""));
            B("StringUser(short)", () => M2Message.StringUser(0x10, "ether1"));
            B("StringUser(utf8)", () => M2Message.StringUser(0x10, "Žluťoučký kůň"));
            B("StringUser(255)", () => M2Message.StringUser(0x123, Text(255)));
            B("StringUser(256)", () => M2Message.StringUser(0x123, Text(256)));
            B("StringUser(300)", () => M2Message.StringUser(0x123, Text(300)));
            B("StringUser(key 0x10006)", () => M2Message.StringUser(0x10006, "ns-dropped"));
            B("U32User(0)", () => M2Message.U32User(3, 0));
            B("U32User(80)", () => M2Message.U32User(3, 80));
            B("U32User(-1)", () => M2Message.U32User(0x1F00, -1));
            B("RawUser(empty)", () => M2Message.RawUser(9, new byte[0]));
            B("RawUser(16)", () => M2Message.RawUser(9, Data(16)));
            B("RawUser(255)", () => M2Message.RawUser(9, Data(255)));
            B("RawUser(256)", () => M2Message.RawUser(9, Data(256)));
            B("RawUser(300)", () => M2Message.RawUser(9, Data(300)));
            B("StringSys(null)", () => M2Message.StringSys(Sys, null!));
            B("StringSys(empty)", () => M2Message.StringSys(Sys, ""));
            B("StringSys(short)", () => M2Message.StringSys(Sys, "a comment"));
            B("StringSys(255)", () => M2Message.StringSys(Sys, Text(255)));
            B("StringSys(256)", () => M2Message.StringSys(Sys, Text(256)));
            B("StringSys(user key)", () => M2Message.StringSys(0x10, "ether1"));
            B("RawSys(null)", () => M2Message.RawSys(0x10003, null!));
            B("RawSys(6)", () => M2Message.RawSys(0x10003, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }));
            B("RawSys(255)", () => M2Message.RawSys(0x10003, Data(255)));
            B("RawSys(256)", () => M2Message.RawSys(0x10003, Data(256)));
            B("RawSys(1000)", () => M2Message.RawSys(0x10003, Data(1000)));
            B("Addr6Sys(16)", () => M2Message.Addr6Sys(0x10004, Data(16)));
            B("Addr6Sys(15)", () => M2Message.Addr6Sys(0x10004, Data(15)));
            B("Addr6Sys(null)", () => M2Message.Addr6Sys(0x10004, null!));
            B("U32ArraySys()", () => M2Message.U32ArraySys(0x10005));
            B("U32ArraySys(1,2,-1)", () => M2Message.U32ArraySys(0x10005, 1, 2, -1));
            B("U32ArraySys(To)", () => M2Message.U32ArraySys(WinboxM2Protocol.SysKey.To, 24, 1));
            B("U64ArraySys()", () => M2Message.U64ArraySys(0x10008, new ulong[0]));
            B("U64ArraySys(1,max)", () => M2Message.U64ArraySys(0x10008, new[] { 1UL, ulong.MaxValue }));
            B("StringArraySys()", () => M2Message.StringArraySys(0x10009, new string[0]));
            B("StringArraySys(a,null,utf8)", () => M2Message.StringArraySys(0x10009, new[] { "a", null!, "č" }));
            B("StringArraySys(300)", () => M2Message.StringArraySys(0x10009, new[] { Text(300) }));
            B("RawArraySys()", () => M2Message.RawArraySys(0x1000A, new byte[0][]));
            B("RawArraySys(2,null,300)", () => M2Message.RawArraySys(0x1000A, new[] { Data(2), null!, Data(300) }));
            B("Addr6ArraySys()", () => M2Message.Addr6ArraySys(0x1000B, new byte[0][]));
            B("Addr6ArraySys(2)", () => M2Message.Addr6ArraySys(0x1000B, new[] { Data(16), Data(16, 1) }));
            B("Addr6ArraySys(bad)", () => M2Message.Addr6ArraySys(0x1000B, new[] { Data(4) }));
            B("MessageArraySys()", () => M2Message.MessageArraySys(WinboxM2Protocol.RecordKey.Records, new byte[0][][]));
            B("MessageArraySys(2)", () => M2Message.MessageArraySys(WinboxM2Protocol.RecordKey.Records, new[]
            {
                new[] { M2Message.SessionIdField(1), M2Message.StringUser(0x10, "a") },
                new[] { M2Message.SessionIdField(2) },
            }));
            B("U32ArrayUser()", () => M2Message.U32ArrayUser(0x12));
            B("U32ArrayUser(5,6)", () => M2Message.U32ArrayUser(0x12, 5, 6));

            GoldenFile.AssertMatches("m2-builders.txt", lines);
        }

        // ── parsers ───────────────────────────────────────────────────────────

        /// <summary>A field header <c>[key lo][key hi][ns][type]</c> followed by <paramref name="body"/>.</summary>
        private static byte[] Field(int key, int type, params byte[][] body)
            => new[] { (byte)key, (byte)(key >> 8), (byte)(key >> 16), (byte)type }.Concat(body.SelectMany(b => b)).ToArray();

        /// <summary>A length or count in the width the type byte's size flags give it: short 1, normal 2, long 4.</summary>
        private static byte[] Len(int type, int n)
        {
            int w = (type & 1) != 0 ? 1 : (type & 2) != 0 ? 4 : 2;
            return Enumerable.Range(0, w).Select(i => (byte)(n >> (8 * i))).ToArray();
        }

        private static byte[] Elements(int type, IList<byte[]> elements, bool withLength)
            => Len(type, elements.Count).Concat(elements.SelectMany(e => withLength ? Len(type, e.Length).Concat(e) : e)).ToArray();

        /// <summary>One value of the given type byte, in the shape master*.js writes it.</summary>
        private static byte[] ValueOf(int type)
        {
            byte[] sub = M2Message.BuildM2(M2Message.U8Sys(0x1, 9), M2Message.StringUser(2, "in"));
            switch (type >> 3)
            {
                case 0: return new byte[0];                                         // bool: in the type byte
                case 1: return (type & 1) != 0 ? new byte[] { 0xC8 } : BitConverter.GetBytes(0x01020304u);
                case 2: return BitConverter.GetBytes(0x0102030405060708UL);
                case 3: return Data(16);
                case 4: { byte[] s = Encoding.UTF8.GetBytes(type == 0x21 ? "short" : Text(300)); return Len(type, s.Length).Concat(s).ToArray(); }
                case 5: return Len(type, sub.Length).Concat(sub).ToArray();
                case 6: { byte[] r = type == 0x31 ? Data(5) : Data(300); return Len(type, r.Length).Concat(r).ToArray(); }
                case 16: return Elements(type, new[] { new byte[] { 1 }, new byte[] { 0 } }, false);
                case 17: return Elements(type, new[] { BitConverter.GetBytes(7u), BitConverter.GetBytes(0xFFFFFFFFu) }, false);
                case 18: return Elements(type, new[] { BitConverter.GetBytes(7UL), BitConverter.GetBytes(ulong.MaxValue) }, false);
                case 19: return Elements(type, new[] { Data(16), Data(16, 3) }, false);
                case 20: return Elements(type, new[] { Encoding.UTF8.GetBytes("one"), Encoding.UTF8.GetBytes("two") }, true);
                case 21: return Elements(type, new[] { sub, M2Message.BuildM2(M2Message.SessionIdField(4)) }, true);
                case 22: return Elements(type, new[] { Data(3), Data(2, 5) }, true);
                default: return new byte[] { 0xDE, 0xAD };
            }
        }

        private static readonly int[] TypeBytes =
        {
            0x00, 0x01, 0x08, 0x09, 0x10, 0x18,
            0x20, 0x21, 0x22, 0x28, 0x29, 0x2A, 0x30, 0x31, 0x32,
            0x80, 0x81, 0x82, 0x88, 0x89, 0x8A, 0x90, 0x91, 0x92, 0x98, 0x99, 0x9A,
            0xA0, 0xA1, 0xA2, 0xA8, 0xA9, 0xAA, 0xB0, 0xB1, 0xB2,
            0x50,   // no ftype: the skipper stops here, and what follows is lost
        };

        [TestMethod]
        public void Parsers_MatchTheGoldenFile()
        {
            var lines = new List<string> { "# T2 - M2Message parsers, one hand-built frame per type byte.",
                "# Each frame: the field under key 0xFE0005 (or user key 5 / 0xFE0001 / 0xFE0002 / 0xFF0008 / 0xFF0006 where",
                "# noted), then the sentinel u8 0xFF0006=0x2A that shows whether the walk stayed in step.",
                "# X5: the 0x22 0x30 0x32 0x80-0x82 0x89 0x8A 0x99 0x9A 0xA1 0xA2 rows assert TODAY's ParseAllFields behaviour",
                "# (review item X5, a defect Phase 3 fixes deliberately)." };

            foreach (int type in TypeBytes)
            {
                byte[] value = ValueOf(type);
                byte[] tail = M2Message.U8Sys(Sentinel, 0x2A);
                string t = "0x" + type.ToString("X2");

                byte[] frame = M2Message.BuildM2(Field(0xFE0005, type, value), tail);
                lines.Add(t + " frame " + Bytes(frame));
                lines.Add(t + " SkipTypeBytes " + Try(() => M2Message.SkipTypeBytes(type, frame, 6).ToString()) + " (value is " + value.Length + " B)");
                lines.Add(t + " ParseAllFields " + Try(() => Fields(M2Message.ParseAllFields(frame))));
                lines.Add(t + " Describe " + Try(() => M2Message.Describe(frame)));
                lines.Add(t + " ParseRecords(0xFE0005) " + Try(() => Records(M2Message.ParseRecords(frame, 0xFE0005))));
                lines.Add(t + " ExtractRawField(0xFE0005) " + Try(() => Bytes(M2Message.ExtractRawField(frame, 0xFE0005))));
                lines.Add(t + " ExtractRawField(sentinel) " + Try(() => Bytes(M2Message.ExtractRawField(frame, Sentinel))));
                lines.Add(t + " ParseSysReqId " + Try(() => Value(M2Message.ParseSysReqId(frame))));
                lines.Add(t + " ParseSysU32(0xFE0005) " + Try(() => Value(M2Message.ParseSysU32(frame, 0xFE0005))));
                lines.Add(t + " ParseU32ArrayField(0xFE0005) " + Try(() => Ints(M2Message.ParseU32ArrayField(frame, 0xFE0005))));
                lines.Add(t + " RewriteU32ArrayField(0xFE0005,+99) " + Try(() => Bytes(M2Message.RewriteU32ArrayField(frame, 0xFE0005, v => v.Concat(new[] { 99 }).ToArray()))));

                byte[] user = M2Message.BuildM2(Field(5, type, value), tail);
                lines.Add(t + " user-key-5 ParseUserBytes " + Try(() => Bytes(M2Message.ParseUserBytes(user, 5))));
                lines.Add(t + " user-key-5 ParseRawUser " + Try(() => Bytes(M2Message.ParseRawUser(user, 5))));
                lines.Add(t + " user-key-5 ParseAllFields " + Try(() => Fields(M2Message.ParseAllFields(user))));

                byte[] id = M2Message.BuildM2(Field(WinboxM2Protocol.RecordKey.Id, type, value), tail);
                lines.Add(t + " as-0xFE0001 TryParseSessionId " + Try(() => M2Message.TryParseSessionId(id, out int s) + " " + s));
                lines.Add(t + " as-0xFE0001 ParseSessionId " + Try(() => M2Message.ParseSessionId(id).ToString()));

                byte[] status = M2Message.BuildM2(Field(WinboxM2Protocol.SysKey.ErrorCode, type, value), tail);
                lines.Add(t + " as-0xFF0008 ParseSysStatus " + Try(() => M2Message.ParseSysStatus(status).ToString()));
                lines.Add(t + " as-0xFF0008 DescribeSysError " + Try(() => M2Message.DescribeSysError(status)));

                byte[] recs = M2Message.BuildM2(Field(WinboxM2Protocol.RecordKey.Records, type, value), tail);
                lines.Add(t + " as-0xFE0002 ParseRecords " + Try(() => Records(M2Message.ParseRecords(recs, WinboxM2Protocol.RecordKey.Records))));

                byte[] reqId = M2Message.BuildM2(Field(Sentinel, type, value));
                lines.Add(t + " as-0xFF0006 ParseSysReqId " + Try(() => Value(M2Message.ParseSysReqId(reqId))));
            }

            // Shapes the per-type loop does not reach.
            byte[] twoKinds = M2Message.BuildM2(M2Message.U32Sys(0x1000C, 3), M2Message.U32ArraySys(0x1000C, 4, 5),
                M2Message.U32Sys(0x1000C, 6), M2Message.BoolSys(0x1001F, true), M2Message.U32Sys(0x1001F, 9));
            lines.Add("qualified-keys ParseAllFields " + Fields(M2Message.ParseAllFields(twoKinds)));
            lines.Add("qualified-keys Describe " + M2Message.Describe(twoKinds));
            lines.Add("not-m2 ParseAllFields " + Fields(M2Message.ParseAllFields(new byte[] { 1, 2, 3 })));
            lines.Add("not-m2 Describe " + M2Message.Describe(new byte[] { 1, 2, 3 }));
            lines.Add("null Describe " + M2Message.Describe(null!));
            lines.Add("empty ParseSysStatus " + M2Message.ParseSysStatus(new byte[0]));
            lines.Add("truncated-u32 ParseAllFields " + Fields(M2Message.ParseAllFields(new byte[] { (byte)'M', (byte)'2', 5, 0, 0xFE, 0x08, 1, 2 })));
            lines.Add("truncated-str ParseAllFields " + Fields(M2Message.ParseAllFields(new byte[] { (byte)'M', (byte)'2', 5, 0, 0xFE, 0x21, 9, 0x41 })));
            lines.Add("truncated-raw ParseUserBytes " + Try(() => Bytes(M2Message.ParseUserBytes(new byte[] { (byte)'M', (byte)'2', 5, 0, 0, 0x31 }, 5))));
            lines.Add("truncated-raw ParseRawUser " + Try(() => Bytes(M2Message.ParseRawUser(new byte[] { (byte)'M', (byte)'2', 5, 0, 0, 0x31 }, 5))));
            lines.Add("truncated-raw ExtractRawField " + Try(() => Bytes(M2Message.ExtractRawField(new byte[] { (byte)'M', (byte)'2', 5, 0, 0xFE, 0x31, 9, 1 }, 0xFE0005))));
            byte[] errored = M2Message.BuildM2(M2Message.U32Sys(WinboxM2Protocol.SysKey.ErrorCode, 0xFE0006),
                M2Message.StringSys(WinboxM2Protocol.SysKey.ErrorString, "timeout"));
            lines.Add("error+string DescribeSysError " + M2Message.DescribeSysError(errored));
            lines.Add("error+string ParseSessionId " + Try(() => M2Message.ParseSessionId(errored).ToString()));
            lines.Add("no-error DescribeSysError " + M2Message.DescribeSysError(M2Message.BuildM2(M2Message.U8Sys(Sentinel, 1))));
            lines.Add("FType 0x29 " + M2Message.FType(0x29) + " 0xA8 " + M2Message.FType(0xA8) + " 0x18 " + M2Message.FType(0x18));

            GoldenFile.AssertMatches("m2-parsers.txt", lines);
        }

        private static string Ints(int[]? values) => values == null ? "null" : "[" + string.Join(",", values) + "]";

        // ── chunk framing (C3) ────────────────────────────────────────────────

        [TestMethod]
        public void ChunkFraming_MatchesTheGoldenFile()
        {
            var lines = new List<string> { "# T2 - WinBox chunk framing shared by both carriers (WinboxTcpTransport)" };
            foreach (int n in new[] { 0, 1, 254, 255, 256, 509, 510, 511, 3570 })
            {
                byte[] data = Data(n);
                byte[] chunked = WinboxTcpTransport.Chunk(data, 0x06);
                lines.Add("Chunk(" + n + ",0x06) " + Bytes(chunked));
                if (n <= 600) lines.Add("BuildRawFrame(" + n + ") " + Bytes(WinboxTcpTransport.BuildRawFrame(data)));

                int pos = 0;
                byte[] back = WinboxTcpTransport.ReadChunkedFrame(k => { byte[] r = chunked.Skip(pos).Take(k).ToArray(); pos += k; return r; }, 0x06, encrypted: false);
                lines.Add("ReadChunkedFrame(" + n + ") " + (back.SequenceEqual(data) ? "round-trips" : "DIFFERS " + Bytes(back)) + ", consumed " + pos + " of " + chunked.Length);

                var buffer = chunked.ToList();
                byte[]? taken = WinboxTcpTransport.TryTakeChunkedFrame(buffer, encrypted: false);
                lines.Add("TryTakeChunkedFrame(" + n + ") " + (taken != null && taken.SequenceEqual(data) ? "round-trips" : Bytes(taken)) + ", left " + buffer.Count);
                var partial = chunked.Take(chunked.Length - 1).ToList();
                lines.Add("TryTakeChunkedFrame(" + n + " less one byte) " + Bytes(WinboxTcpTransport.TryTakeChunkedFrame(partial, encrypted: false)) + ", left " + partial.Count);
            }

            // An encrypted frame of exactly 14 full chunks (3570 B) ends at its declared length with no terminator,
            // and a terminator where a frame starts is skipped.
            byte[] enc = new byte[3570];
            const int declared = 3570 - 18;
            enc[0] = declared >> 8; enc[1] = declared & 0xFF;
            byte[] encChunks = WinboxTcpTransport.Chunk(enc, 0x06);
            byte[] noTerminator = encChunks.Take(encChunks.Length - 2).ToArray();
            byte[] next = WinboxTcpTransport.Chunk(Data(3), 0x06);
            var stream = noTerminator.Concat(next).ToList();
            lines.Add("encrypted-3570 TryTake first " + (WinboxTcpTransport.TryTakeChunkedFrame(stream, encrypted: true)?.Length.ToString() ?? "null") + ", left " + stream.Count);
            lines.Add("encrypted-3570 TryTake second " + Bytes(WinboxTcpTransport.TryTakeChunkedFrame(stream, encrypted: true)) + ", left " + stream.Count);
            var terminated = new List<byte> { 0, 0xFF };
            terminated.AddRange(next);
            lines.Add("leading-terminator TryTake " + Bytes(WinboxTcpTransport.TryTakeChunkedFrame(terminated, encrypted: true)) + ", left " + terminated.Count);
            int p2 = 0;
            byte[] read2 = new byte[] { 0, 0xFF }.Concat(next).ToArray();
            lines.Add("leading-terminator ReadChunkedFrame " + Bytes(WinboxTcpTransport.ReadChunkedFrame(k => { byte[] r = read2.Skip(p2).Take(k).ToArray(); p2 += k; return r; }, 0x06, encrypted: true)));
            int p3 = 0;
            lines.Add("wrong-tag ReadChunkedFrame " + Try(() => Bytes(WinboxTcpTransport.ReadChunkedFrame(k => { byte[] r = next.Skip(p3).Take(k).ToArray(); p3 += k; return r; }, 0x01, encrypted: false))));
            lines.Add("BuildRawFrame(65536) " + Try(() => Bytes(WinboxTcpTransport.BuildRawFrame(new byte[65536]))));
            lines.Add("constants EncryptedFrameTag=0x" + WinboxTcpTransport.EncryptedFrameTag.ToString("X2")
                + " ContinuationTag=0x" + WinboxTcpTransport.ContinuationTag.ToString("X2")
                + " EncryptedFrameOverhead=" + WinboxTcpTransport.EncryptedFrameOverhead);

            GoldenFile.AssertMatches("m2-framing.txt", lines);
        }
    }
}
