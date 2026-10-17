using System;
using System.Collections.Generic;

namespace tik4net.Objects
{
    /// <summary>
    /// The state, value, word and negation shared by <see cref="TikField{T}"/> and <see cref="TikNegatableField{T}"/> — one
    /// copy of the Absent/Present/Unparsed rules. The two public types differ only in whether <see cref="Negated"/> may be set.
    /// </summary>
    internal readonly struct TikFieldCore<T>
    {
        internal readonly bool Printed;
        internal readonly TikValue<T> Content;
        // The field's leading '!'. Set only on a present, non-null value.
        internal readonly bool Negated;

        internal TikFieldCore(TikValue<T> content, bool negated = false)
        {
            Printed = true;
            Content = content;
            Negated = negated && !content.IsWord && content.Value != null;
        }

        internal TikFieldState State => !Printed ? TikFieldState.Absent : Content.IsWord ? TikFieldState.Unparsed : TikFieldState.Present;

        internal T Value => Printed ? Content.Value : default!;

        internal string? RawValue => Printed ? Content.RawValue : null;

        internal bool IsNegated => Negated;

        internal object? BoxedValue => State == TikFieldState.Present ? Content.Value : null;

        internal T ValueOrDefault(T fallback) => State == TikFieldState.Present ? Content.Value : fallback;

        internal TikFieldCore<T> WithoutNegation() => Printed ? new TikFieldCore<T>(Content) : this;

        internal bool TryGetValue(out T value)
        {
            if (State == TikFieldState.Present)
                return Content.TryGetValue(out value);
            value = default!;
            return false;
        }

        // Against null: "has no value" — absent, or present with null; never unparsed (the router printed something).
        // Against a value: present and holding exactly that value, negation included.
        internal bool EqualsValue(TikValue<T> b, bool bNegated = false)
            => !b.IsWord && b.Value == null
                ? !Printed || (!Content.IsWord && Content.Value == null)
                : Printed && Content == b && Negated == bNegated;

        // A lifted nullable comparison of plain values: false when either side has none, or either side is negated.
        internal bool Lifted(TikValue<T> b, bool bNegated, out int comparison)
        {
            comparison = 0;
            if (State != TikFieldState.Present || Negated || Content.Value == null || b.IsWord || bNegated || b.Value == null)
                return false;
            comparison = Comparer<T>.Default.Compare(Content.Value, b.Value);
            return true;
        }

        internal int CompareTo(TikFieldCore<T> other)
        {
            if (State != other.State)
                return Rank(State).CompareTo(Rank(other.State));
            if (!Printed)
                return 0;
            int byContent = Content.CompareTo(other.Content);
            return byContent != 0 ? byContent : Negated.CompareTo(other.Negated);
        }

        private static int Rank(TikFieldState state)
            => state == TikFieldState.Absent ? 0 : state == TikFieldState.Unparsed ? 1 : 2;

        internal bool Equals(TikFieldCore<T> other)
            => Printed == other.Printed && (!Printed || (Content.Equals(other.Content) && Negated == other.Negated));

        public override int GetHashCode()
        {
            unchecked
            {
                if (!Printed)
                    return 0;
                return ((int)State * 397 ^ Content.GetHashCode()) + (Negated ? 16 : 0);
            }
        }

        public override string ToString()
            => !Printed ? string.Empty
             : Content.IsWord ? Content.RawValue!
             : (Negated ? TikWireText.Negation(Content.Value) : "") + TikWireText.Format(Content.Value);

        internal string DebuggerText
            => !Printed ? "Absent"
             : Content.IsWord ? "Unparsed \"" + Content.RawValue + "\""
             : Content.Value == null ? "null"
             : (Negated ? TikWireText.Negation(Content.Value) : "") + TikWireText.Format(Content.Value);
    }
}
