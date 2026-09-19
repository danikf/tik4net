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

        // ── Inline completion: RouterOS rewrites the word in place and lists nothing ──────────────────
        //
        // The reactions below are the bytes measured 2026-09-19 on RouterOS 7.19.6 and 7.24.4 (identical on both
        // apart from where a listing starts), escapes included, exactly as the settle drivers hand them over. An
        // inline completion stays on the input line; a listing moves to new rows and redraws the prompt.

        private const string Esc = "\x1b";

        // The colour prompt WinBox CLI and MAC-Telnet draw ("[admin@CHR] > " with SGR codes around the parts).
        private const string ColourPrompt = "[" + Esc + "[m" + Esc + "[36madmin" + Esc + "[m@" + Esc + "[m" + Esc + "[32mCHR" + Esc + "[m] > ";

        /// <summary><c>/interface/vl</c> Tab: echo, cursor-left 2, two blanks, cursor-left 2, <c>vlan/</c>.</summary>
        [TestMethod]
        public void InlineCompletion_OfAMenu_YieldsNoTokens_AndTheCompletedLine()
        {
            string reaction = ("/interface/vl\x1b[2D  \x1b[2Dvlan/");

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, "/interface/vl").Count);
            Assert.AreEqual("/interface/vlan/", CliCompletionParser.Clean(reaction, "/interface/vl"));
        }

        /// <summary>A verb after a menu: the rewrite ends in a blank, the word is complete.</summary>
        [TestMethod]
        public void InlineCompletion_OfAVerb_YieldsNoTokens_AndTheCompletedLine()
        {
            string reaction = ("/interface/vlan pri\x1b[3D   \x1b[3Dprint ");

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, "/interface/vlan pri").Count);
            Assert.AreEqual("/interface/vlan print", CliCompletionParser.Clean(reaction, "/interface/vlan pri"));
        }

        /// <summary>A one-letter word is rewritten with backspace-blank-backspace, which the ANSI strip keeps.</summary>
        [TestMethod]
        public void InlineCompletion_OfAOneLetterWord_UsesBackspaces_AndIsRecognised()
        {
            const string typed = "/interface/wireless/security-profiles add mode=d";
            string reaction = (typed + "\b \bdynamic-keys ");

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, typed).Count);
            Assert.AreEqual("/interface/wireless/security-profiles add mode=dynamic-keys",
                CliCompletionParser.Clean(reaction, typed));
        }

        /// <summary>
        /// Every value shares a prefix and nothing of the word was typed: RouterOS appends the prefix straight after
        /// the echo. That looks like the first row of a 7.24.4 glued listing of one token — what tells them apart is
        /// that a listing goes on to new rows and this stays on the input line.
        /// </summary>
        [TestMethod]
        public void InlineCompletion_OfASharedPrefix_AfterAnEmptyWord_IsNotAGluedListing()
        {
            const string typed = "/interface/bridge add frame-types=";
            string reaction = (typed + "admit-");

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, typed).Count);
            Assert.AreEqual("/interface/bridge add frame-types=admit-", CliCompletionParser.Clean(reaction, typed));
        }

        /// <summary>Nothing to complete: 7.24.4 echoes a trailing blank, 7.19.6 nothing. Either way, no answer.</summary>
        [TestMethod]
        public void NoCompletion_YieldsNoTokens_AndAnEmptyAnswer()
        {
            const string typed = "/interface/vlan add nosuchfield";
            foreach (string reaction in new[] { (typed + " "), (typed) })
            {
                Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, typed).Count);
                Assert.AreEqual("", CliCompletionParser.Clean(reaction, typed));
            }
        }

        /// <summary>
        /// WinBox CLI / MAC-Telnet: the same rewrite, then a colour repaint of the whole line from the prompt after
        /// a carriage return. Stripped, the two ran together into <c>/interface/vl  vlan/</c>.
        /// </summary>
        [TestMethod]
        public void InlineCompletion_RepaintedFromThePrompt_IsTheCompletedLine()
        {
            string reaction = "/interface/vl" + Esc + "[2D  " + Esc + "[2Dvlan/"
                              + "\r" + ColourPrompt + Esc + "[m" + Esc + "[36m/interface/vlan/";

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, "/interface/vl").Count);
            Assert.AreEqual("/interface/vlan/", CliCompletionParser.Clean(reaction, "/interface/vl"));
        }

        /// <summary>
        /// The other repaint: back over the whole word and write it again, coloured. Stripped, that appended a
        /// second copy — <c>/ip/firewall//ip/firewall/</c>.
        /// </summary>
        [TestMethod]
        public void InlineCompletion_RepaintedOverTheWord_IsTheCompletedLine()
        {
            string reaction = "/ip/fire" + Esc + "[4D    " + Esc + "[4Dfirewall/" + Esc + "[13D" + Esc + "[m" + Esc + "[36m/ip/firewall/";

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, "/ip/fire").Count);
            Assert.AreEqual("/ip/firewall/", CliCompletionParser.Clean(reaction, "/ip/fire"));
        }

        /// <summary>A shared prefix after an empty word, with the colour repaint that highlights the value.</summary>
        [TestMethod]
        public void InlineCompletion_OfASharedPrefix_RepaintedInColour()
        {
            const string typed = "/interface/bridge add frame-types=";
            string reaction = typed + "admit-" + "\r" + ColourPrompt
                              + Esc + "[m" + Esc + "[36m/interface/bridge" + Esc + "[m " + Esc + "[m" + Esc + "[35madd" + Esc + "[m "
                              + Esc + "[m" + Esc + "[32mframe-types" + Esc + "[m" + Esc + "[33m=" + Esc + "[37;41;1ma" + Esc + "[mdmit-";

            Assert.AreEqual(0, CliCompletionParser.Tokens(reaction, typed).Count);
            Assert.AreEqual("/interface/bridge add frame-types=admit-", CliCompletionParser.Clean(reaction, typed));
        }

        /// <summary>A colour listing: coloured tokens, the prompt redraw, then the repaint of the typed word.</summary>
        [TestMethod]
        public void ColourListing_IsStillAListing()
        {
            const string typed = "/interface/v";
            string reaction = typed + "\r\n" + Esc + "[m" + Esc + "[36mveth" + Esc + "[m     " + Esc + "[m" + Esc + "[36mvlan" + Esc + "[m   \r\n\r"
                              + Esc + "[9999B" + ColourPrompt + typed + Esc + "[K"
                              + Esc + "[12D" + Esc + "[m" + Esc + "[36m/interface/" + Esc + "[m" + Esc + "[31mv";

            CollectionAssert.AreEqual(new[] { "veth", "vlan" }, System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(reaction, typed)));
        }

        /// <summary>A listing after a partial word, in both versions' shapes, still lists.</summary>
        [TestMethod]
        public void ListingAfterAPartialWord_IsStillAListing_OnBothVersions()
        {
            const string typed = "/interface/vlan add m";
            string glued = (typed + "mtu     mvrp   \r\n\r\x1b[9999B[admin@CHR] > " + typed);
            string broken = (typed + "\r\nmtu     mvrp   \r\n\r\x1b[9999B[admin@CHR2] > " + typed);

            CollectionAssert.AreEqual(new[] { "mtu", "mvrp" }, System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(glued, typed)));
            CollectionAssert.AreEqual(new[] { "mtu", "mvrp" }, System.Linq.Enumerable.ToArray(CliCompletionParser.Tokens(broken, typed)));
        }
    }
}
