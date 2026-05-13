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
        public string Value { get; set; }

        /// <inheritdoc/>
        public TikCommandParameterFormat ParameterFormat { get; set; }

        /// <summary>Creates a fake command parameter.</summary>
        public TikFakeParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            Name = name;
            Value = value;
            ParameterFormat = parameterFormat;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Name}={Value}";
    }
}
