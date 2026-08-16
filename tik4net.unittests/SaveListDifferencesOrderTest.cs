using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Ip.Firewall;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// B6: <c>SaveListDifferences</c> applies the ORDER of the modified list on an <c>IsOrdered</c> entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until this landed the method carried a <c>//TODO support for order!</c> and did nothing about
    /// position, so a caller who reordered a mangle chain and saved got every field written correctly and
    /// the chain left as it was — the failure mode that matters, because a rule in the wrong position does
    /// the wrong thing while reading correct.
    /// </para>
    /// <para>
    /// Every test here applies the save against <see cref="FakeRouterTable{TEntity}"/> and asserts the
    /// router's resulting ROW ORDER rather than the commands emitted: a move sequence can be plausible and
    /// still land the rows somewhere neither list asked for, which is exactly how the ordering bug behind
    /// <c>TikOrderTracker</c> survived (3 of the 7 moves applied, an order matching neither input).
    /// </para>
    /// </remarks>
    [TestClass]
    public class SaveListDifferencesOrderTest
    {
        private static FirewallMangle Rule(string comment) => new FirewallMangle
        {
            Chain = "prerouting",
            Action = FirewallMangle.ActionType.Accept,
            Comment = comment,
            Passthrough = true,
        };

        private static string[] Comments(IEnumerable<FirewallMangle> rules)
            => rules.Select(r => r.Comment).ToArray();

        /// <summary>Seeds a table with rules commented A, B, C… and returns the connection plus the loaded list.</summary>
        private static (TikFakeConnection Connection, FakeRouterTable<FirewallMangle> Table, List<FirewallMangle> Loaded)
            Seeded(params string[] comments)
        {
            var table = new FakeRouterTable<FirewallMangle>().Seed(comments.Select(Rule).ToArray());
            var connection = table.AttachTo(new TikFakeConnection());
            return (connection, table, connection.LoadAll<FirewallMangle>().ToList());
        }

        [TestMethod]
        public void ReorderingTheListReordersTheRouter()
        {
            var (connection, table, loaded) = Seeded("A", "B", "C");
            var backup = loaded.CloneEntityList().ToList();

            var reordered = new List<FirewallMangle> { loaded[2], loaded[0], loaded[1] };   // C, A, B
            connection.SaveListDifferences(reordered, backup);

            CollectionAssert.AreEqual(new[] { "C", "A", "B" }, Comments(table.Load(connection)).ToArray());
        }

        [TestMethod]
        public void AReversalMovesEveryRowThatNeedsIt()
        {
            // The case that caught the original ordering defect: a full reversal needs a move for nearly
            // every row, and a check against the STARTING indexes skips the ones whose neighbours have
            // already shifted.
            var (connection, table, loaded) = Seeded("A", "B", "C", "D", "E");
            var backup = loaded.CloneEntityList().ToList();

            var reversed = Enumerable.Reverse(loaded).ToList();
            connection.SaveListDifferences(reversed, backup);

            CollectionAssert.AreEqual(new[] { "E", "D", "C", "B", "A" }, Comments(table.Load(connection)).ToArray());
        }

        [TestMethod]
        public void ANewRuleLandsWhereTheListPutItRatherThanAtTheEnd()
        {
            // The router APPENDS a created row, so a new rule in the middle of the list owes its position
            // entirely to the follow-up move.
            var (connection, table, loaded) = Seeded("A", "C");
            var backup = loaded.CloneEntityList().ToList();

            var modified = new List<FirewallMangle> { loaded[0], Rule("B"), loaded[1] };
            connection.SaveListDifferences(modified, backup);

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Comments(table.Load(connection)).ToArray());
        }

        [TestMethod]
        public void SeveralNewRulesKeepTheirRelativeOrder()
        {
            // Several entities with no .id at once — the case that rules out simply folding this method into
            // TikListMerge, whose key extractor would map every one of them to the same empty key.
            var (connection, table, loaded) = Seeded("A");
            var backup = loaded.CloneEntityList().ToList();

            var modified = new List<FirewallMangle> { Rule("X"), Rule("Y"), loaded[0], Rule("Z") };
            connection.SaveListDifferences(modified, backup);

            CollectionAssert.AreEqual(new[] { "X", "Y", "A", "Z" }, Comments(table.Load(connection)).ToArray());
        }

        [TestMethod]
        public void DeleteInsertUpdateAndReorderInOneSave()
        {
            var (connection, table, loaded) = Seeded("A", "B", "C", "D");
            var backup = loaded.CloneEntityList().ToList();

            loaded[0].Comment = "A-renamed";                                       // update
            var modified = new List<FirewallMangle> { loaded[3], Rule("N"), loaded[0], loaded[1] };  // D, N, A, B — C deleted

            connection.SaveListDifferences(modified, backup);

            CollectionAssert.AreEqual(new[] { "D", "N", "A-renamed", "B" }, Comments(table.Load(connection)).ToArray());
        }

        [TestMethod]
        public void AnUnchangedOrderIssuesNoMoves()
        {
            var (connection, table, loaded) = Seeded("A", "B", "C");
            var backup = loaded.CloneEntityList().ToList();

            connection.SaveListDifferences(loaded, backup);

            Assert.AreEqual(0, table.AppliedCommands.Count(c => c.Contains("/move")),
                "nothing changed, so nothing should be written — a move per row would churn the router on every save");
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Comments(table.Load(connection)).ToArray());
        }

        [TestMethod]
        public void AnUnorderedEntityIsNotMoved()
        {
            // /ip/address is not IsOrdered, and /move is not a legal command there — the list order has to be
            // ignored rather than acted on.
            var table = new FakeRouterTable<IpAddress>().Seed(
                new IpAddress { Address = "10.0.0.1/24", Interface = "ether1" },
                new IpAddress { Address = "10.0.1.1/24", Interface = "ether2" });
            var connection = table.AttachTo(new TikFakeConnection());

            var loaded = connection.LoadAll<IpAddress>().ToList();
            var backup = loaded.CloneEntityList().ToList();

            connection.SaveListDifferences(Enumerable.Reverse(loaded).ToList(), backup);

            Assert.AreEqual(0, table.AppliedCommands.Count(c => c.Contains("/move")));
        }
    }
}
