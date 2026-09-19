// CliCompletionParserTest.cs — router-free unit tests for CliCompletionParser, the parser behind
// ITikCliCompletion / the mikrotik_cli_complete MCP tool. The inputs below are the ANSI-stripped shape a
// RouterOS PTY returns for a <stem><Tab> probe (verified live, ROS 7.x): a block of space-padded completion
// columns, then a prompt redraw echoing the typed stem. The parser must drop the echo + prompt and split the
// columns into tokens — including tokens that share a name with a path segment (e.g. 'interface').

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests
{
    [TestClass]
    public class CliCompletionParserTest
    {
        // The 'add ' completion of /interface/vlan: the listing columns, then the prompt redraw with the stem.
        private const string VlanAddReaction =
            "arp             interface                      mvrp\r\n" +
            "arp-timeout     loop-protect                   name\r\n" +
            "comment         loop-protect-disable-time      use-service-tag\r\n" +
            "copy-from       loop-protect-send-interval     vlan-id\r\n" +
            "disabled        mtu\r\n" +
            "[admin@MikroTik] > /interface/vlan add ";

        [TestMethod]
        public void Tokens_AddCompletion_ReturnsSettableParameters()
        {
            var tokens = CliCompletionParser.Tokens(VlanAddReaction, "/interface/vlan add ");

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "arp", "arp-timeout", "comment", "copy-from", "disabled",
                    "interface", "loop-protect", "loop-protect-disable-time",
                    "loop-protect-send-interval", "mtu", "mvrp", "name",
                    "use-service-tag", "vlan-id",
                },
                System.Linq.Enumerable.ToArray(tokens));
        }

        [TestMethod]
        public void Tokens_ParameterSharingPathSegmentName_IsKept()
        {
            // 'interface' is both a path segment of the stem AND a settable parameter — it must survive.
            var tokens = CliCompletionParser.Tokens(VlanAddReaction, "/interface/vlan add ");
            CollectionAssert.Contains(System.Linq.Enumerable.ToArray(tokens), "interface");
        }

        [TestMethod]
        public void Clean_DropsPromptRedrawAndStemEcho()
        {
            // A leading bare echo of the typed stem (as RouterOS echoes keystrokes) must also be dropped.
            string reaction = "/interface vlan add \r\narp     mtu     name\r\n[admin@MikroTik] > /interface vlan add ";
            string cleaned = CliCompletionParser.Clean(reaction, "/interface vlan add ");

            Assert.IsFalse(cleaned.Contains("]"), "prompt redraw line should be removed");
            Assert.IsFalse(cleaned.Contains("MikroTik"), "prompt redraw line should be removed");
            CollectionAssert.AreEquivalent(
                new[] { "arp", "mtu", "name" },
                System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(reaction, "/interface vlan add ")));
        }

        [TestMethod]
        public void Tokens_Deduplicates()
        {
            string reaction = "print   set   print\r\n[admin@MikroTik] > /ip ";
            var tokens = System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(reaction, "/ip "));
            Assert.AreEqual(2, tokens.Length);
        }

        [TestMethod]
        public void Tokens_EmptyOrPromptOnly_ReturnsEmpty()
        {
            // A unique inline completion / nothing to list → only the prompt redraw, no tokens.
            var tokens = CliCompletionParser.Tokens("[admin@MikroTik] > /interface/vlan ", "/interface/vlan ");
            Assert.AreEqual(0, tokens.Count);

            Assert.AreEqual(0, CliCompletionParser.Tokens("", "/ip ").Count);
            Assert.AreEqual(0, CliCompletionParser.Tokens(null, "/ip ").Count);
        }

        /// <summary>
        /// RouterOS 7.24.4 (wire trace, Telnet, <c>/</c> + Tab) prints the listing on the SAME line as the echo of
        /// what was typed, with no break between them: <c>/lora     ping     app …</c>. The echo is ours, not part of
        /// the first completion — <c>lora</c> is.
        /// </summary>
        [TestMethod]
        public void Tokens_ListingGluedToTheEcho_DropsTheEchoNotTheFirstToken()
        {
            string reaction = "/lora     ping     app     caps-man     system     undo   \r\n\r[admin@CHR] > /";

            var tokens = System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(reaction, "/"));

            CollectionAssert.AreEqual(new[] { "lora", "ping", "app", "caps-man", "system", "undo" }, tokens);
            Assert.AreEqual("lora     ping     app     caps-man     system     undo",
                CliCompletionParser.Clean(reaction, "/"));
        }

        /// <summary>
        /// The same glue after a longer input: the echo is the whole typed line, and only it is dropped.
        /// </summary>
        [TestMethod]
        public void Tokens_ListingGluedToALongerEcho_DropsExactlyTheEcho()
        {
            string reaction = "/ip firewall filter add action     chain     comment\r\n[admin@CHR] > /ip firewall filter add ";

            var tokens = System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(reaction, "/ip firewall filter add "));

            CollectionAssert.AreEqual(new[] { "action", "chain", "comment" }, tokens);
        }

        /// <summary>
        /// The same with the 7.24.4 menu listing, whose typed line ends in a space: <c>/interface 6to4 …</c>.
        /// </summary>
        [TestMethod]
        public void Tokens_MenuListingGluedToTheEcho_DropsTheEcho()
        {
            string reaction = "/interface 6to4     bonding     bridge     print     set   \r\n\r[admin@CHR] > /interface ";

            var tokens = System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(reaction, "/interface "));

            CollectionAssert.AreEqual(new[] { "6to4", "bonding", "bridge", "print", "set" }, tokens);
        }

        /// <summary>
        /// An inline completion also starts with the echo, but 7.24.4 rewrites the word in place with cursor moves
        /// (<c>/interface/vl ESC[2D␠␠ESC[2Dvlan/</c>), which the ANSI strip leaves as the echo followed by
        /// WHITESPACE. That is not a glued listing, and the echo is not removed from it.
        /// </summary>
        [TestMethod]
        public void Clean_InlineRewriteAfterTheEcho_IsNotTakenForAGluedListing()
        {
            Assert.AreEqual("/interface/vl  vlan/",
                CliCompletionParser.Clean("/interface/vl  vlan/\r\n\r[admin@CHR] > ", "/interface/vl"));
        }
    }
}
