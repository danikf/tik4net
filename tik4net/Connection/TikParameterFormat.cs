namespace tik4net.Connection
{
    /// <summary>
    /// The one rule both command classes use to decide how a parameter of format
    /// <see cref="TikCommandParameterFormat.Default"/> is sent.
    /// </summary>
    /// <remarks>
    /// Names that already carry their format (<c>=name</c>, <c>?name</c>) are not resolved here: each caller keeps that
    /// check, since the name itself is what goes on the wire.
    /// </remarks>
    internal static class TikParameterFormat
    {
        /// <summary>
        /// The format <paramref name="parameter"/> is sent with. Precedence: the parameter's own format; then the
        /// names whose format is fixed by what they are (<c>.proplist</c> is always the field list,
        /// <see cref="TikCommandParameterFormat.NameValue"/>; <c>.tag</c> is
        /// <see cref="TikCommandParameterFormat.Tag"/>); then the command's default; then the use case's default
        /// (<see cref="TikCommandParameterFormat.Filter"/> for a read, <see cref="TikCommandParameterFormat.NameValue"/>
        /// for a write); then <see cref="TikCommandParameterFormat.NameValue"/>.
        /// </summary>
        internal static TikCommandParameterFormat Resolve(ITikCommandParameter parameter,
            TikCommandParameterFormat commandDefault, TikCommandParameterFormat useCaseDefault)
        {
            if (parameter.ParameterFormat != TikCommandParameterFormat.Default)
                return parameter.ParameterFormat;
            if (parameter.Name == TikSpecialProperties.Proplist)
                return TikCommandParameterFormat.NameValue;
            if (parameter.Name == TikSpecialProperties.Tag)
                return TikCommandParameterFormat.Tag;
            if (commandDefault != TikCommandParameterFormat.Default)
                return commandDefault;
            if (useCaseDefault != TikCommandParameterFormat.Default)
                return useCaseDefault;
            return TikCommandParameterFormat.NameValue;
        }
    }
}
