namespace tik4net.Connection
{
    /// <summary>
    /// Small helpers for slicing RouterOS command paths (<c>/interface/ethernet/print</c>) into their verb
    /// and parent segments. The single home for that slicing — rather than a copy per transport — so the
    /// leading/trailing-slash and empty-path edge cases are handled in exactly one place.
    /// </summary>
    internal static class TikPath
    {
        /// <summary>
        /// The last (verb) segment of a command path, lower-cased: <c>/interface/set</c> → <c>set</c>.
        /// An empty/blank path, or <c>/</c>, defaults to <c>print</c> (the implicit read verb).
        /// </summary>
        internal static string Verb(string commandText)
        {
            var segments = Segments(commandText);
            return segments.Length == 0 ? "print" : segments[segments.Length - 1].ToLowerInvariant();
        }

        /// <summary>
        /// The segments of a path, without the slashes at either end: <c>/ip/route/</c> → <c>ip</c>, <c>route</c>.
        /// A blank path, or <c>/</c>, has none.
        /// </summary>
        internal static string[] Segments(string path)
        {
            string p = Normalize(path).TrimStart('/');
            return p.Length == 0 ? new string[0] : p.Split('/');
        }

        /// <summary>
        /// The path with its last (verb) segment removed: <c>/interface/set</c> → <c>/interface</c>.
        /// A path of one segment has the root for its parent (<c>/ip</c> → <c>/</c>, <c>/ping</c> → <c>/</c>), and so
        /// has the root itself and a blank path: <c>/</c> is its own parent, the only path that is, which is what
        /// ends a walk up the tree.
        /// </summary>
        internal static string Parent(string commandText)
        {
            string p = Normalize(commandText);
            int lastSlash = p.LastIndexOf('/');
            return lastSlash > 0 ? p.Substring(0, lastSlash) : Root;
        }

        /// <summary>The root menu, <c>/</c>: what <see cref="Parent"/> gives for a path of one segment or none.</summary>
        internal const string Root = "/";

        /// <summary>
        /// A segment appended to a menu path, with one slash between them however the menu ends:
        /// <c>/ip/route</c> + <c>print</c> → <c>/ip/route/print</c>, <c>/</c> + <c>ping</c> → <c>/ping</c>.
        /// </summary>
        internal static string Child(string menu, string segment)
            => Normalize(menu) + "/" + segment;

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
