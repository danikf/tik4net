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
