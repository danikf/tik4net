// MenuSchemaTests.cs — DescribeMenu's two router-side sources, without a router.
//
// The inspect answers are the ones RouterOS 7.24.4 gave (POST /rest/console/inspect, 2026-09-28), shortened; the Tab
// listings are the shapes 6.49.13 gave, including the traps a single Tab hides (findings-cli §14).

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class MenuSchemaTests
    {
        // ── /console/inspect ──────────────────────────────────────────────────

        private static ITikSentence Row(params (string Name, string Value)[] words)
            => new TikFakeReSentence(words.ToDictionary(w => w.Name, w => w.Value));

        private static ITikSentence Node(string type, string name, string nodeType)
            => Row(("type", type), ("name", name), ("node-type", nodeType));

        private static ITikSentence Completion(string word, bool show = true)
            => Row(("type", "completion"), ("completion", word), ("show", show ? "true" : "false"));

        private static TikFakeConnection Inspect(Dictionary<string, ITikSentence[]> answers)
        {
            var router = new TikFakeConnection();
            foreach (var answer in answers)
            {
                string[] key = answer.Key.Split(' ');   // "child ip,route"
                router.WithResponse(
                    cmd => cmd.First() == "/console/inspect" && cmd.Contains("=request=" + key[0]) && cmd.Contains("=path=" + key[1]),
                    answer.Value.Concat(new ITikSentence[] { new TikFakeDoneSentence() }).ToArray());
            }
            return router.WithResponse(cmd => cmd.First() == "/console/inspect", new ITikSentence[] { new TikFakeDoneSentence() });
        }

        private static Dictionary<string, ITikSentence[]> Route() => new Dictionary<string, ITikSentence[]>
        {
            ["child ip,route"] = new[] { Node("self", "route", "dir"), Node("child", "add", "cmd"), Node("child", "set", "cmd"),
                Node("child", "get", "cmd"), Node("child", "print", "cmd"), Node("child", "nexthop", "dir") },
            ["child ip,route,add"] = new[] { Node("self", "add", "cmd"), Node("child", "dst-address", "arg"),
                Node("child", "routing-table", "arg"), Node("child", "copy-from", "arg") },
            ["child ip,route,set"] = new[] { Node("self", "set", "cmd"), Node("child", "numbers", "arg"),
                Node("child", "routing-table", "arg") },
            ["completion ip,route,get,value-name"] = new[] { Completion("[", show: false), Completion("active"), Completion("routing-table"),
                Completion("*", show: false) },
            ["completion ip,route,set,routing-table"] = new[] { Completion("main"), Completion("<value>", show: false) },
        };

        [TestMethod]
        public void Inspect_GivesTheVerbsArgumentsAndTheReadableFields()
        {
            var schema = ConsoleInspectSchemaReader.Read(Inspect(Route()), "/ip/route");

            Assert.AreEqual(TikMenuSchemaSource.ConsoleInspect, schema.Source);
            CollectionAssert.AreEquivalent(new[] { "add", "set", "get", "print" }, schema.Commands.ToArray(), "a sub-menu is not a command");
            CollectionAssert.AreEquivalent(new[] { "dst-address", "routing-table", "copy-from" }, schema.AddArguments!.ToArray());
            CollectionAssert.AreEquivalent(new[] { "numbers", "routing-table" }, schema.SetArguments!.ToArray());
            CollectionAssert.AreEquivalent(new[] { "active", "routing-table" }, schema.ReadableFields!.ToArray(),
                "the syntax helpers ('[', the id prefix '*') are hidden");
        }

        [TestMethod]
        public void Inspect_GivesTheSubmenusAnyCommandsArgumentsAndTheUnsetFields()
        {
            var answers = Route();
            answers["child ip,route"] = answers["child ip,route"].Concat(new[] { Node("child", "unset", "cmd"), Node("child", "move", "cmd"),
                Node("child", "check", "cmd") }).ToArray();
            answers["child ip,route,check"] = new[] { Node("self", "check", "cmd"), Node("child", "numbers", "arg") };
            answers["completion ip,route,unset,value-name"] = new[] { Completion("[", show: false), Completion("routing-table") };

            var schema = ConsoleInspectSchemaReader.Read(Inspect(answers), "/ip/route");

            CollectionAssert.AreEqual(new[] { "nexthop" }, schema.Submenus!.ToArray());
            CollectionAssert.AreEqual(new[] { "numbers" }, schema.Arguments("check")!.ToArray());
            Assert.IsNull(schema.Arguments("ping"), "a command the menu does not have");
            CollectionAssert.AreEqual(new[] { "routing-table" }, schema.UnsetFields!.ToArray());
            Assert.AreEqual(true, schema.IsOrdered);
        }

        [TestMethod]
        public void Inspect_ValuesAreTheShownCompletions_AskedOnce()
        {
            var router = Inspect(Route());
            var schema = ConsoleInspectSchemaReader.Read(router, "/ip/route");

            CollectionAssert.AreEqual(new[] { "main" }, schema.ValuesOf("routing-table").ToArray());
            Assert.AreSame(schema.ValuesOf("routing-table"), schema.ValuesOf("routing-table"));
        }

        [TestMethod]
        public void Inspect_ASingletonHasNoAdd()
        {
            var schema = ConsoleInspectSchemaReader.Read(Inspect(new Dictionary<string, ITikSentence[]>
            {
                ["child system,identity"] = new[] { Node("self", "identity", "dir"), Node("child", "set", "cmd"), Node("child", "get", "cmd") },
                ["child system,identity,set"] = new[] { Node("self", "set", "cmd"), Node("child", "name", "arg") },
            }), "/system/identity");

            Assert.IsNull(schema.AddArguments);
            CollectionAssert.AreEqual(new[] { "name" }, schema.SetArguments!.ToArray());
        }

        [TestMethod]
        public void Inspect_AMenuTheRouterDoesNotHave_IsNoSuchCommand()
        {
            // 7.24.4 answers [] for an unknown path as it does for a menu without 'add' — which is why the menu node
            // itself is asked first.
            var ex = Assert.ThrowsException<TikNoSuchCommandException>(
                () => ConsoleInspectSchemaReader.Read(Inspect(new Dictionary<string, ITikSentence[]>()), "/ip/nosuch"));
            StringAssert.Contains(ex.Message, "/ip/nosuch");
        }

        [TestMethod]
        public void ATransportThatCannotAsk_IsRefusedByCapability()
        {
            // The fake declares no MenuSchema: fail-closed, before anything is sent.
            Assert.ThrowsException<TikConnectionCapabilityNotSupportedException>(
                () => new TikFakeConnection().DescribeMenu("/ip/route"));
        }

        private static ITikSentence Syntax(string symbolType, string symbol, string text)
            => Row(("type", "syntax"), ("symbol", symbol), ("symbol-type", symbolType), ("nested", "1"), ("nonorm", "false"),
                ("text", text));

        [TestMethod]
        public void Inspect_DescribesTheWordsAndGivesAnArgumentsGrammar_AskedOnce()
        {
            // 7.24.5: 'syntax' on a menu and on a command explains each word (many with no text); on an argument it
            // defines the value, then what the definition is built of.
            var answers = Route();
            answers["syntax ip,route"] = new[] { Syntax("collection", "", ""), Syntax("explanation", "..", "go up to ip"),
                Syntax("explanation", "add", "Create a new item"), Syntax("explanation", "check", "") };
            answers["syntax ip,route,add"] = new[] { Syntax("explanation", "distance", "Administrative distance of the route"),
                Syntax("explanation", "gateway", "") };
            answers["syntax ip,route,add,distance"] = new[] { Syntax("definition", "Distance", "Num"),
                Syntax("definition", "Num", "1..255    (integer number)") };
            answers["syntax ip,route,add,routing-table"] = new[] { Syntax("definition", "", "") };
            var router = Inspect(answers);
            var schema = ConsoleInspectSchemaReader.Read(router, "/ip/route");

            Assert.AreEqual("Create a new item", schema.Description("add"));
            Assert.IsNull(schema.Description("check"), "a word without text has no description");
            Assert.IsNull(schema.Description(".."));
            Assert.AreEqual("Administrative distance of the route", schema.Description("distance", "add"));
            Assert.IsNull(schema.Description("gateway", "add"));
            CollectionAssert.AreEqual(new[] { "Distance ::= Num", "Num ::= 1..255    (integer number)" },
                schema.ValueGrammar("distance", "add").ToArray());
            Assert.AreEqual(0, schema.ValueGrammar("routing-table", "add").Count, "an enum answers one empty definition");

            int asked = router.SentCommands.Count;
            schema.Description("add");
            schema.ValueGrammar("distance", "add");
            Assert.AreEqual(asked, router.SentCommands.Count, "asked once and remembered");
        }

        // ── The help key (F1, RouterOS 6) ─────────────────────────────────────

        [TestMethod]
        public void HelpKey_AListingIsOneDescriptionPerWord()
        {
            // 6.49.13 '/ip address set ' F1: a one-line description of the command first, then 'name -- text' rows, the
            // positional arguments in angle brackets, and the prompt redrawn after it.
            const string help = "Change item properties\r\n"
                + "<numbers> -- List of item numbers\r\n"
                + "address -- Local IP address\r\n"
                + ".. -- go up to ip\r\n"
                + "comment -- Short description of the item\r\n"
                + "[admin@CHR2] > /ip address set ";

            var descriptions = CliHelpParser.Descriptions(help);

            Assert.AreEqual("List of item numbers", descriptions["numbers"]);
            Assert.AreEqual("Local IP address", descriptions["address"]);
            Assert.AreEqual("Short description of the item", descriptions["comment"]);
            Assert.IsFalse(descriptions.ContainsKey(".."));
            Assert.AreEqual(3, descriptions.Count, string.Join(", ", descriptions.Keys));
        }

        [TestMethod]
        public void HelpKey_AfterAnArgumentIsTheValueGrammar()
        {
            const string help = "Address ::= Address[/Netmask]\r\n"
                + "  Netmask ::= Num\r\n"
                + "    Num ::= 0..32    (integer number)\r\n"
                + "Chain ::= input | forward | output\r\n"
                + "[admin@CHR2] > /ip address add address=";

            CollectionAssert.AreEqual(new[] { "Address ::= Address[/Netmask]", "Netmask ::= Num", "Num ::= 0..32    (integer number)",
                "Chain ::= input | forward | output" }, CliHelpParser.Grammar(help).ToArray());
            Assert.AreEqual(0, CliHelpParser.Descriptions(help).Count);
        }

        /// <summary>A Tab fake that also presses F1.</summary>
        private sealed class ScriptedHelp : ITikCliCompletion, ICliHelpKey
        {
            private readonly ScriptedTab _tab;
            private readonly Dictionary<string, string> _help;
            public readonly List<string> Pressed = new List<string>();

            public ScriptedHelp(ScriptedTab tab, Dictionary<string, string> help) { _tab = tab; _help = help; }
            public IReadOnlyList<string> CompleteCli(string partialInput) => _tab.CompleteCli(partialInput);
            public string CompleteCliRaw(string partialInput) => _tab.CompleteCliRaw(partialInput);

            public string HelpCli(string partialInput)
            {
                Pressed.Add(partialInput);
                return _help.TryGetValue(partialInput, out var answer) ? answer : "";
            }
        }

        [TestMethod]
        public void Tab_TheDescriptionsAndTheGrammarComeFromTheHelpKey()
        {
            var cli = new ScriptedHelp(
                new ScriptedTab(new Dictionary<string, string[]> { ["/ip route "] = new[] { "add", "print", "set" } }),
                new Dictionary<string, string>
                {
                    ["/ip route "] = "add -- Create a new item\r\n",
                    ["/ip route add "] = "distance -- Administrative distance of the route\r\n",
                    ["/ip route add distance="] = "Distance ::= 1..255    (integer number)\r\n",
                });

            var schema = CliCompletionSchemaReader.Read(cli, "/ip/route");

            Assert.AreEqual("Create a new item", schema.Description("add"));
            Assert.AreEqual("Administrative distance of the route", schema.Description("distance", "add"));
            CollectionAssert.AreEqual(new[] { "Distance ::= 1..255    (integer number)" }, schema.ValueGrammar("distance", "add").ToArray());
            CollectionAssert.AreEqual(new[] { "/ip route ", "/ip route add ", "/ip route add distance=" }, cli.Pressed,
                "the menu path with spaces, as RouterOS 6 completes it");
        }

        [TestMethod]
        public void Tab_WithoutAHelpKeyTheRouterDescribesNothing()
        {
            var schema = CliCompletionSchemaReader.Read(
                new ScriptedTab(new Dictionary<string, string[]> { ["/ip route "] = new[] { "add" } }), "/ip/route");

            Assert.IsNull(schema.Description("add"));
            Assert.AreEqual(0, schema.ValueGrammar("distance", "add").Count);
        }

        // ── Tab completion (RouterOS 6) ───────────────────────────────────────

        /// <summary>Answers a Tab from a table: a listing (tokens), or an inline completion (the completed line).</summary>
        private sealed class ScriptedTab : ITikCliCompletion
        {
            private readonly Dictionary<string, string[]> _listings;
            private readonly Dictionary<string, string> _inline;
            public readonly List<string> Asked = new List<string>();

            public ScriptedTab(Dictionary<string, string[]> listings, Dictionary<string, string>? inline = null)
            {
                _listings = listings;
                _inline = inline ?? new Dictionary<string, string>();
            }

            public IReadOnlyList<string> CompleteCli(string partialInput)
            {
                Asked.Add(partialInput);
                return _listings.TryGetValue(partialInput, out var tokens) ? tokens : Array.Empty<string>();
            }

            public string CompleteCliRaw(string partialInput)
                => _inline.TryGetValue(partialInput, out var line) ? line : "";
        }

        [TestMethod]
        public void Tab_TheReadableFieldsAreAsked_EvenWhenTheMenuListsNoGet()
        {
            // 6.49.13: '/ip route ' lists add … unset and no get, and 'get value-name=' completes every field anyway.
            var tab = new ScriptedTab(new Dictionary<string, string[]>
            {
                ["/ip route "] = new[] { "nexthop", "add", "print", "set" },
                ["/ip route get value-name="] = new[] { "dst-address", "gateway-status", "routing-mark" },
            });

            var schema = CliCompletionSchemaReader.Read(tab, "/ip/route");

            CollectionAssert.AreEquivalent(new[] { "dst-address", "gateway-status", "routing-mark" }, schema.ReadableFields!.ToArray());
        }

        /// <summary>A colour terminal: each word carries what it is.</summary>
        private sealed class ColouredTab : ITikCliCompletion, ICliCompletionReaction
        {
            private readonly Dictionary<string, CliCompletionItem[]> _listings;
            public readonly List<string> Asked = new List<string>();

            public ColouredTab(Dictionary<string, CliCompletionItem[]> listings) => _listings = listings;

            public (IReadOnlyList<CliCompletionItem> Items, string Raw) CompleteCliBoth(string partialInput)
            {
                Asked.Add(partialInput);
                return (_listings.TryGetValue(partialInput, out var items) ? items : Array.Empty<CliCompletionItem>(), "");
            }

            public IReadOnlyList<string> CompleteCli(string partialInput) => CompleteCliBoth(partialInput).Items.Select(i => i.Name).ToList();
            public string CompleteCliRaw(string partialInput) => "";
        }

        private static CliCompletionItem Dir(string name) => new CliCompletionItem(name, CliCompletionKind.Submenu);
        private static CliCompletionItem Cmd(string name) => new CliCompletionItem(name, CliCompletionKind.Command);
        private static CliCompletionItem Arg(string name) => new CliCompletionItem(name, CliCompletionKind.Argument);

        [TestMethod]
        public void Tab_TheColourSaysWhatAWordIs_WithoutATabOnIt()
        {
            // 6.49.13 over a colour terminal: '/ip route ' draws the sub-menus cyan and the commands magenta; the second
            // Tab adds '..' and 'get'.
            var tab = new ColouredTab(new Dictionary<string, CliCompletionItem[]>
            {
                ["/ip route "] = new[] { Dir("nexthop"), Dir("rule"), Cmd("add"), Cmd("move"), Cmd("set"), Cmd("unset"), Dir(".."), Cmd("get") },
                ["/ip route unset value-name="] = new[] { Arg("routing-mark"), Arg("check-gateway") },
            });

            var schema = CliCompletionSchemaReader.Read(tab, "/ip/route");

            CollectionAssert.AreEquivalent(new[] { "nexthop", "rule" }, schema.Submenus!.ToArray(), "'..' is the parent, not a sub-menu");
            CollectionAssert.AreEquivalent(new[] { "add", "move", "set", "unset", "get" }, schema.Commands.ToArray());
            Assert.AreEqual(true, schema.IsOrdered);
            CollectionAssert.AreEquivalent(new[] { "routing-mark", "check-gateway" }, schema.UnsetFields!.ToArray());
            CollectionAssert.AreEqual(new[] { "/ip route ", "/ip route unset value-name=" }, tab.Asked);
        }

        [TestMethod]
        public void Tab_WithoutColour_AWordIsASubmenuWhenItsOwnListingHasTheParent()
        {
            var tab = new ScriptedTab(new Dictionary<string, string[]>
            {
                ["/tool "] = new[] { "netwatch", "ping", ".." },
                ["/tool netwatch "] = new[] { "add", "print", "..", "get" },
                ["/tool ping "] = new[] { "address", "count", "interface", "vrf" },
            });

            var schema = CliCompletionSchemaReader.Read(tab, "/tool");

            CollectionAssert.AreEqual(new[] { "netwatch" }, schema.Submenus!.ToArray());
            CollectionAssert.AreEqual(new[] { "ping" }, schema.Commands.ToArray());
            Assert.AreEqual(false, schema.IsOrdered);
            CollectionAssert.AreEquivalent(new[] { "address", "count", "interface", "vrf" }, schema.Arguments("ping")!.ToArray());
            Assert.IsNull(schema.Arguments("traceroute"));
        }

        [TestMethod]
        public void Tab_NothingCompletedAfterGet_MeansTheMenuCannotSay()
        {
            var tab = new ScriptedTab(new Dictionary<string, string[]> { ["/x "] = new[] { "print" } });

            Assert.IsNull(CliCompletionSchemaReader.Read(tab, "/x").ReadableFields);
        }

        [TestMethod]
        public void Tab_AStemThatIsItselfAWord_IsKept()
        {
            // 6.49.13 '/ip arp get value-name=' lists 'published...'; a Tab on 'published' accepts the word and moves on
            // to the next parameter ('number='), so nothing lists under it.
            var tab = new ScriptedTab(
                new Dictionary<string, string[]> { ["/ip arp get value-name="] = new[] { "address", "published..." } },
                new Dictionary<string, string> { ["/ip arp get value-name=published"] = "number=" });

            CollectionAssert.AreEquivalent(new[] { "address", "published" },
                CliCompletionSchemaReader.Walk(tab, "/ip arp get value-name=").ToArray());
        }

        [TestMethod]
        public void Tab_AnElidedStemIsExpanded()
        {
            // 6.49.13 '/ip firewall filter add ' lists 'connection-...' for seven arguments.
            var tab = new ScriptedTab(new Dictionary<string, string[]>
            {
                ["/ip firewall filter add "] = new[] { "action", "connection-...", "chain" },
                ["/ip firewall filter add connection-"] = new[] { "connection-mark", "connection-state" },
            });

            var names = CliCompletionSchemaReader.Walk(tab, "/ip firewall filter add ");

            CollectionAssert.AreEquivalent(new[] { "action", "connection-mark", "connection-state", "chain" }, names.ToArray());
        }

        [TestMethod]
        public void Tab_AListingIsOneTab_NotOnePerInitial()
        {
            // A listing of names is not cut (6.49.13: 103 fields in one Tab), and every Tab costs a settle window.
            var tab = new ScriptedTab(new Dictionary<string, string[]>
            {
                ["/ip route set "] = new[] { "comment", "disabled", "distance", "dst-address" },
            });

            var names = CliCompletionSchemaReader.Walk(tab, "/ip route set ");

            Assert.AreEqual(4, names.Count);
            CollectionAssert.AreEqual(new[] { "/ip route set " }, tab.Asked);
        }

        [TestMethod]
        public void Tab_ACommonPrefixCompletedInline_IsAskedAgain_AndAUniqueOneIsTheCandidate()
        {
            // 's' completes inline to 'src-' (the prefix all share), which then lists; 'c' completes to the one
            // candidate. Neither the prefix nor a partial word is a name.
            var tab = new ScriptedTab(
                new Dictionary<string, string[]> { ["/x add src-"] = new[] { "src-address", "src-port" } },
                new Dictionary<string, string>
                {
                    ["/x add s"] = "/x add src-",
                    ["/x add c"] = "/x add comment=",
                });

            CollectionAssert.AreEquivalent(new[] { "src-address", "src-port" }, CliCompletionSchemaReader.Walk(tab, "/x add s").ToArray());
            CollectionAssert.AreEqual(new[] { "comment" }, CliCompletionSchemaReader.Walk(tab, "/x add c").ToArray());
        }

        [TestMethod]
        public void Tab_ATokenWithoutTheTypedPrefix_HasWalkedIntoTheNextParameter()
        {
            var tab = new ScriptedTab(new Dictionary<string, string[]>
            {
                ["/x add m"] = new[] { "mode", "ap", "station" },   // the listing of mode='s values, not names
            });

            CollectionAssert.AreEqual(new[] { "mode" }, CliCompletionSchemaReader.Walk(tab, "/x add m").ToArray());
        }
    }
}
