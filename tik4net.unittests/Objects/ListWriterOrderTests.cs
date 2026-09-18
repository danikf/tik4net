using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The two list writers — <see cref="TikListMerge{TEntity}"/> and <c>SaveListDifferences</c>, sync and async —
    /// over the shared ordering planner: fewest moves, creates in place, dynamic rows left alone, rows outside the
    /// list left where they are, and a failure or cancellation part-way that a re-run recovers from.
    /// </summary>
    /// <remarks>
    /// Asserted against <see cref="FakeRouterTable{TEntity}"/>'s resulting ROW ORDER and the commands it applied,
    /// never against the commands alone. The table refuses what RouterOS refuses on a dynamic row
    /// ("cannot move/remove/change builtin"), measured on 7.24.
    /// </remarks>
    [TestClass]
    public class ListWriterOrderTests
    {
        private static FirewallMangle Rule(string comment, string chain = "prerouting") => new FirewallMangle
        {
            Chain = chain,
            Action = FirewallMangle.ActionType.Accept,
            Comment = comment,
            Passthrough = true,
        };

        private static string[] Comments(IEnumerable<FirewallMangle> rules) => rules.Select(r => r.Comment).ToArray();

        private static (TikFakeConnection Connection, FakeRouterTable<FirewallMangle> Table) Table(params string[] comments)
        {
            var table = new FakeRouterTable<FirewallMangle>().Seed(comments.Select(c => Rule(c)).ToArray());
            return (table.AttachTo(new TikFakeConnection()), table);
        }

        private static int Count(FakeRouterTable<FirewallMangle> table, string verb)
            => table.AppliedCommands.Count(c => c.StartsWith(table.Path + "/" + verb));

        private static List<FirewallMangle> InOrder(IList<FirewallMangle> loaded, params string[] comments)
            => comments.Select(c => loaded.FirstOrDefault(r => r.Comment == c) ?? Rule(c)).ToList();

        // ── SaveListDifferences ───────────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void SendingOneRuleToTheEndIsTwoMoves()
        {
            var (connection, table) = Table("A", "B", "C", "D");
            var loaded = connection.LoadAll<FirewallMangle>().ToList();

            connection.SaveListDifferences(InOrder(loaded, "B", "C", "D", "A"), loaded.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "B", "C", "D", "A" }, Comments(table.Load(connection)));
            Assert.AreEqual(2, Count(table, "move"),
                "A in front of D, D in front of A — anchoring everything on the last rule made it three");
        }

        [TestMethod]
        public void BringingARuleToTheFrontIsOneMove()
        {
            var (connection, table) = Table("A", "B", "C", "D");
            var loaded = connection.LoadAll<FirewallMangle>().ToList();

            connection.SaveListDifferences(InOrder(loaded, "D", "A", "B", "C"), loaded.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "D", "A", "B", "C" }, Comments(table.Load(connection)));
            Assert.AreEqual(1, Count(table, "move"));
        }

        [TestMethod]
        public void ANewRuleIsCreatedInPlaceWithoutAMove()
        {
            var (connection, table) = Table("A", "C");
            var loaded = connection.LoadAll<FirewallMangle>().ToList();

            connection.SaveListDifferences(InOrder(loaded, "A", "B", "C"), loaded.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Comments(table.Load(connection)));
            Assert.AreEqual(0, Count(table, "move"));
            Assert.IsTrue(table.AppliedCommands.Single(c => c.StartsWith(table.Path + "/add")).Contains("=place-before="));
        }

        [TestMethod]
        public void ANewRuleOfAnEmptyListIsAppended()
        {
            var (connection, table) = Table();

            connection.SaveListDifferences(new List<FirewallMangle> { Rule("Z") }, new List<FirewallMangle>());

            CollectionAssert.AreEqual(new[] { "Z" }, Comments(table.Load(connection)));
            Assert.IsFalse(table.AppliedCommands.Single(c => c.StartsWith(table.Path + "/add")).Contains("place-before"),
                "no rule of the list exists to anchor on, so the router's append is the only place there is");
        }

        [TestMethod]
        public void ANewLastRuleStaysInFrontOfTheRuleThatFollowedTheList()
        {
            var table = new FakeRouterTable<FirewallMangle>().Seed(Rule("A"), Rule("X", "forward"));
            var connection = table.AttachTo(new TikFakeConnection());
            var chain = connection.LoadAll<FirewallMangle>().Where(r => r.Chain == "prerouting").ToList();

            connection.SaveListDifferences(InOrder(chain, "A", "Z"), chain.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "A", "Z", "X" }, Comments(table.Load(connection)));
        }

        [TestMethod]
        public void RulesOutsideTheListKeepTheirPlaceAndTheLastRuleStopsInFrontOfThem()
        {
            // The list is one chain; the table holds another chain's rule after it.
            var table = new FakeRouterTable<FirewallMangle>().Seed(Rule("A"), Rule("B"), Rule("C"), Rule("X", "forward"));
            var connection = table.AttachTo(new TikFakeConnection());
            var chain = connection.LoadAll<FirewallMangle>().Where(r => r.Chain == "prerouting").ToList();

            connection.SaveListDifferences(InOrder(chain, "B", "C", "A"), chain.CloneEntityList());

            CollectionAssert.AreEqual(new[] { "B", "C", "A", "X" }, Comments(table.Load(connection)),
                "A goes to the end of the LIST, in front of the rule that followed it — not past it to the end of the table");
        }

        [TestMethod]
        public void ADynamicRuleIsNeitherDeletedNorMoved()
        {
            var table = new FakeRouterTable<FirewallMangle>().Seed(Rule("A"));
            table.SeedDynamic(Rule("DYN"));
            table.Seed(Rule("B"));
            var connection = table.AttachTo(new TikFakeConnection());
            var loaded = connection.LoadAll<FirewallMangle>().ToList();

            // The caller leaves the dynamic rule out and reverses the rest.
            connection.SaveListDifferences(InOrder(loaded, "B", "A"), loaded.CloneEntityList());

            var after = Comments(table.Load(connection));
            CollectionAssert.Contains(after, "DYN", "a dynamic rule is not the caller's to delete — the router refuses it");
            CollectionAssert.AreEqual(new[] { "B", "A" }, after.Where(c => c != "DYN").ToArray());
        }

        [TestMethod]
        public void TheAsyncTwinSendsWhatTheSyncOneSends()
        {
            string[] Run(bool async)
            {
                var (connection, table) = Table("A", "B", "C", "D", "E");
                var loaded = connection.LoadAll<FirewallMangle>().ToList();
                var backup = loaded.CloneEntityList();
                loaded[1].Comment = "B2";
                var modified = new List<FirewallMangle> { loaded[4], Rule("N"), loaded[0], loaded[1] };  // E N A B2, C and D deleted
                if (async)
                    connection.SaveListDifferencesAsync(modified, backup).GetAwaiter().GetResult();
                else
                    connection.SaveListDifferences(modified, backup);
                CollectionAssert.AreEqual(new[] { "E", "N", "A", "B2" }, Comments(table.Load(connection)));
                return table.AppliedCommands.ToArray();
            }

            CollectionAssert.AreEqual(Run(false), Run(true));
        }

        [TestMethod]
        public void AFailurePartWayIsRecoveredByReloadingAndSavingAgain()
        {
            var (connection, table) = Table("A", "B", "C", "D", "E");
            var loaded = connection.LoadAll<FirewallMangle>().ToList();
            var desired = new[] { "E", "D", "C", "B", "A" };

            int writes = 0;
            table.BeforeWrite = cmd => ++writes == 2 ? "failure: simulated" : null;
            Assert.ThrowsException<TikCommandTrapException>(
                () => connection.SaveListDifferences(InOrder(loaded, desired), loaded.CloneEntityList()));

            table.BeforeWrite = null;
            var reloaded = connection.LoadAll<FirewallMangle>().ToList();
            connection.SaveListDifferences(InOrder(reloaded, desired), reloaded.CloneEntityList());

            CollectionAssert.AreEqual(desired, Comments(table.Load(connection)));
        }

        [TestMethod]
        public void CancellationStopsBeforeTheNextCommand()
        {
            var (connection, table) = Table("A", "B", "C", "D", "E");
            var loaded = connection.LoadAll<FirewallMangle>().ToList();
            using (var cts = new CancellationTokenSource())
            {
                int writes = 0;
                table.BeforeWrite = cmd => { if (++writes == 2) cts.Cancel(); return null; };

                Assert.ThrowsException<OperationCanceledException>(() => connection
                    .SaveListDifferencesAsync(InOrder(loaded, "E", "D", "C", "B", "A"), loaded.CloneEntityList(), cts.Token)
                    .GetAwaiter().GetResult());
                Assert.AreEqual(2, table.AppliedCommands.Count, "the command in flight completes; nothing after it is sent");
            }
        }

        /// <summary>An ordered menu that offers no <c>move</c> — the combination the next test needs.</summary>
        [TikEntity("/probe/ordered", IsOrdered = true,
            SupportedOperations = TikEntityOperations.Add | TikEntityOperations.Set | TikEntityOperations.Remove)]
        public class MovelessRule
        {
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            [TikProperty("comment")]
            public string Comment { get; set; }
        }

        [TestMethod]
        public void WithoutMoveEveryRowIsStillCreatedBeforeTheRefusal()
        {
            // A refusal of the ordering pass leaves every row existing with the right fields and only the order
            // wrong — the creates are not skipped because a move in between could not be sent.
            var table = new FakeRouterTable<MovelessRule>().Seed(
                new MovelessRule { Comment = "A" }, new MovelessRule { Comment = "B" });
            var connection = table.AttachTo(new TikFakeConnection());
            var loaded = connection.LoadAll<MovelessRule>().ToList();
            var modified = new List<MovelessRule> { loaded[1], new MovelessRule { Comment = "N" }, loaded[0] };

            Assert.ThrowsException<InvalidOperationException>(
                () => connection.SaveListDifferences(modified, loaded.CloneEntityList()));

            CollectionAssert.AreEquivalent(new[] { "A", "B", "N" }, table.Load(connection).Select(r => r.Comment).ToArray());
            Assert.AreEqual(0, table.AppliedCommands.Count(c => c.Contains("/move")));
        }

        // ── TikListMerge ──────────────────────────────────────────────────────────────────────────────────

        private static TikListMerge<FirewallMangle> Merge(ITikConnection connection, IEnumerable<FirewallMangle> expected,
            IEnumerable<FirewallMangle> original)
            => connection.CreateMerge(expected, original).WithKey(r => r.Comment).Field(r => r.Chain)
                .JustForInsertField(r => r.Comment).JustForInsertField(r => r.Action);

        [TestMethod]
        public void MergeSimulateReportsTheMovesSaveSends()
        {
            var (connection, table) = Table("A", "B", "C", "D");
            var original = connection.LoadAll<FirewallMangle>();
            var expected = new[] { "B", "C", "D", "A" }.Select(c => Rule(c)).ToList();

            Merge(connection, expected, original).Simulate(out int inserts, out int updates, out int deletes, out int moves);
            Merge(connection, expected, original).Save();

            Assert.AreEqual(2, moves, "A in front of D, D in front of A — anchoring on the last rule made it three");
            Assert.AreEqual(moves, Count(table, "move"));
            CollectionAssert.AreEqual(new[] { "B", "C", "D", "A" }, Comments(table.Load(connection)));
        }

        [TestMethod]
        public void MergeSaveAsyncSendsWhatSaveSends()
        {
            string[] Run(bool async)
            {
                var (connection, table) = Table("A", "B", "C", "D");
                var original = connection.LoadAll<FirewallMangle>();
                var expected = new[] { "D", "N", "A", "B" }.Select(c => Rule(c)).ToList();
                var merge = Merge(connection, expected, original);
                var result = async ? merge.SaveAsync().GetAwaiter().GetResult() : merge.Save();
                CollectionAssert.AreEqual(new[] { "D", "N", "A", "B" }, Comments(table.Load(connection)));
                Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Id)), "the result carries the router ids, new rows included");
                return table.AppliedCommands.ToArray();
            }

            CollectionAssert.AreEqual(Run(false), Run(true));
        }

        [TestMethod]
        public void APartialExpectationWithDeletesVetoedLeavesTheOtherRulesInPlace()
        {
            // The documented pattern for saving part of a list: expected names only some rules, and
            // WithOperationFilter keeps the merge from deleting the rest.
            var (connection, table) = Table("A", "B", "C");
            var original = connection.LoadAll<FirewallMangle>();
            var expected = new[] { "C", "A", "N" }.Select(c => Rule(c)).ToList();

            Merge(connection, expected, original)
                .WithOperationFilter((operation, oldEntity, newEntity) => operation != TikListMerge<FirewallMangle>.MergeOperation.Delete)
                .Save();

            var after = Comments(table.Load(connection));
            CollectionAssert.Contains(after, "B", "the delete was vetoed");
            CollectionAssert.AreEqual(new[] { "C", "A", "N" }, after.Where(c => c != "B").ToArray());
            Assert.AreEqual(0, Count(table, "remove"));
        }

        [TestMethod]
        public void MergeLeavesADynamicRuleAlone()
        {
            var table = new FakeRouterTable<FirewallMangle>().Seed(Rule("A"));
            table.SeedDynamic(Rule("DYN"));
            table.Seed(Rule("B"));
            var connection = table.AttachTo(new TikFakeConnection());
            var original = connection.LoadAll<FirewallMangle>();

            Merge(connection, new[] { "B", "A" }.Select(c => Rule(c)).ToList(), original).Save();

            var after = Comments(table.Load(connection));
            CollectionAssert.Contains(after, "DYN");
            CollectionAssert.AreEqual(new[] { "B", "A" }, after.Where(c => c != "DYN").ToArray());
        }
    }
}
