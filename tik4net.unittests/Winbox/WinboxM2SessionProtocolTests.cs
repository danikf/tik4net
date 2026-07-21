using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Loopback protocol tests for <see cref="WinboxM2Session"/> against <see cref="FakeWinboxServer"/>.
    /// <para>
    /// These pin the <b>current lockstep behaviour</b> so the multiplexing rewrite
    /// (<c>_notes/async/P2-winbox-multiplexing-design.md</c>) has a regression net — the same role
    /// <c>ApiConnectionProtocolTests</c> plays for the binary API's reader-loop rewrite (P1.7 → P2.3).
    /// They must keep passing afterwards.
    /// </para>
    /// </summary>
    [TestClass]
    public class WinboxM2SessionProtocolTests
    {
        private const string Host = "127.0.0.1";
        private const int ConnectTimeoutMs = 5000;
        private const int IoTimeoutMs = 5000;

        /// <summary>
        /// Runs the fake server's scripted side on a background task while the caller drives the client,
        /// then surfaces any server-side failure (a server assert that fails inside the task would otherwise
        /// show up only as a client-side timeout).
        /// </summary>
        private static async Task WithSession(Action<FakeWinboxServer> serverScript,
            Action<WinboxM2Session, FakeWinboxServer> clientScript, int sessionId = 265)
        {
            using (var server = new FakeWinboxServer())
            using (var session = new WinboxM2Session())
            {
                var serverTask = Task.Run(() =>
                {
                    server.RunFullLoginSequence(sessionId);
                    serverScript?.Invoke(server);
                });

                await Task.Run(() =>
                {
                    session.Open(Host, server.Port, "admin", "", ConnectTimeoutMs, IoTimeoutMs);
                    clientScript(session, server);
                });

                await serverTask;   // rethrows anything the scripted server hit
            }
        }

        [TestMethod]
        public async Task Open_FallsBackToLegacyMd5_WhenServerRejectsEcSrp5()
        {
            await WithSession(null, (session, server) =>
            {
                Assert.IsFalse(session.IsEncrypted,
                    "Legacy MD5 auth must leave the channel unencrypted.");
                Assert.AreEqual(4, server.ReceivedMessages.Count,
                    "Legacy handshake is exactly 4 client messages: list, setup, get-salt, login.");
            });
        }

        /// <summary>
        /// Regression for findings-winbox.md §1: a session id above 255 must round-trip as u32. A u8-only
        /// encoder truncated 265 to 9 and addressed a dead session — the bug that made mepty look like a
        /// drain-timing problem.
        /// </summary>
        [TestMethod]
        public async Task Open_EchoesSessionIdAbove255_AsU32()
        {
            await WithSession(null, (session, server) =>
            {
                // Messages after the first (list) carry the session id the server handed out.
                var withSessionId = server.ReceivedMessages.Skip(1).ToList();
                Assert.AreEqual(3, withSessionId.Count);

                foreach (var msg in withSessionId)
                    Assert.AreEqual(265, M2Message.ParseSessionId(msg),
                        "Session id > 255 must be encoded as u32, not truncated to a byte.");
            }, sessionId: 265);
        }

        /// <summary>
        /// The request id advances by one per outbound message and starts at 1. This is the invariant the
        /// multiplexing dispatch will key on (findings-winbox.md §12.1); once concurrent senders exist the
        /// counter also has to become interlocked (§12.6), but the sequence itself must not change.
        /// </summary>
        [TestMethod]
        public async Task RequestId_IncrementsPerMessage_StartingAtOne()
        {
            await WithSession(null, (session, server) =>
            {
                var reqIds = server.ReceivedMessages
                    .Select(M2Message.ParseSysReqId)
                    .ToList();

                CollectionAssert.AreEqual(new int?[] { 1, 2, 3, 4 }, reqIds,
                    "Request ids must be a gap-free sequence starting at 1.");
            });
        }

        /// <summary>
        /// A response larger than one 255-byte chunk must reassemble. Real responses exceed this routinely —
        /// the interface <c>getall</c> traced in findings-winbox.md §12.1 is 612 B.
        /// </summary>
        [TestMethod]
        public async Task SendReceive_ReassemblesResponseSpanningMultipleChunks()
        {
            byte[] payload = Enumerable.Range(0, 900).Select(i => (byte)(i % 251)).ToArray();
            byte[] reply = M2Message.BuildM2(
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, 5),
                M2Message.RawUser(0x0C, payload));

            await WithSession(
                server =>
                {
                    server.ReadRawFrame();
                    server.SendRawFrame(reply);
                },
                (session, server) =>
                {
                    byte[] request = M2Message.BuildM2(
                        M2Message.SysToArr(24, 1), M2Message.SysFrom(), session.NextReqIdField());

                    byte[] response = session.SendReceive(request, IoTimeoutMs);

                    CollectionAssert.AreEqual(payload, M2Message.ParseRawUser(response, 0x0C),
                        "A multi-chunk response must reassemble byte-for-byte.");
                });
        }

        /// <summary>
        /// A response whose body is an exact multiple of the 255-byte chunk size needs a trailing
        /// zero-length chunk to terminate — otherwise the reader waits for a continuation that never comes.
        /// Boundary case of the framing in <c>WinboxTcpTransport.SendChunked</c>.
        /// </summary>
        [TestMethod]
        public async Task SendReceive_HandlesResponseOnExactChunkBoundary()
        {
            // The framed body is [2-byte inner length] + reply, and it must come out an exact multiple of
            // 255. Measure the RawUser overhead rather than hard-coding it — the long-form encoder spends 6
            // bytes (4 key/type + 2 length), and assuming 4 silently pushed this test two bytes off the
            // boundary, where it passed without exercising anything.
            byte[] head = M2Message.BuildM2(M2Message.SysToArr(0, 8), M2Message.SysFrom());
            int rawUserOverhead = M2Message.RawUser(0x0C, new byte[300]).Length - 300;
            int payloadLen = 255 * 3 - 2 - head.Length - rawUserOverhead;

            byte[] payload = Enumerable.Repeat((byte)0xAB, payloadLen).ToArray();
            byte[] reply = M2Message.BuildM2(
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.RawUser(0x0C, payload));

            Assert.AreEqual(0, (2 + reply.Length) % 255,
                "This test is only meaningful when the framed body lands exactly on a chunk boundary.");

            await WithSession(
                server =>
                {
                    server.ReadRawFrame();
                    server.SendRawFrame(reply);
                },
                (session, server) =>
                {
                    byte[] request = M2Message.BuildM2(
                        M2Message.SysToArr(24, 1), M2Message.SysFrom(), session.NextReqIdField());

                    byte[] response = session.SendReceive(request, IoTimeoutMs);

                    CollectionAssert.AreEqual(payload, M2Message.ParseRawUser(response, 0x0C));
                });
        }

        /// <summary>
        /// The response's request id is readable by <see cref="M2Message.ParseSysReqId"/> — the parser the
        /// multiplexing reader loop dispatches on. Also guards §12.2: <c>0xFF0003</c> must not be mistaken
        /// for the correlation field, so here it deliberately carries a <i>different</i> value.
        /// </summary>
        [TestMethod]
        public async Task ParseSysReqId_ReadsEchoedRequestId_NotTheSessionScopedField()
        {
            const int echoedReqId = 7;
            byte[] reply = M2Message.BuildM2(
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.U8Sys(0xFF0003, 2),                 // session-scoped, constant — NOT correlation
                M2Message.U8Sys(WinboxM2Protocol.SysKey.RequestId, echoedReqId));

            await WithSession(
                server =>
                {
                    server.ReadRawFrame();
                    server.SendRawFrame(reply);
                },
                (session, server) =>
                {
                    byte[] request = M2Message.BuildM2(
                        M2Message.SysToArr(24, 1), M2Message.SysFrom(), session.NextReqIdField());

                    byte[] response = session.SendReceive(request, IoTimeoutMs);

                    Assert.AreEqual(echoedReqId, M2Message.ParseSysReqId(response),
                        "Dispatch must key on 0xFF0006, not the constant 0xFF0003.");
                });
        }

        [TestMethod]
        public void ParseSysReqId_ReturnsNull_WhenFieldAbsent()
        {
            byte[] m2 = M2Message.BuildM2(M2Message.SysToArr(0, 8), M2Message.SysFrom());

            Assert.IsNull(M2Message.ParseSysReqId(m2),
                "A message without 0xFF0006 must report no request id, not a default of 0.");
        }
    }
}
