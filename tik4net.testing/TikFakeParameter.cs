using System;

namespace tik4net.Testing
{
    /// <summary>
    /// Fake implementation of <see cref="ITikCommandParameter"/> for use with <see cref="TikFakeConnection"/>.
    /// </summary>
    public sealed class TikFakeParameter : ITikCommandParameter
    {
        /// <inheritdoc/>
        public string Name { get; set; }

        /// <inheritdoc/>
        public string? Value { get; set; }

        /// <inheritdoc/>
        public TikCommandParameterFormat ParameterFormat { get; set; }

        /// <summary>Creates a fake command parameter.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty, which every real transport refuses: no
        /// transport can send a nameless word. A bare <c>?name</c> filter has a null <paramref name="value"/>, not an
        /// empty name.</exception>
        public TikFakeParameter(string name, string? value, TikCommandParameterFormat parameterFormat)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (name.Length == 0)
                throw new ArgumentException("The provided String argument name must not be empty.", nameof(name));
            Name = name;
            Value = value;
            ParameterFormat = parameterFormat;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Name}={Value}";
    }
}
