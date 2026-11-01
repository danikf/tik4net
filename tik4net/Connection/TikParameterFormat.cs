using System;
using System.Collections.Generic;

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
        /// <paramref name="name"/> without the prefix that carries its format: <c>=comment</c> and <c>?comment</c> →
        /// <c>comment</c>. A filter's operator stays: <c>?&gt;mtu</c> → <c>&gt;mtu</c>.
        /// </summary>
        internal static string BareName(string name) => name.TrimStart('=', '?');

        /// <summary>
        /// The first non-empty value of the parameter called <paramref name="bareName"/>, in whichever format it arrived.
        /// </summary>
        /// <remarks>
        /// Both formats have to be accepted. A parameter the caller declared <c>NameValue</c> keeps that
        /// format, while an unformatted one is rewritten to <c>Filter</c> on the read path by
        /// <c>TikGenericCommand.ResolveParamsForRead</c> — so the same command arrives two different ways
        /// depending only on how it was spelled, and honouring one of them would fix half the callers.
        /// </remarks>
        internal static string? FindInput(IEnumerable<ITikCommandParameter> parameters, string bareName)
        {
            foreach (var p in parameters)
            {
                if (string.Equals(BareName(p.Name), bareName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(p.Value))
                    return p.Value;
            }
            return null;
        }

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
