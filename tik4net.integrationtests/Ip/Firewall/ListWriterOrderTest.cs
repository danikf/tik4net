using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Live-router coverage of the list writers' ordering pass: the fewest moves, a new rule created in place with
    /// <c>place-before</c> (on WinBox native: the M2 add carrying the anchor as its next-id), rules outside the list
    /// left where they are, dynamic rules left alone, and an interrupted save that a re-run finishes.
    /// </summary>
    /// <remarks>
    /// Rules live in a private mangle chain named after a per-run stamp — nothing jumps into them, so they never
    /// see traffic — and are read back filtered by chain, a single equality the router answers (the lab mangle
    /// table holds ~1700 unrelated rules). Teardown sweeps by chain, because the writers create rules themselves.
    /// </remarks>
    [TestClass]
    public class ListWriterOrderTest : TestBase
    {
        private string _chain;

        protected override void OnInitialize()
        {
            string stamp = "T4L" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            _chain = stamp;
        }

        /// <summary>
        /// Sweeps this run's chain, and sweeps it again over a fresh connection when the test's own one cannot: a
        /// test that failed on a receive timeout leaves its terminal out of step, and a sweep over that connection
        /// fails quietly and leaves the chain behind for the next run.
        /// </summary>
        protected override void OnCleanup()
        {
            try { Sweep(Connection); }
            catch
            {
                try
                {
                    using (var fresh = OpenSecondaryConnection())
                        Sweep(fresh);
                }
                catch { /* best effort — never mask the test's outcome */ }
            }
        }

        private void Sweep(ITikConnection connection)
        {
            foreach (var rule in LoadChain(connection, _chain))
                connection.Delete(rule);
            foreach (var rule in connection.LoadList<FirewallFilter>(
                         connection.CreateParameter("chain", _chain, TikCommandParameterFormat.Filter)))
                connection.Delete(rule);
        }

        private static List<FirewallMangle> LoadChain(ITikConnection connection, string chain)
            => connection.LoadList<FirewallMangle>(connection.CreateParameter("chain", chain, TikCommandParameterFormat.Filter)).ToList();

        private List<FirewallMangle> LoadChain(string chain) => LoadChain(Connection, chain);

        private List<FirewallFilter> LoadOwnFilterRules()
            => Connection.LoadList<FirewallFilter>(Connection.CreateParameter("chain", _chain, TikCommandParameterFormat.Filter)).ToList();

        private FirewallMangle Rule(string comment) => new FirewallMangle
        {
            Chain = _chain,
            Action = FirewallMangle.ActionType.Passthrough,
            Comment = comment,
        };

        private List<FirewallMangle> Seed(params string[] comments)
        {
            foreach (var comment in comments)
                Connection.Save(Rule(comment));
            return LoadChain(_chain);
        }

        private static string[] Comments(IEnumerable<FirewallMangle> rules) => rules.Select(r => r.Comment).ToArray();

        private static List<FirewallMangle> InOrder(IList<FirewallMangle> loaded, Func<string, FirewallMangle> create, params string[] comments)
            => comments.Select(c => loaded.FirstOrDefault(r => r.Comment == c) ?? create(c)).ToList();

        private TikListMerge<FirewallMangle> Merge(IEnumerable<FirewallMangle> expected, IEnumerable<FirewallMangle> original)
            => Connection.CreateMerge(expected, original)
                .WithKey(r => r.Comment)
                .Field(r => r.Chain)
                .JustForInsertField(r => r.Action)
                .JustForInsertField(r => r.Comment);

        [TestMethod]
        public void SendingOneRuleToTheEndIsTwoMoves()
        {
            var original = Seed("A", "B", "C", "D");
            int moves = 0;

            Merge(new[] { "B", "C", "D", "A" }.Select(c => Rule(c)).ToList(), original)
                .WithMoveLogCallback((rule, from, to) => moves++)
                .Save();

            CollectionAssert.AreEqual(new[] { "B", "C", "D", "A" }, Comments(LoadChain(_chain)));
            Assert.AreEqual(2, moves, "A in front of D, then D in front of A — the end of the list is never assumed");
        }

        [TestMethod]
        public void NewRulesAreCreatedInPlace()
        {
            // place-before, on every transport: the only command that puts B and D where they belong.
            var loaded = Seed("A", "C", "E");

            Connection.SaveListDifferences(InOrder(loaded, c => Rule(c), "A", "B", "C", "D", "E"), loaded.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E" }, Comments(LoadChain(_chain)));
        }

        [TestMethod]
        public void TheListsLastRuleStopsInFrontOfTheRuleThatFollowedTheList()
        {
            // X is on the router after the list but not part of it — the caller loaded the list filtered.
            var loaded = Seed("A", "B", "C", "X").Where(r => r.Comment != "X").ToList();

            Connection.SaveListDifferences(InOrder(loaded, c => Rule(c), "B", "C", "A", "N"), loaded.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "B", "C", "A", "N", "X" }, Comments(LoadChain(_chain)),
                "the list ends in front of X, where it ended before — not past it at the end of the table");
        }

        [TestMethod]
        public void ADynamicRuleIsNeitherDeletedNorMoved()
        {
            // Needs a dynamic rule in the filter table: the fasttrack counter rule exists while (and after) a
            // fasttrack rule does. The suite does not create one — it changes forwarding on a live router.
            var dynamicRules = Connection.LoadList<FirewallFilter>(
                Connection.CreateParameter("dynamic", "true", TikCommandParameterFormat.Filter)).ToList();
            if (dynamicRules.Count == 0)
                Assert.Inconclusive("no dynamic filter rule on the router (the fasttrack counter rule appears once a fasttrack rule exists)");

            foreach (var comment in new[] { "A", "B", "C" })
                Connection.Save(new FirewallFilter { Chain = _chain, Action = FirewallFilter.ActionType.Passthrough, Comment = comment });
            var own = LoadOwnFilterRules();
            var original = dynamicRules.Concat(own).ToList();

            // The dynamic rule is left out of the modified list, and the rest reversed.
            Connection.SaveListDifferences(Enumerable.Reverse(own).ToList(), original.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "C", "B", "A" }, LoadOwnFilterRules().Select(r => r.Comment).ToArray());
            Assert.AreEqual(dynamicRules.Count, Connection.LoadList<FirewallFilter>(
                Connection.CreateParameter("dynamic", "true", TikCommandParameterFormat.Filter)).Count(),
                "the dynamic rule must still be there — the router refuses to remove it, so the writer must not try");
        }

        [TestMethod]
        public void AnInterruptedSaveIsFinishedByRunningItAgain()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "async list save");
            var original = Seed("A", "B", "C", "D", "E");
            var desired = new[] { "E", "D", "C", "B", "A", "N" };

            using (var cts = new CancellationTokenSource())
            {
                int moves = 0;
                var merge = Merge(desired.Select(c => Rule(c)).ToList(), original)
                    .WithMoveLogCallback((rule, from, to) => { if (++moves == 2) cts.Cancel(); });
                try
                {
                    merge.SaveAsync(cts.Token).GetAwaiter().GetResult();
                    Assert.Fail("the save was cancelled after its second move");
                }
                catch (OperationCanceledException) { }
            }
            CollectionAssert.AreNotEqual(desired, Comments(LoadChain(_chain)), "the interrupted save left the chain half-done");

            Merge(desired.Select(c => Rule(c)).ToList(), LoadChain(_chain)).SaveAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(desired, Comments(LoadChain(_chain)));
        }

        [TestMethod]
        public void SaveListDifferencesAsyncAppliesTheOrder()
        {
            EnsureCapability(TikConnectionCapability.AsyncCommands, "async list save");
            var loaded = Seed("A", "B", "C", "D");
            var backup = loaded.CloneEntityList();

            Connection.SaveListDifferencesAsync(InOrder(loaded, c => Rule(c), "D", "N", "B", "A"), backup)
                .GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "D", "N", "B", "A" }, Comments(LoadChain(_chain)));
        }
    }
}
