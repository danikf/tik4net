using System.Collections.Generic;

namespace tik4net.Connection
{
    /// <summary>
    /// The answer rules both command classes share: which verbs read, what an empty answer means, and how many rows a
    /// single-row read may return. <c>ApiCommand</c> checks the sentence shapes of the binary API before it asks;
    /// <c>TikGenericCommand</c> asks with the rows its transport returned.
    /// </summary>
    /// <remarks>
    /// An empty answer means two different things, and only the verb tells them apart: after a read it is "nothing
    /// matched" (<see cref="TikNoSuchItemException"/>), after any other verb it is "succeeded with nothing to return"
    /// (<see cref="TikCommandEmptyResponseException"/>), which is how every write answers.
    /// </remarks>
    internal static class TikCommandResult
    {
        /// <summary>True for the read verbs, <c>print</c>, <c>get</c> and <c>getall</c>.</summary>
        internal static bool IsReadVerb(string verb)
        {
            switch (verb)
            {
                case "print":
                case "get":
                case "getall":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The scalar of a command whose answer carried no value, decided in this order: an <c>add</c> throws
        /// <see cref="TikAddIdNotReadException"/> (the row is very likely on the router, and a default would hide it);
        /// with <paramref name="allowDefault"/> the result is <paramref name="defaultValue"/>; a read verb throws
        /// <see cref="TikNoSuchItemException"/>; any other verb throws <see cref="TikCommandEmptyResponseException"/>.
        /// </summary>
        /// <param name="command">The command that was run.</param>
        /// <param name="verb">The command's verb.</param>
        /// <param name="allowDefault">True for the <c>ExecuteScalarOrDefault</c> family.</param>
        /// <param name="defaultValue">The caller's default.</param>
        /// <param name="evidence">What the transport saw, completing "'verb' …" (for example "answered !done without
        /// a =ret= value").</param>
        internal static string? EmptyScalar(ITikCommand command, string verb, bool allowDefault, string? defaultValue,
            string evidence)
        {
            if (verb == "add")
                throw TikAddIdNotRead.Create(command, null);
            if (allowDefault)
                return defaultValue;
            if (IsReadVerb(verb))
                throw new TikNoSuchItemException(command);
            throw new TikCommandEmptyResponseException(command, NoScalarMessage($"'{verb}' {evidence}"));
        }

        /// <summary>
        /// The scalar of a raw command that printed nothing: <paramref name="defaultValue"/> with
        /// <paramref name="allowDefault"/>, otherwise <see cref="TikCommandEmptyResponseException"/>. A raw command has
        /// no verb model, so an empty answer is never "no such item".
        /// </summary>
        internal static string? EmptyRawScalar(ITikCommand command, bool allowDefault, string? defaultValue)
        {
            if (allowDefault)
                return defaultValue;
            throw new TikCommandEmptyResponseException(command, NoScalarMessage("The raw command returned no output"));
        }

        /// <summary>
        /// The scalar of a row read by field name: the value of <paramref name="field"/>. When the row lacks it, the
        /// result is <paramref name="defaultValue"/> with <paramref name="allowDefault"/>, otherwise
        /// <see cref="TikSentenceException"/> carrying the row.
        /// </summary>
        internal static string? FieldOfRow(ITikReSentence row, string field, bool allowDefault, string? defaultValue)
        {
            if (row.TryGetResponseField(field, out var value))
                return value;
            if (allowDefault)
                return defaultValue;
            throw new TikSentenceException($"Field '{field}' is not in the row the router returned.", row);
        }

        /// <summary>
        /// The one row of a single-row read: none is <see cref="TikNoSuchItemException"/> (or null without
        /// <paramref name="throwIfMissing"/>), more than one is <see cref="TikCommandAmbiguousResultException"/>.
        /// </summary>
        internal static T? SingleRow<T>(ITikCommand command, IList<T> rows, bool throwIfMissing)
            where T : class
        {
            if (rows.Count == 0)
            {
                if (throwIfMissing)
                    throw new TikNoSuchItemException(command);
                return null;
            }
            if (rows.Count > 1)
                throw new TikCommandAmbiguousResultException(command, rows.Count);
            return rows[0];
        }

        private static string NoScalarMessage(string evidence)
            => evidence + ", so there is no scalar value to return. "
                + "Commands that return nothing (set/unset/remove/enable/comment/…) succeed this way — "
                + "run them with ExecuteNonQuery(), or use ExecuteScalarOrDefault() when the value is optional.";
    }
}
