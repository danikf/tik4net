using tik4net.Connection;

namespace tik4net
{
    /// <summary>
    /// The one <see cref="TikAddIdNotReadException"/> every transport throws for an <c>add</c> the router answered without
    /// the new <c>.id</c>.
    /// </summary>
    internal static class TikAddIdNotRead
    {
        /// <summary>
        /// The exception for <paramref name="command"/>, carrying what the router said: <paramref name="routerSaid"/>, or
        /// null (and blank) for "nothing".
        /// </summary>
        /// <remarks>
        /// No id in the answer, and the router did not complain, so the row is very probably on the router with nothing
        /// referring to it. It is reported rather than returned because every way of returning it was wrong: a null
        /// through the non-nullable <c>RunAdd</c> contract, a default from <c>ExecuteScalarOrDefault</c>, or (on the
        /// terminals) the last line of the answer, which then reached the <c>[find where .id=…]</c> of the next set or
        /// remove and matched nothing. Either way the caller's own cleanup had no id to delete with, so the row outlived
        /// the process that made it. What the router said is carried with it, because "nothing arrived" and "something
        /// arrived that was not an id" are different diagnoses.
        /// </remarks>
        internal static TikAddIdNotReadException Create(ITikCommand command, string? routerSaid)
        {
            string said = (routerSaid ?? string.Empty).Trim();
            return new TikAddIdNotReadException(
                "the add reached the router but no .id came back, so the new row cannot be tracked and has "
                + "very likely been created anyway — look it up by a field the add set rather than repeating "
                + "the add, which would make a second row. The router answered: "
                + (said.Length == 0 ? "(nothing)" : "'" + said + "'"),
                command,
                new TikDoneSentenceResult(said.Length == 0 ? null : said));
        }
    }
}
