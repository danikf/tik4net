using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Pins <see cref="M2Message.TryParseSessionId"/>, which the WinBox-CLI reader uses to decide whether an
    /// arriving mepty frame belongs to the terminal it is driving. Getting this wrong is not cosmetic: a
    /// foreign session's bytes entering the buffer corrupt the response and get counted into the per-session
    /// byte acknowledgement (see the P2.13c note), and a frame that carries no SESSION_ID at all must stay
    /// acceptable rather than being silently discarded.
    /// </summary>
    [TestClass]
    public class M2MessageSessionIdTests
    {
        [TestMethod]
        public void TryParseSessionId_ReadsSmallIdEncodedAsU8()
        {
            byte[] msg = M2Message.BuildM2(M2Message.SessionIdField(7));

            Assert.IsTrue(M2Message.TryParseSessionId(msg, out int id));
            Assert.AreEqual(7, id);
        }

        /// <summary>RouterOS switches the SESSION_ID to u32 above 255; a client that truncates addresses the
        /// wrong terminal, so both encodings must round-trip.</summary>
        [TestMethod]
        public void TryParseSessionId_ReadsLargeIdEncodedAsU32()
        {
            byte[] msg = M2Message.BuildM2(M2Message.SessionIdField(265));

            Assert.IsTrue(M2Message.TryParseSessionId(msg, out int id));
            Assert.AreEqual(265, id);
        }

        [TestMethod]
        public void TryParseSessionId_FindsIdAmongOtherFields()
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler),
                M2Message.SysFrom(),
                M2Message.SessionIdField(300),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, new byte[] { 1, 2, 3 }),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Counter, 8192));

            Assert.IsTrue(M2Message.TryParseSessionId(msg, out int id));
            Assert.AreEqual(300, id);
        }

        /// <summary>A frame carrying no SESSION_ID cannot be attributed to a session — the reader treats that
        /// as "accept", so this must report false rather than throw.</summary>
        [TestMethod]
        public void TryParseSessionId_ReturnsFalseWhenNoSessionIdField()
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data));

            Assert.IsFalse(M2Message.TryParseSessionId(msg, out int id));
            Assert.AreEqual(-1, id);
        }

        [TestMethod]
        public void TryParseSessionId_ReturnsFalseForMalformedInput()
        {
            Assert.IsFalse(M2Message.TryParseSessionId(null, out _));
            Assert.IsFalse(M2Message.TryParseSessionId(new byte[0], out _));
            Assert.IsFalse(M2Message.TryParseSessionId(new byte[] { (byte)'X', (byte)'9', 0, 0 }, out _));
        }

        /// <summary>The throwing wrapper keeps its contract: malformed input and a missing SESSION_ID are
        /// distinct failures, and callers such as the mepty Login path rely on it throwing rather than
        /// returning a bogus id.</summary>
        [TestMethod]
        public void ParseSessionId_ThrowsWhenAbsentOrMalformed()
        {
            byte[] noSessionId = M2Message.BuildM2(M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler));

            Assert.ThrowsException<InvalidOperationException>(() => M2Message.ParseSessionId(noSessionId));
            Assert.ThrowsException<InvalidOperationException>(
                () => M2Message.ParseSessionId(new byte[] { (byte)'X', (byte)'9', 0, 0 }));
            Assert.AreEqual(42, M2Message.ParseSessionId(M2Message.BuildM2(M2Message.SessionIdField(42))));
        }
    }
}
