using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Loopback tests for <c>getall</c> pagination (P2.9) — the client must follow <b>both</b> continuation
    /// keys a handler can page with, and carry each one back exactly as received.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <see cref="WinboxM2Protocol.RecordKey.Continuation"/> (<c>ufe0003</c>) was followed before; a
    /// handler paging via <see cref="WinboxM2Protocol.RecordKey.ContinuationRaw"/> (<c>mfe0015</c>) would have
    /// had its first page returned as the whole answer — wrong results, silently. No live handler is known to
    /// use it, which is exactly why these are scripted: the failure mode is invisible without a peer that
    /// pages that way, and waiting for one to turn up is not coverage.
    /// </para>
    /// <para>
    /// The message-array TLVs are built here by hand rather than with a production helper, so the tests pin
    /// the wire shape independently of the code under test.
    /// </para>
    /// </remarks>
    [TestClass]
    public class WinboxM2PaginationTests
    {
        private const string Host = "127.0.0.1";
        private const int TimeoutMs = 5000;
        private static readonly int[] Handler = { 24, 1 };
        private const int NameKey = 0x06;   // arbitrary user-namespace field carried by each fake record

        // ── wire builders (test-side, deliberately not shared with production) ──

        /// <summary>Builds a message-array field (ftype 21, normal form): 2B count + (2B len + submessage)*.</summary>
        private static byte[] MessageArray(int fullKey, params byte[][] elements)
        {
            var b = new List<byte>
            {
                (byte)(fullKey & 0xFF), (byte)((fullKey >> 8) & 0xFF), (byte)((fullKey >> 16) & 0xFF), 0xA8
            };
            b.AddRange(BitConverter.GetBytes((ushort)elements.Length));
            foreach (var e in elements)
            {
                b.AddRange(BitConverter.GetBytes((ushort)e.Length));
                b.AddRange(e);
            }
            return b.ToArray();
        }

        /// <summary>A one-record page: the records array plus whatever continuation fields the test wants.</summary>
        private static byte[] Page(int requestId, string recordName, params byte[][] continuationFields)
        {
            var fields = new List<byte[]>
            {
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, (byte)requestId),
                MessageArray(WinboxM2Protocol.RecordKey.Records,
                    M2Message.BuildM2(M2Message.StringUser(NameKey, recordName))),
            };
            fields.AddRange(continuationFields);
            return M2Message.BuildM2(fields.ToArray());
        }

        /// <summary>An opaque <c>mfe0015</c> cursor — content is meaningless to us, which is the point.</summary>
        private static byte[] RawCursor(params int[] marker)
            => MessageArray(WinboxM2Protocol.RecordKey.ContinuationRaw,
                   M2Message.BuildM2(marker.Select(m => M2Message.U32User(0x11, m)).ToArray()));

        private static int RequestIdOf(byte[] m2)
        {
            var fields = M2Message.ParseAllFields(m2);
            return fields.TryGetValue(WinboxM2Protocol.SysKey.RequestId, out var t) && t.Item2 != null
                ? Convert.ToInt32(t.Item2) : -1;
        }

        private static string NameOf(Dictionary<int, Tuple<string, object>> record)
            => record.TryGetValue(NameKey, out var t) ? t.Item2 as string : null;

        /// <summary>Logs in, then hands the test a live operations layer over the scripted server.</summary>
        private static async Task WithOperations(Action<FakeWinboxServer> serverScript,
            Action<WinboxNativeM2Operations, FakeWinboxServer> clientScript)
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.RunFullLoginSequence();
                    serverScript?.Invoke(server);
                });

                await Task.Run(() =>
                {
                    session.Open(Host, server.Port, "admin", "", TimeoutMs, TimeoutMs);
                    clientScript(new WinboxNativeM2Operations(session, TimeoutMs), server);
                });

                await serverTask;
            }
        }

        /// <summary>
        /// The defect P2.9 names: a handler that pages via <c>mfe0015</c> must be followed, and the cursor
        /// must go back byte-identical. Asserting the <b>bytes</b> is what separates this from a client that
        /// decodes and rebuilds the token — that would pass a "did you send something" check while sending a
        /// cursor of its own invention.
        /// </summary>
        [TestMethod]
        public async Task GetAll_FollowsRawContinuation_AndEchoesTheCursorVerbatim()
        {
            byte[] cursor = RawCursor(0xDEAD, 0xBEEF);
            byte[] secondRequest = null;

            await WithOperations(
                server =>
                {
                    byte[] first = server.ReadRawFrame();
                    server.SendRawFrame(Page(RequestIdOf(first), "page1", cursor));

                    secondRequest = server.ReadRawFrame();
                    server.SendRawFrame(Page(RequestIdOf(secondRequest), "page2"));
                },
                (ops, server) =>
                {
                    var records = ops.GetAll(Handler);

                    CollectionAssert.AreEqual(new[] { "page1", "page2" }, records.Select(NameOf).ToArray(),
                        "Both pages must reach the caller — the first page alone is the P2.9 wrong-results bug.");

                    byte[] echoed = M2Message.ExtractRawField(
                        secondRequest, WinboxM2Protocol.RecordKey.ContinuationRaw);
                    Assert.IsNotNull(echoed, "The follow-up request must carry the mfe0015 cursor back.");
                    CollectionAssert.AreEqual(cursor, echoed,
                        "The cursor is opaque: it must be echoed byte-for-byte, not re-encoded.");
                });
        }

        /// <summary>The u32 cursor keeps working — the path that was already live must not regress.</summary>
        [TestMethod]
        public async Task GetAll_FollowsU32Continuation()
        {
            await WithOperations(
                server =>
                {
                    byte[] first = server.ReadRawFrame();
                    server.SendRawFrame(Page(RequestIdOf(first), "page1",
                        M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 7)));

                    byte[] second = server.ReadRawFrame();
                    Assert.AreEqual(7u,
                        M2Message.ParseAllFields(second)[WinboxM2Protocol.RecordKey.Continuation].Item2,
                        "The u32 cursor must be carried back on the follow-up request.");
                    server.SendRawFrame(Page(RequestIdOf(second), "page2"));
                },
                (ops, server) =>
                    CollectionAssert.AreEqual(new[] { "page1", "page2" },
                        ops.GetAll(Handler).Select(NameOf).ToArray()));
        }

        /// <summary>
        /// A u32 cursor at or above <c>0x80000000</c>. The old code round-tripped the token through
        /// <c>Convert.ToInt32</c>, which throws <see cref="OverflowException"/> on exactly these values —
        /// a paginating handler with a high cursor would have failed the whole read, not just truncated it.
        /// Echoing the raw bytes removes the numeric round trip, so the value cannot matter any more.
        /// </summary>
        [TestMethod]
        public async Task GetAll_FollowsU32Continuation_AboveInt32Range()
        {
            await WithOperations(
                server =>
                {
                    byte[] first = server.ReadRawFrame();
                    server.SendRawFrame(Page(RequestIdOf(first), "page1",
                        M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, unchecked((int)0x80000001))));

                    byte[] second = server.ReadRawFrame();
                    Assert.AreEqual(0x80000001u,
                        M2Message.ParseAllFields(second)[WinboxM2Protocol.RecordKey.Continuation].Item2);
                    server.SendRawFrame(Page(RequestIdOf(second), "page2"));
                },
                (ops, server) =>
                    CollectionAssert.AreEqual(new[] { "page1", "page2" },
                        ops.GetAll(Handler).Select(NameOf).ToArray()));
        }

        /// <summary>Both cursors at once: webfig echoes each one it received, so we must too.</summary>
        [TestMethod]
        public async Task GetAll_EchoesBothCursors_WhenTheReplyCarriesBoth()
        {
            byte[] cursor = RawCursor(0x0102);

            await WithOperations(
                server =>
                {
                    byte[] first = server.ReadRawFrame();
                    server.SendRawFrame(Page(RequestIdOf(first), "page1",
                        M2Message.U32Sys(WinboxM2Protocol.RecordKey.Continuation, 3), cursor));

                    byte[] second = server.ReadRawFrame();
                    var fields = M2Message.ParseAllFields(second);
                    Assert.AreEqual(3u, fields[WinboxM2Protocol.RecordKey.Continuation].Item2);
                    CollectionAssert.AreEqual(cursor,
                        M2Message.ExtractRawField(second, WinboxM2Protocol.RecordKey.ContinuationRaw));
                    server.SendRawFrame(Page(RequestIdOf(second), "page2"));
                },
                (ops, server) =>
                    CollectionAssert.AreEqual(new[] { "page1", "page2" },
                        ops.GetAll(Handler).Select(NameOf).ToArray()));
        }

        /// <summary>
        /// No cursor of either kind ends the read after one request. Guards the other direction: a client
        /// that treats a missing cursor as "keep asking" would spin against every non-paging handler.
        /// </summary>
        [TestMethod]
        public async Task GetAll_StopsAfterOnePage_WhenNoCursorIsReturned()
        {
            await WithOperations(
                server =>
                {
                    byte[] first = server.ReadRawFrame();
                    server.SendRawFrame(Page(RequestIdOf(first), "only"));
                },
                (ops, server) =>
                {
                    var records = ops.GetAll(Handler);
                    Assert.AreEqual(1, records.Count);
                    Assert.AreEqual(5, server.ReceivedMessages.Count,
                        "4 login messages + exactly one getall: an absent cursor must end the read.");
                });
        }
    }
}
