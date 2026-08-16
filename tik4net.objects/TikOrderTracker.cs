using System;
using System.Collections.Generic;

namespace tik4net.Objects
{
    /// <summary>
    /// Tracks the order the router currently holds while a merge rewrites it, and decides whether a given
    /// row still needs a <c>/move</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both list-shaped writers reorder an <c>IsOrdered</c> menu the same way — walk the desired list from
    /// last to first and move each row before the one already placed — and the decision "does this row need
    /// moving" is the part that is easy to get subtly wrong, so it lives here once rather than in each.
    /// </para>
    /// <para>
    /// <b>The check must be against the CURRENT order, not the original one.</b> Every move already applied
    /// changes who sits next to whom, so comparing against the starting indexes skips moves that are still
    /// needed: a three-way reorder of a mangle section once applied 3 of the 7 moves it needed and produced
    /// an order matching neither input. That is what this class exists to prevent recurring in two places.
    /// </para>
    /// </remarks>
    internal sealed class TikOrderTracker
    {
        private readonly List<string> _order;

        /// <summary>Starts tracking from the order the router holds now.</summary>
        /// <param name="currentKeys">Row keys, in the router's current order.</param>
        public TikOrderTracker(IEnumerable<string> currentKeys)
        {
            _order = new List<string>(currentKeys);
        }

        /// <summary>The order as tracked so far.</summary>
        public IEnumerable<string> Current
        {
            get { return _order; }
        }

        /// <summary>Records that a row was deleted from the router.</summary>
        public void Remove(string key)
        {
            _order.Remove(key);
        }

        /// <summary>Records that a row was created — the router appends, so a move is what places it.</summary>
        public void Append(string key)
        {
            _order.Add(key);
        }

        /// <summary>
        /// Returns whether <paramref name="movedKey"/> has to be moved to sit immediately before
        /// <paramref name="anchorKey"/>.
        /// </summary>
        /// <param name="movedKey">The row being placed.</param>
        /// <param name="anchorKey">The row it must end up in front of.</param>
        /// <param name="movedIndex">Current index of the moved row, or -1 when it is not tracked.</param>
        /// <param name="anchorIndex">Current index of the anchor row, or -1 when it is not tracked.</param>
        /// <returns>False only when the row already sits immediately before the anchor.</returns>
        public bool NeedsMove(string movedKey, string anchorKey, out int movedIndex, out int anchorIndex)
        {
            movedIndex = _order.IndexOf(movedKey);
            anchorIndex = _order.IndexOf(anchorKey);

            // An untracked row (index -1) is moved on purpose: not knowing where it is means not being able
            // to prove it is already in place, and a redundant move is harmless where a skipped one is not.
            return movedIndex < 0 || anchorIndex < 0 || movedIndex != anchorIndex - 1;
        }

        /// <summary>
        /// Records the move reported by <see cref="NeedsMove"/> — the row now sits immediately before the anchor.
        /// </summary>
        /// <param name="movedKey">The row that was moved.</param>
        /// <param name="anchorKey">The row it was moved in front of.</param>
        public void ApplyMove(string movedKey, string anchorKey)
        {
            int movedIndex = _order.IndexOf(movedKey);
            if (movedIndex >= 0)
                _order.RemoveAt(movedIndex);

            int anchorIndex = _order.IndexOf(anchorKey);
            if (anchorIndex >= 0)
                _order.Insert(anchorIndex, movedKey);
        }
    }
}
