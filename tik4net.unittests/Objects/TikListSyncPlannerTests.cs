using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The ordering planner under the list writers, proved exhaustively rather than by example: every plan must
    /// land the rows in the desired order, and must do it with the fewest single-row moves there are.
    /// </summary>
    /// <remarks>
    /// The replay here is an independent model — a plain list with "move before" and "end of table" — not the
    /// <c>TikOrderTracker</c> the planner checks itself with, so a shared misunderstanding of the two cannot pass.
    /// Minimality is checked against a breadth-first search over single-row moves for small lists, which does not
    /// rely on the longest-increasing-subsequence argument the planner is built on. The search has the planner's
    /// own constraint: a row can only be put in FRONT of another row of the list, never after its end, because
    /// what follows the list on the router is not known without reading the whole table.
    /// </remarks>
    [TestClass]
    public class TikListSyncPlannerTests
    {
        private const int Foreign = -100;   // a row of the table that is not in the list

        /// <summary>
        /// Replays a plan on a table holding the current rows (list rows as their desired index, plus foreign rows)
        /// and returns the table afterwards. <see cref="TikListSyncPlanner.Tail"/> is the end of the table.
        /// </summary>
        private static List<int> Replay(List<int> table, IReadOnlyList<TikListSyncStep> steps)
        {
            var rows = new List<int>(table);
            int foreignCount = 0;
            for (int i = 0; i < rows.Count; i++) if (rows[i] <= Foreign) rows[i] = Foreign - foreignCount++;
            foreach (var step in steps)
            {
                int anchorRow = step.Anchor == TikListSyncPlanner.Tail ? int.MaxValue : step.Anchor;

                if (step.Kind == TikListSyncStepKind.Move)
                    Assert.IsTrue(rows.Remove(step.Row), "a move of row " + step.Row + " that is not on the table");
                else
                    Assert.IsFalse(rows.Contains(step.Row), "a create of row " + step.Row + " that already exists");

                if (anchorRow == int.MaxValue)
                    rows.Add(step.Row);
                else
                {
                    int at = rows.IndexOf(anchorRow);
                    Assert.IsTrue(at >= 0, "anchor " + anchorRow + " is not on the table");
                    rows.Insert(at, step.Row);
                }
            }
            return rows;
        }

        private static IEnumerable<int[]> Permutations(int n)
        {
            var items = Enumerable.Range(0, n).ToArray();
            return Permute(items, 0);
        }

        private static IEnumerable<int[]> Permute(int[] a, int k)
        {
            if (k == a.Length) { yield return (int[])a.Clone(); yield break; }
            for (int i = k; i < a.Length; i++)
            {
                (a[k], a[i]) = (a[i], a[k]);
                foreach (var p in Permute(a, k + 1)) yield return p;
                (a[k], a[i]) = (a[i], a[k]);
            }
        }

        private static int LisLength(IList<int> s)
        {
            if (s.Count == 0) return 0;
            var best = new int[s.Count];
            for (int i = 0; i < s.Count; i++)
            {
                best[i] = 1;
                for (int j = 0; j < i; j++)
                    if (s[j] < s[i]) best[i] = Math.Max(best[i], best[j] + 1);
            }
            return best.Max();
        }

        /// <summary>
        /// Fewest single-row moves turning <paramref name="from"/> into the identity order, by BFS, where a move puts
        /// a row in front of another row of the list and never after the list's end.
        /// </summary>
        private static int BfsMinimumMoves(int[] from)
        {
            string target = string.Join(",", Enumerable.Range(0, from.Length));
            var seen = new HashSet<string> { string.Join(",", from) };
            var frontier = new List<int[]> { from };
            for (int depth = 0; ; depth++)
            {
                if (frontier.Any(f => string.Join(",", f) == target)) return depth;
                var next = new List<int[]>();
                foreach (var state in frontier)
                    for (int i = 0; i < state.Length; i++)
                        for (int j = 0; j < state.Length - 1; j++)   // in front of one of the others; never appended
                        {
                            var list = state.ToList();
                            int v = list[i];
                            list.RemoveAt(i);
                            list.Insert(j, v);
                            var arr = list.ToArray();
                            if (seen.Add(string.Join(",", arr))) next.Add(arr);
                        }
                frontier = next;
            }
        }

        private static IReadOnlyList<TikListSyncStep> Plan(int n, IEnumerable<int> currentOrder, ISet<int> newRows)
        {
            var desiredKeys = Enumerable.Range(0, n).Select(i => newRows.Contains(i) ? null : "k" + i).ToList();
            return TikListSyncPlanner.PlanOrder(desiredKeys, currentOrder.Select(i => "k" + i));
        }

        [TestMethod]
        public void EveryPermutationLandsInOrderWithAtMostOneMoveOverAnUnrestrictedReorder()
        {
            for (int n = 0; n <= 7; n++)
                foreach (var current in Permutations(n))
                {
                    var steps = Plan(n, current, new HashSet<int>());
                    int moves = steps.Count(s => s.Kind == TikListSyncStepKind.Move);
                    int unrestricted = n - LisLength(current);
                    bool lastInPlace = n == 0 || current[n - 1] == n - 1;

                    CollectionAssert.AreEqual(Enumerable.Range(0, n).ToList(), Replay(current.ToList(), steps),
                        "order after the plan, from " + string.Join(",", current));
                    Assert.IsTrue(moves == unrestricted || (moves == unrestricted + 1 && !lastInPlace),
                        "move count " + moves + " from " + string.Join(",", current) + " (unrestricted " + unrestricted + ")");
                    if (lastInPlace)
                        Assert.AreEqual(unrestricted, moves, "the last row stays, so nothing is paid for not knowing the end");
                    Assert.IsFalse(steps.Any(s => s.Kind == TikListSyncStepKind.Create));
                    Assert.IsFalse(steps.Any(s => s.Anchor == TikListSyncPlanner.Tail), "rows exist, so every anchor is a row");
                }
        }

        [TestMethod]
        public void TheMoveCountIsTheTrueMinimum()
        {
            // Independent of the LIS argument: breadth-first search over single-row moves that never append.
            for (int n = 1; n <= 6; n++)
                foreach (var current in Permutations(n))
                    Assert.AreEqual(BfsMinimumMoves(current),
                        Plan(n, current, new HashSet<int>()).Count(s => s.Kind == TikListSyncStepKind.Move),
                        "from " + string.Join(",", current));
        }

        [TestMethod]
        public void NewRowsAreCreatedInPlaceAndNeverMoved()
        {
            // Every subset of a 6-row list is new, the rest existing in every order.
            int n = 6;
            for (int mask = 0; mask < (1 << n); mask++)
            {
                var newRows = new HashSet<int>(Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0));
                var existing = Enumerable.Range(0, n).Where(i => !newRows.Contains(i)).ToArray();
                foreach (var perm in Permute(existing, 0))
                {
                    var steps = Plan(n, perm, newRows);
                    int moves = steps.Count(s => s.Kind == TikListSyncStepKind.Move);
                    int unrestricted = existing.Length - LisLength(perm);
                    CollectionAssert.AreEqual(Enumerable.Range(0, n).ToList(), Replay(perm.ToList(), steps));
                    Assert.AreEqual(newRows.Count, steps.Count(s => s.Kind == TikListSyncStepKind.Create));
                    Assert.IsTrue(moves == unrestricted || moves == unrestricted + 1, "moves " + moves);
                    Assert.AreEqual(existing.Length == 0, steps.Any(s => s.Anchor == TikListSyncPlanner.Tail),
                        "the end of the table is an anchor only when no row of the list exists yet");
                }
            }
        }

        [TestMethod]
        public void ForeignRowsKeepTheirOrderAndTheListLandsAmongThem()
        {
            // List rows 0..3 interleaved with foreign rows in every arrangement of the list; the foreign rows'
            // own order must not change, and the list must come out in order.
            foreach (var perm in Permutations(4))
            {
                var table = new List<int> { Foreign, perm[0], perm[1], Foreign, perm[2], Foreign, perm[3], Foreign };
                var steps = Plan(4, perm, new HashSet<int>());
                var after = Replay(table, steps);

                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, after.Where(r => r >= 0).ToList());
                CollectionAssert.AreEqual(new[] { Foreign, Foreign - 1, Foreign - 2, Foreign - 3 },
                    after.Where(r => r <= Foreign).ToList(), "foreign rows reordered from " + string.Join(",", perm));
                // The list's last row goes in front of the foreign row that followed the list, never past it.
                Assert.IsTrue(after.IndexOf(3) < after.IndexOf(Foreign - 3), "from " + string.Join(",", perm));
            }
        }

        [TestMethod]
        public void SendingOneRowToTheEndIsTwoMovesAndNoRead()
        {
            // A B C D -> B C D A. The router cannot put A "after D" without knowing what follows D, so A goes in
            // front of D and D in front of A: two moves, where anchoring everything on the last row made it three.
            var steps = Plan(4, new[] { 3, 0, 1, 2 }, new HashSet<int>());
            Assert.AreEqual(2, steps.Count);
            Assert.AreEqual(3, steps[0].Row);
            Assert.AreEqual(2, steps[0].Anchor);
            Assert.AreEqual(2, steps[1].Row);
            Assert.AreEqual(3, steps[1].Anchor);
        }

        [TestMethod]
        public void BringingARowToTheFrontIsOneMove()
        {
            var steps = Plan(4, new[] { 1, 2, 3, 0 }, new HashSet<int>());
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(0, steps[0].Row);
        }

        [TestMethod]
        public void RowsOnTheTableThatAreNotInTheListAreIgnored()
        {
            var steps = TikListSyncPlanner.PlanOrder(new List<string> { "a", "b" }, new[] { "x", "b", "y", "a", "z" });
            Assert.AreEqual(1, steps.Count);
        }
    }
}
