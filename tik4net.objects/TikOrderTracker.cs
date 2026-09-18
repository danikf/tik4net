using System;
using System.Collections.Generic;

namespace tik4net.Objects
{
    /// <summary>
    /// A model of an ordered menu's row order that a planned command sequence is replayed on — the check that
    /// <see cref="TikListSyncPlanner"/> runs on every plan before a command is sent.
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

        /// <summary>Number of tracked rows.</summary>
        public int Count
        {
            get { return _order.Count; }
        }

        /// <summary>Current index of a row, or -1 when it is not tracked.</summary>
        public int IndexOf(string key)
        {
            return _order.IndexOf(key);
        }

        /// <summary>
        /// Puts <paramref name="key"/> immediately before <paramref name="anchorKey"/> — a <c>/move</c> of a
        /// tracked row, or a create with <c>place-before</c> of an untracked one.
        /// </summary>
        /// <exception cref="InvalidOperationException">The anchor is not tracked.</exception>
        public void MoveBefore(string key, string anchorKey)
        {
            _order.Remove(key);
            int anchorIndex = _order.IndexOf(anchorKey);
            if (anchorIndex < 0)
                throw new InvalidOperationException("TikOrderTracker: anchor '" + anchorKey + "' is not tracked.");
            _order.Insert(anchorIndex, key);
        }

        /// <summary>Puts <paramref name="key"/> after every tracked row.</summary>
        public void MoveToEnd(string key)
        {
            _order.Remove(key);
            _order.Add(key);
        }
    }
}
