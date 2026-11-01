using System;
using System.Collections.Generic;

namespace tik4net.Objects
{
    /// <summary>
    /// A model of an ordered menu's row order that a planned command sequence is replayed on — the check that
    /// <see cref="TikListSyncPlanner"/> runs on every plan before a command is sent. Rows are the desired list's indexes.
    /// </summary>
    /// <remarks>
    /// <b>A plan has to be checked against the order as each step leaves it, not the starting one.</b> Every
    /// move changes who sits next to whom, so reasoning from the starting indexes skips moves that are still
    /// needed: a three-way reorder of a mangle section once applied 3 of the 7 moves it needed and produced an
    /// order matching neither input. Replaying the steps here and comparing the result with the desired order
    /// is what stops that recurring.
    /// </remarks>
    internal sealed class TikOrderTracker
    {
        private readonly List<int> _order;

        /// <summary>Starts tracking from the order the router holds now.</summary>
        /// <param name="currentRows">Rows, in the router's current order.</param>
        public TikOrderTracker(IEnumerable<int> currentRows)
        {
            _order = new List<int>(currentRows);
        }

        /// <summary>The order as tracked so far.</summary>
        public IEnumerable<int> Current
        {
            get { return _order; }
        }

        /// <summary>Number of tracked rows.</summary>
        public int Count
        {
            get { return _order.Count; }
        }

        /// <summary>Current index of a row, or -1 when it is not tracked.</summary>
        public int IndexOf(int row)
        {
            return _order.IndexOf(row);
        }

        /// <summary>
        /// Puts <paramref name="row"/> immediately before <paramref name="anchorRow"/> — a <c>/move</c> of a
        /// tracked row, or a create with <c>place-before</c> of an untracked one.
        /// </summary>
        /// <exception cref="InvalidOperationException">The anchor is not tracked.</exception>
        public void MoveBefore(int row, int anchorRow)
        {
            _order.Remove(row);
            int anchorIndex = _order.IndexOf(anchorRow);
            if (anchorIndex < 0)
                throw new InvalidOperationException("TikOrderTracker: anchor row " + anchorRow + " is not tracked.");
            _order.Insert(anchorIndex, row);
        }

        /// <summary>Puts <paramref name="row"/> after every tracked row.</summary>
        public void MoveToEnd(int row)
        {
            _order.Remove(row);
            _order.Add(row);
        }
    }
}
