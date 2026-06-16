namespace tik4net.Connection
{
    /// <summary>
    /// Small helpers for slicing RouterOS command paths (<c>/interface/ethernet/print</c>) into their verb
    /// and parent segments. Consolidates the per-transport copies that used to live as <c>GetVerb</c>,
    /// <c>VerbOf</c>, <c>StripVerb</c> and <c>StripLastSegment</c> (R8) so the leading/trailing-slash and
    /// empty-path edge cases are handled in exactly one place.
    /// </summary>
    internal static class TikPath
    {
        /// <summary>
        /// The last (verb) segment of a command path, lower-cased: <c>/interface/set</c> → <c>set</c>.
        /// An empty/blank path defaults to <c>print</c> (the implicit read verb).
        /// </summary>
        internal static string Verb(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                return "print";
            var segments = commandText.Trim().TrimStart('/').Split('/');
            return segments[segments.Length - 1].ToLowerInvariant();
        }

        /// <summary>
        /// The path with its last (verb) segment removed: <c>/interface/set</c> → <c>/interface</c>.
        /// A blank path yields the empty string.
        /// </summary>
        internal static string Parent(string commandText)
        {
            string p = Normalize(commandText);
            if (p.Length == 0)
                return "";
            int lastSlash = p.LastIndexOf('/');
            return lastSlash > 0 ? p.Substring(0, lastSlash) : p;
        }

        /// <summary>
        /// Trims surrounding whitespace, ensures a leading slash and drops any trailing slash:
        /// <c> interface/print/ </c> → <c>/interface/print</c>. A blank path yields the empty string.
        /// </summary>
        internal static string Normalize(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
                return "";
            string p = commandText.Trim();
            if (!p.StartsWith("/"))
                p = "/" + p;
            return p.TrimEnd('/');
        }
    }
}
