using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The terminal width every PTY transport advertises to RouterOS. The router decides whether to wrap
    /// long <c>:put … as-value</c> output itself — inserting a <c>\r\n</c> into the middle of a value —
    /// from what these replies say, so the answers are the contract, not an implementation detail.
    /// See <see cref="Vt100State.RouterOsWidth"/> and Docs/findings-cli.md §6.
    /// </summary>
    [TestClass]
    public class Vt100StateTests
    {
        /// <summary>
        /// RouterOS's width probe, captured off the wire on 7.24: home the cursor, drive it as far right as
        /// it will go, ask where it is — then print a space and ask again, twice.
        /// </summary>
        private const string WidthProbe = "\x1B[H\x1B[9999C\x1B[6n \x1B[6n \x1B[6n";

        [TestMethod]
        public void TheWidthProbeIsAnsweredWithAColumnTheTerminalThenWrapsAt()
        {
            var vt = Vt100State.ForRouterOs();

            List<string> replies = vt.Process(WidthProbe);

            Assert.AreEqual(3, replies.Count, "one cursor report per ESC[6n");
            Assert.AreEqual("\x1B[1;" + Vt100State.RouterOsWidth + "R", replies[0],
                "the cursor-forward probe must saturate at the advertised width");
            // The space after the right margin is the whole question RouterOS is asking: a terminal that
            // answers with the next column has said it does not wrap, and RouterOS then wraps the output
            // itself, into the data. Row 2 column 1 is the answer that leaves the byte stream alone.
            Assert.AreEqual("\x1B[2;1R", replies[1], "the terminal must demonstrate that it wraps");
            Assert.AreEqual("\x1B[2;2R", replies[2]);
        }

        [TestMethod]
        public void AWidthTheProbeCannotReachNeverDemonstratesTheWrap()
        {
            // The defect this pins: 65535 was advertised on the WinBox mepty and MAC-Telnet terminals.
            // ESC[9999C from column 1 cannot reach it, so the column just keeps climbing, RouterOS reads
            // "this terminal does not wrap", and hard-wraps its output at 10 000 characters — mid-token,
            // which the as-value parser then joins onto the previous field as a multi-value continuation.
            var vt = new Vt100State(65535, 25);

            List<string> replies = vt.Process(WidthProbe);

            Assert.AreEqual("\x1B[1;10000R", replies[0]);
            Assert.AreEqual("\x1B[1;10001R", replies[1], "no wrap is ever shown, whatever the width says");
            Assert.AreEqual("\x1B[1;10002R", replies[2]);
        }

        [TestMethod]
        public void TheAdvertisedWidthIsReachableByTheCursorForwardProbe()
        {
            Assert.IsTrue(Vt100State.RouterOsWidth <= 1 + 9999,
                "ESC[9999C starts at column 1, so a wider terminal can never saturate and the wrap is "
                + "never demonstrated — see " + nameof(AWidthTheProbeCannotReachNeverDemonstratesTheWrap));
        }
    }
}
