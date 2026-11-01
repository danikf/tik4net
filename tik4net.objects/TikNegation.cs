using System;

namespace tik4net.Objects
{
    /// <summary>
    /// How the router spells a <c>!</c>: in front of a value or a list member (<c>!10.0.0.0/8</c>, <c>!ack</c>), and on a
    /// whole list either in front of the first item (<c>dst-port=!22,8291</c>) or, on a list whose members carry their own,
    /// as a bare leading element (<c>tcp-flags=!,syn,!ack</c>, where <c>!syn</c> would read as one negated member).
    /// </summary>
    internal static class TikNegation
    {
        internal const string Bang = "!";

        /// <summary>
        /// Whether <paramref name="text"/> is a negated value or member, with <paramref name="rest"/> what it negates. A bare
        /// <c>!</c> negates nothing, so it is not one: as text it stays text.
        /// </summary>
        internal static bool TrySplit(string text, out string rest)
        {
            bool negated = text.Length > 1 && text[0] == '!';
            rest = negated ? text.Substring(1) : text;
            return negated;
        }

        /// <summary>
        /// Whether <paramref name="text"/> carries a whole-list <c>!</c>, with <paramref name="items"/> what it negates.
        /// <paramref name="items"/> may be blank (<c>!</c>, <c>!,</c>), which negates nothing: see <see cref="NegatesNothing"/>.
        /// </summary>
        internal static bool SplitWholeList(string text, bool membersNegatable, out string items)
        {
            items = text;
            if (text.Length == 0 || text[0] != '!')
                return false;
            if (membersNegatable)
            {
                if (text.Length > 1 && text[1] != ',')
                    return false;   // !syn,ack: the first member's own '!'
                items = text.Length > 1 ? text.Substring(2) : "";
                return true;
            }
            items = text.Substring(1);
            return true;
        }

        internal static bool NegatesNothing(string items) => items.Trim().Length == 0;

        /// <summary>What a whole-list <c>!</c> looks like in front of the items.</summary>
        internal static string WholeListPrefix(bool membersNegatable) => membersNegatable ? "!," : Bang;
    }
}
