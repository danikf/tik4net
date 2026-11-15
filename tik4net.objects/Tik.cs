using System;

namespace tik4net.Objects
{
    /// <summary>
    /// Short helpers for writing entity values the way the RouterOS console spells them.
    /// </summary>
    public static class Tik
    {
        /// <summary>
        /// The console's spelling of a value, for any entity property: <c>rule.DstPort = Tik.Wire("!22,8291")</c>,
        /// <c>server.LeaseTime = Tik.Wire("1d")</c>, <c>rule.TcpFlags = Tik.Wire("syn,!ack")</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The text is parsed for the property it is assigned to, strictly: a <see cref="TikField{T}"/> reads it as
        /// <see cref="TikField{T}.Parse"/> does, a <see cref="TikNegatableField{T}"/> as <see cref="TikNegatableField{T}.Parse"/>
        /// does, and a value the property's type cannot hold throws at the assignment. So the type still decides what a
        /// <c>!</c> means: a negation on a negatable matcher, a member's negation in a list of
        /// <see cref="TikNegatableValue{T}"/>, text on a <c>string?</c> field, and a refusal anywhere else.
        /// </para>
        /// <para>
        /// It is the one spelling that needs no type named: a <c>string</c> literal converts to the property only where the
        /// property's value is a <c>string</c>, because C# applies one user-defined conversion per assignment
        /// (<c>(TikDuration)"1d"</c> otherwise). For a word the type has no member for — an action a newer RouterOS added —
        /// use <see cref="TikField{T}.FromWire"/>, which sends the word as written instead of parsing it.
        /// </para>
        /// </remarks>
        /// <param name="text">The value as the console writes it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
        public static TikWireString Wire(string text) => new TikWireString(text ?? throw new ArgumentNullException(nameof(text)));
    }

    /// <summary>
    /// A value in the RouterOS console's spelling, made by <see cref="Tik.Wire"/>, which converts to whichever
    /// <see cref="TikField{T}"/> or <see cref="TikNegatableField{T}"/> it is assigned to, parsing the text for that type.
    /// </summary>
    public readonly struct TikWireString : IEquatable<TikWireString>
    {
        internal TikWireString(string text)
        {
            Text = text;
        }

        /// <summary>The text as written; <c>null</c> only on a <c>default</c> instance.</summary>
        public string Text { get; }

        /// <inheritdoc/>
        public bool Equals(TikWireString other) => string.Equals(Text, other.Text, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TikWireString other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Text == null ? 0 : StringComparer.Ordinal.GetHashCode(Text);

        /// <summary>The text as written.</summary>
        public override string ToString() => Text ?? "";

        /// <summary>Same text.</summary>
        public static bool operator ==(TikWireString a, TikWireString b) => a.Equals(b);

        /// <summary>Different text.</summary>
        public static bool operator !=(TikWireString a, TikWireString b) => !a.Equals(b);
    }
}
