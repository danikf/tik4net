using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Testing;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// Covers the CLI error detection that P2.12 was about: RouterOS has no structural error channel over a
    /// terminal — output and errors are the same text — so a phrase list can never be complete, and every
    /// phrase it misses used to be reported to the caller as <b>success</b>.
    ///
    /// The messages below are verbatim from a live RouterOS 7.23.2 over Telnet.
    /// </summary>
    [TestClass]
    public class CliErrorParserTests
    {
        // A dummy command is enough — the parser only carries it into the exception it constructs.
        private static ITikCommand Cmd() => new TikFakeConnection().CreateCommand("/interface/set");

        [TestMethod]
        public void SilentOnSuccess_TreatsAnyLeftoverTextAsAnError()
        {
            // Neither of these matches any phrase the parser knows, which is exactly why they were silently
            // swallowed: the caller was told a rejected write had succeeded.
            foreach (string routerSaid in new[]
            {
                "value of mtu contains invalid trailing characters",
                "input does not match any value of interface",
                "invalid value of mac, mac address required",
                "action cancelled",
            })
            {
                Assert.ThrowsException<TikCommandTrapException>(
                    () => CliErrorParser.ThrowIfError(routerSaid, Cmd(), silentOnSuccess: true),
                    $"'{routerSaid}' must be reported as a failure, not as success");
            }
        }

        [TestMethod]
        public void SilentOnSuccess_AcceptsTheEmptyOutputOfASuccessfulWrite()
        {
            // What a successful set/remove/enable/disable/move actually returns once CliOutputHelper has
            // stripped the echo and the prompt: nothing.
            CliErrorParser.ThrowIfError("", Cmd(), silentOnSuccess: true);
            CliErrorParser.ThrowIfError("   \n \n", Cmd(), silentOnSuccess: true);
        }

        [TestMethod]
        public void SilentOnSuccess_StillReportsTheSpecificTrapKind()
        {
            // Positional detection runs last, so a classifiable error keeps its precise exception type
            // rather than collapsing into the generic trap.
            Assert.ThrowsException<TikNoSuchItemException>(
                () => CliErrorParser.ThrowIfError("no such item", Cmd(), silentOnSuccess: true));
        }

        [TestMethod]
        public void WithoutSilentOnSuccess_OutputIsLeftAlone()
        {
            // The read/monitor paths produce output by design, so they must keep phrase matching only —
            // a print returning rows is not an error.
            CliErrorParser.ThrowIfError(".id=*1;name=ether1", Cmd());
            CliErrorParser.ThrowIfError("value of mtu contains invalid trailing characters", Cmd());
        }

        [TestMethod]
        public void SilentOnSuccessVerbs_CoverTheWritesAndExcludeThePrinters()
        {
            foreach (string verb in new[] { "set", "remove", "enable", "disable", "move", "unset" })
                Assert.IsTrue(CliErrorParser.IsSilentOnSuccessVerb(verb), verb + " is silent when it succeeds");

            // 'add' prints the new .id, 'run' prints whatever the script printed, and print/monitor verbs
            // exist to produce output — treating their output as an error would break every one of them.
            foreach (string verb in new[] { "add", "run", "print", "getall", "monitor-traffic", "ping" })
                Assert.IsFalse(CliErrorParser.IsSilentOnSuccessVerb(verb), verb + " legitimately prints output");
        }
    }
}
