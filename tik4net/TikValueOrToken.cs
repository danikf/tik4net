using System;

namespace tik4net
{
    /// <summary>
    /// What <see cref="TikDuration"/> and <see cref="TikDataRate"/> share: a value, or the word the router used in place of
    /// one, or neither (the type's default). Two of them are equal on the value, and on the word ignoring its case.
    /// </summary>
    internal readonly struct TikValueOrToken<TValue> : IEquatable<TikValueOrToken<TValue>>
        where TValue : struct
    {
        internal readonly TValue? Value;
        internal readonly string? Token;

        internal TikValueOrToken(TValue? value, string? token)
        {
            Value = value;
            Token = token;
        }

        internal TikValueKind Kind(bool isKnownWord)
            => Value.HasValue ? TikValueKind.Value
             : Token == null ? TikValueKind.Empty
             : isKnownWord ? TikValueKind.Special
             : TikValueKind.Unknown;

        public bool Equals(TikValueOrToken<TValue> other)
            => Nullable.Equals(Value, other.Value)
            && string.Equals(Token, other.Token, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is TikValueOrToken<TValue> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Value.HasValue ? Value.Value.GetHashCode() : 0;
                return (h * 397) ^ (Token == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Token));
            }
        }
    }
}
