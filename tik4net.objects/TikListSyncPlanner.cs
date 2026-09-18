using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace tik4net.Objects
{
    /// <summary>What one ordering step does: move an existing row, or create a new one in place.</summary>
    internal enum TikListSyncStepKind
    {
        /// <summary><c>/move numbers=&lt;row&gt; destination=&lt;anchor&gt;</c>.</summary>
        Move,
        /// <summary><c>/add … place-before=&lt;anchor&gt;</c>.</summary>
        Create,
    }

    /// <summary>
    /// One command of the ordering pass. <see cref="Row"/> and <see cref="Anchor"/> are indexes into the
    /// desired list; <see cref="Anchor"/> is <see cref="TikListSyncPlanner.Tail"/> — the end of the table — only
    /// when none of the list's rows exists yet.
    /// </summary>
    internal readonly struct TikListSyncStep
    {
        public TikListSyncStep(TikListSyncStepKind kind, int row, int anchor, int oldIndex, int newIndex)
        {
            Kind = kind;
            Row = row;
            Anchor = anchor;
            OldIndex = oldIndex;
            NewIndex = newIndex;
        }

        public TikListSyncStepKind Kind { get; }

        /// <summary>Index of the placed row in the desired list.</summary>
        public int Row { get; }

        /// <summary>Index of the row it goes in front of, or <see cref="TikListSyncPlanner.Tail"/>.</summary>
        public int Anchor { get; }

        /// <summary>For a move: the row's index in the list as it stands before this step (-1 when unknown).</summary>
        public int OldIndex { get; }

        /// <summary>For a move: the anchor's index before this step (the list's length for the end of the table).</summary>
        public int NewIndex { get; }
    }

    /// <summary>
    /// Decides the commands that leave an ordered menu's rows in a desired order: the fewest single-row moves,
    /// and every new row created in place. Pure — no I/O — so the list writers (<see cref="TikListMerge{TEntity}"/>
    /// and <c>SaveListDifferences</c>, sync and async) share it and differ only in how they send the commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fewest moves.</b> The rows whose current positions already form the longest increasing subsequence
    /// (in desired order) stay where they are; every other existing row is moved once. Walking the desired list
    /// from last to first, each row goes in front of its desired successor, which by then is final: either one
    /// of the rows that never move, or a row this walk has already placed.
    /// </para>
    /// <para>
    /// <b>New rows</b> are created with <c>place-before</c> in the same walk, so they cost one command and no move.
    /// </para>
    /// <para>
    /// <b>The last row.</b> RouterOS can only place a row in FRONT of another, and the row after the list is not
    /// known: the list may be a filtered part of the table (one chain, one comment tag), and finding its successor
    /// would mean reading the whole table — on a CLI transport a full <c>detail</c> read of every rule, to move
    /// one. So when the last desired row is not already last, it goes in front of the list's current last row,
    /// and that row is not counted among the ones that stay put — it moves in front of its own successor later
    /// in the walk. The plan is then the fewest single-row moves that never place a row after the list's end,
    /// which is at most one more than an unrestricted reorder (sending one rule of four to the end is two moves,
    /// not one) and costs nothing when the last row stays. The list's rows end up exactly where they were
    /// among the rows of the table that are not in it.
    /// </para>
    /// <para>
    /// The plan is checked before it is returned: it is replayed on a <see cref="TikOrderTracker"/> and must
    /// produce exactly the desired order. A plausible command sequence that lands the rows elsewhere is how an
    /// earlier version of this pass shipped (3 of 7 moves, an order matching neither input), so a planner defect
    /// throws here rather than reaching the router.
    /// </para>
    /// </remarks>
    internal static class TikListSyncPlanner
    {
        /// <summary>The <see cref="TikListSyncStep.Anchor"/> meaning "the end of the table" — used only when none of the list's rows exists yet.</summary>
        public const int Tail = -1;

        /// <summary>
        /// Plans the ordering pass.
        /// </summary>
        /// <param name="desiredKeys">Key of every desired row, in the desired order; <c>null</c> for a row that
        /// does not exist yet and is created by this pass. Keys must be unique.</param>
        /// <param name="currentKeys">Keys of the existing rows in their current router order. Keys that are not
        /// among <paramref name="desiredKeys"/> are ignored — they belong to someone else.</param>
        /// <returns>The steps, in the order they must be sent.</returns>
        public static IReadOnlyList<TikListSyncStep> PlanOrder(IList<string?> desiredKeys, IEnumerable<string> currentKeys)
        {
            int n = desiredKeys.Count;
            var desiredIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
                if (desiredKeys[i] != null)
                    desiredIndexOf.Add(desiredKeys[i]!, i);

            // Current order restricted to the rows this list owns, as desired indexes.
            var current = currentKeys.Where(desiredIndexOf.ContainsKey).Select(k => desiredIndexOf[k]).ToList();
            var fixedRows = LongestIncreasingSubsequence(current);

            // The last desired row goes in front of the list's current last row when it is not already in place
            // (see the remarks): that row then has to move itself, so it is left out of the rows that stay put.
            int lastAnchor = Tail;
            bool lastIsPlaced = n > 0 && desiredKeys[n - 1] != null && fixedRows.Contains(n - 1);
            if (n > 0 && !lastIsPlaced && current.Count > 0)
            {
                lastAnchor = current[current.Count - 1];
                fixedRows = LongestIncreasingSubsequence(current.Take(current.Count - 1).ToList());
            }

            // Replay target: tracker keys are the desired indexes, so a new row has one too.
            var tracker = new TikOrderTracker(current.Select(Token));
            var steps = new List<TikListSyncStep>();
            for (int i = n - 1; i >= 0; i--)
            {
                bool isNew = desiredKeys[i] == null;
                if (!isNew && fixedRows.Contains(i))
                    continue;

                int anchor = i == n - 1 ? lastAnchor : i + 1;
                var kind = isNew ? TikListSyncStepKind.Create : TikListSyncStepKind.Move;
                int oldIndex = isNew ? -1 : tracker.IndexOf(Token(i));
                int newIndex = anchor == Tail ? tracker.Count : tracker.IndexOf(Token(anchor));
                steps.Add(new TikListSyncStep(kind, i, anchor, oldIndex, newIndex));

                if (anchor == Tail)
                    tracker.MoveToEnd(Token(i));
                else
                    tracker.MoveBefore(Token(i), Token(anchor));
            }

            var expected = Enumerable.Range(0, n).Select(Token);
            if (!tracker.Current.SequenceEqual(expected))
                throw new InvalidOperationException(
                    "TikListSyncPlanner: the ordering plan does not reproduce the desired order — a defect in the planner, "
                    + "caught before any command was sent.");

            return steps;
        }

        private static string Token(int desiredIndex) => desiredIndex.ToString(CultureInfo.InvariantCulture);

        /// <summary>The values of one longest strictly increasing subsequence of <paramref name="sequence"/>.</summary>
        internal static HashSet<int> LongestIncreasingSubsequence(IList<int> sequence)
        {
            // Patience sorting, O(n log n): tails[k] is the index (into sequence) of the smallest possible last
            // element of an increasing run of length k+1; parent links rebuild one run of the maximum length.
            var tails = new List<int>();
            var parent = new int[sequence.Count];
            for (int i = 0; i < sequence.Count; i++)
            {
                int lo = 0, hi = tails.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (sequence[tails[mid]] < sequence[i]) lo = mid + 1; else hi = mid;
                }
                parent[i] = lo > 0 ? tails[lo - 1] : -1;
                if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
            }

            var result = new HashSet<int>();
            for (int k = tails.Count > 0 ? tails[tails.Count - 1] : -1; k >= 0; k = parent[k])
                result.Add(sequence[k]);
            return result;
        }
    }
}
