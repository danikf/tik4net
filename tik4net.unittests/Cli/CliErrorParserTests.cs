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

        /// <summary>
        /// The raw level is handed a finished CLI line instead of building one, so it used to have no verb
        /// and could only phrase-match. The verb is in the text, and these are the lines it may be read from.
        /// </summary>
        [TestMethod]
        public void RawSilentVerb_IsReadFromAPlainWriteCommand()
        {
            foreach (string line in new[]
            {
                "/interface set *2 comment=\"x\"",
                "/interface/set *2 comment=\"x\"",
                "/ip/firewall/filter remove numbers=0",
                "/interface enable ether1",
                "/interface disable ether1",
                "/ip firewall filter move 1 destination=0",
                "/interface unset *2 comment",
            })
            {
                Assert.IsTrue(CliErrorParser.TryGetRawSilentVerb(line, out string verb), line);
                Assert.IsTrue(CliErrorParser.IsSilentOnSuccessVerb(verb), line + " -> " + verb);
            }
        }

        /// <summary>
        /// The important half. A wrong verb turns legitimate output into a thrown exception, so anything the
        /// reader cannot vouch for must fall back to phrase matching rather than guess.
        /// </summary>
        [TestMethod]
        public void RawSilentVerb_RefusesEverythingItCannotVouchFor()
        {
            foreach (string line in new[]
            {
                ":put [/interface print as-value where comment=\"x\"]",   // scripting, and it prints
                "/interface print detail as-value where comment=\"x\"",   // print exists to produce output
                "/interface add name=vlan1",                              // add prints the new .id
                "/system/script/run myscript",                            // run prints the script's output
                "/interface set *2 comment=\"x\"; /interface print",       // chained: the second one prints
                ":global x [/interface find]",                            // a script variable
                "/interface set $id comment=\"x\"",                        // a script variable as a selector
                "",
                "   ",
            })
                Assert.IsFalse(CliErrorParser.TryGetRawSilentVerb(line, out _), line);
        }

        /// <summary>
        /// Scanning stops at the first argument, so a VALUE that happens to spell a verb is never read as
        /// one — the case that would otherwise make a legitimate answer throw.
        /// </summary>
        [TestMethod]
        public void RawSilentVerb_DoesNotReadAVerbOutOfAValue()
        {
            Assert.IsFalse(CliErrorParser.TryGetRawSilentVerb(
                "/interface print detail as-value where comment=\"set\"", out _));
            Assert.IsFalse(CliErrorParser.TryGetRawSilentVerb(
                "/log print where message=\"remove\"", out _));
        }
    }
}
