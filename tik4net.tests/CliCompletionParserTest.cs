// CliCompletionParserTest.cs — router-free unit tests for CliCompletionParser, the parser behind
// ITikCliCompletion / the mikrotik_cli_complete MCP tool. The inputs below are the ANSI-stripped shape a
// RouterOS PTY returns for a <stem><Tab> probe (verified live, ROS 7.x): a block of space-padded completion
// columns, then a prompt redraw echoing the typed stem. The parser must drop the echo + prompt and split the
// columns into tokens — including tokens that share a name with a path segment (e.g. 'interface').

using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.tests
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
    }
}
