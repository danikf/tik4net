using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests
{
    /// <summary>
    /// Router-free coverage of how a WinBox handshake reply is classified and how a refusal is ridden
    /// out (P2.41).
    /// </summary>
    /// <remarks>
    /// The live behaviour these encode was measured on RouterOS 7.23.2: about one WinBox login in one to
    /// two hundred comes back with the 33-byte payload <c>"invalid user name or password (6)"</c> where
    /// a 32-byte confirmation digest belongs, although the credentials are correct — replaying the same
    /// client key immediately is accepted. The rules below are the ones that make that survivable without
    /// also papering over a real credential failure, and they are exactly the rules a live run cannot
    /// pin down (a lab router with correct credentials never produces the negative cases at all).
    /// </remarks>
    [TestClass]
    public class WinboxLoginRefusalTests
    {
        private const int ConfirmationLength = 32;

        // ── classifying the reply ─────────────────────────────────────────────

        /// <summary>The observed refusal, verbatim, must be recognised as the router speaking.</summary>
        [TestMethod]
        public void RouterRefusalText_IsReportedAsRefusal()
        {
            byte[] payload = Encoding.ASCII.GetBytes("invalid user name or password (6)");
            Assert.AreEqual(33, payload.Length, "the measured refusal is 33 bytes");

            var ex = Assert.ThrowsException<WinboxLoginRefusedException>(
                () => WinboxHandshakeReply.ThrowIfRouterMessage(payload, ConfirmationLength));

            Assert.AreEqual("invalid user name or password (6)", ex.RouterMessage);
            StringAssert.Contains(ex.Message, "invalid user name or password (6)");
        }

        /// <summary>
        /// A payload of the right size is a digest and must pass through untouched — even in the
        /// pathological case where every byte happens to be printable, which would otherwise be read as
        /// a refusal and turn a good login into an error.
        /// </summary>
        [TestMethod]
        public void PayloadOfConfirmationLength_IsLeftToTheDigestComparison()
        {
            WinboxHandshakeReply.ThrowIfRouterMessage(new byte[ConfirmationLength], ConfirmationLength);
            WinboxHandshakeReply.ThrowIfRouterMessage(
                Encoding.ASCII.GetBytes(new string('A', ConfirmationLength)), ConfirmationLength);
        }

        /// <summary>
        /// A wrong-sized payload that is not text is a broken handshake, not a credential verdict — the
        /// conflation P2.41 was filed against.
        /// </summary>
        [TestMethod]
        public void WrongSizedBinaryPayload_IsReportedAsBrokenHandshake()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(
                () => WinboxHandshakeReply.ThrowIfRouterMessage(new byte[] { 0x01, 0x00, 0xFF }, ConfirmationLength));

            StringAssert.Contains(ex.Message, "did not complete");
            Assert.IsFalse(ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0,
                "a broken handshake must not be described as a credential problem");
        }

        [TestMethod]
        public void EmptyOrNullPayload_IsReportedAsBrokenHandshake()
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => WinboxHandshakeReply.ThrowIfRouterMessage(new byte[0], ConfirmationLength));
            Assert.ThrowsException<InvalidOperationException>(
                () => WinboxHandshakeReply.ThrowIfRouterMessage(null, ConfirmationLength));
        }

        [TestMethod]
        public void AsPrintableText_RejectsAnythingWithControlOrHighBytes()
        {
            Assert.IsNull(WinboxHandshakeReply.AsPrintableText(new byte[] { 0x41, 0x00, 0x42 }));
            Assert.IsNull(WinboxHandshakeReply.AsPrintableText(new byte[] { 0x41, 0x80 }));
            Assert.IsNull(WinboxHandshakeReply.AsPrintableText(new byte[] { 0x41, 0x0A }));
            Assert.AreEqual("A B", WinboxHandshakeReply.AsPrintableText(new byte[] { 0x41, 0x20, 0x42 }));
        }

        // ── riding it out ─────────────────────────────────────────────────────

        /// <summary>A refusal that clears must be invisible to the caller.</summary>
        [TestMethod]
        public void Run_RetriesUntilTheRefusalClears()
        {
            int attempts = 0;
            WinboxLoginRetry.Run(() =>
            {
                attempts++;
                if (attempts < 2) throw new WinboxLoginRefusedException("invalid user name or password (6)");
            });

            Assert.AreEqual(2, attempts);
        }

        /// <summary>
        /// A refusal that never clears is a credential failure and must surface — bounded, so a wrong
        /// password fails in a predictable time rather than looping.
        /// </summary>
        [TestMethod]
        public void Run_GivesUpAfterMaxAttempts_AndRethrowsTheRefusal()
        {
            int attempts = 0;
            Assert.ThrowsException<WinboxLoginRefusedException>(() => WinboxLoginRetry.Run(() =>
            {
                attempts++;
                throw new WinboxLoginRefusedException("invalid user name or password (6)");
            }));

            Assert.AreEqual(WinboxLoginRetry.MaxAttempts, attempts);
        }

        /// <summary>
        /// The refusal reaches the retry wrapped — <see cref="TikConnectionLoginException"/> at the
        /// connection layer, and an <see cref="AggregateException"/> too when it came out of a task — so
        /// matching only the outermost type would quietly disable the whole mechanism.
        /// </summary>
        [TestMethod]
        public void Run_SeesTheRefusalThroughWrappingExceptions()
        {
            foreach (var wrap in new Func<Exception, Exception>[]
            {
                inner => new TikConnectionLoginException(inner),
                inner => new AggregateException(inner),
                inner => new TikConnectionLoginException(new AggregateException(inner)),
            })
            {
                int attempts = 0;
                WinboxLoginRetry.Run(() =>
                {
                    attempts++;
                    if (attempts < 2)
                        throw wrap(new WinboxLoginRefusedException("invalid user name or password (6)"));
                });
                Assert.AreEqual(2, attempts, "unwrapping failed for " + wrap(new Exception("x")).GetType().Name);
            }
        }

        /// <summary>
        /// Only the refusal is retried. Retrying a broken handshake or a socket error would turn one
        /// clear failure into three slow ones and hide the cause.
        /// </summary>
        [TestMethod]
        public void Run_DoesNotRetryAnythingElse()
        {
            foreach (Exception other in new Exception[]
            {
                new InvalidOperationException("WinBox EC-SRP5 handshake: ... did not complete."),
                new UnauthorizedAccessException("Wrong username or password"),
                new System.IO.IOException("Connection closed unexpectedly"),
            })
            {
                int attempts = 0;
                Exception thrown = null;
                try
                {
                    WinboxLoginRetry.Run(() => { attempts++; throw other; });
                }
                catch (Exception ex) { thrown = ex; }

                Assert.AreSame(other, thrown, "the original exception must propagate unchanged");
                Assert.AreEqual(1, attempts, "retried " + other.GetType().Name);
            }
        }

        [TestMethod]
        public async Task RunAsync_BehavesLikeRun()
        {
            int attempts = 0;
            await WinboxLoginRetry.RunAsync(() =>
            {
                attempts++;
                if (attempts < 2) throw new WinboxLoginRefusedException("invalid user name or password (6)");
                return Task.CompletedTask;
            });
            Assert.AreEqual(2, attempts);

            int failing = 0;
            await Assert.ThrowsExceptionAsync<WinboxLoginRefusedException>(
                () => WinboxLoginRetry.RunAsync(() =>
                {
                    failing++;
                    throw new WinboxLoginRefusedException("invalid user name or password (6)");
                }));
            Assert.AreEqual(WinboxLoginRetry.MaxAttempts, failing);
        }
    }
}
