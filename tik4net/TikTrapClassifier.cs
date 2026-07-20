namespace tik4net
{
    /// <summary>
    /// Well-known outcomes a RouterOS trap/error message classifies into, shared across every transport.
    /// </summary>
    internal enum TikTrapKind
    {
        /// <summary>No specific pattern matched — a generic <see cref="TikCommandTrapException"/> applies.</summary>
        Generic,
        /// <summary>Bad path/verb/syntax ('no such command' from the API; CLI/REST equivalents).</summary>
        NoSuchCommand,
        /// <summary>Record not found ('no such item' from the API; CLI/REST equivalents).</summary>
        NoSuchItem,
        /// <summary>Duplicate item ('already have such ...' from the API; CLI/REST equivalents).</summary>
        AlreadyHaveSuchItem,
    }

    /// <summary>
    /// Classifies RouterOS trap/error message text into one of the well-known outcomes. Each transport
    /// (binary API, CLI terminal, REST) speaks a different dialect of the same underlying router error —
    /// this holds the union of phrases observed across all three, so the four outcome exception types
    /// (<see cref="TikNoSuchCommandException"/>, <see cref="TikNoSuchItemException"/>,
    /// <see cref="TikAlreadyHaveSuchItemException"/>, <see cref="TikCommandTrapException"/>) stay in sync
    /// instead of drifting per transport. Each transport is responsible for extracting its own message text
    /// (and any transport-specific signal, e.g. REST's HTTP 404) and constructing the concrete exception —
    /// this only decides which kind applies.
    /// </summary>
    internal static class TikTrapClassifier
    {
        /// <summary>Classifies <paramref name="message"/> (case-insensitive) into a <see cref="TikTrapKind"/>.</summary>
        public static TikTrapKind Classify(string message)
        {
            if (string.IsNullOrEmpty(message))
                return TikTrapKind.Generic;

            string lower = message.ToLowerInvariant();

            // Record not found: API "no such item"; CLI "expected item id" (e.g. '[find .id=…]' resolves to
            // nothing); REST "missing or invalid resource identifier".
            if (lower.Contains("no such item") || lower.Contains("expected item id")
                || lower.Contains("missing or invalid resource identifier"))
                return TikTrapKind.NoSuchItem;

            // Bad path/verb/syntax: API "no such command"; CLI "bad command name" / "expected end of
            // command" / "no such directory" / "syntax error"; REST "no such directory".
            if (lower.Contains("no such command") || lower.Contains("bad command name")
                || lower.Contains("expected end of command") || lower.Contains("no such directory")
                || lower.Contains("syntax error"))
                return TikTrapKind.NoSuchCommand;

            // Duplicate item: API "failure: already have ... such ..."; CLI/REST "already have such item" /
            // "item with such name already exists". Word order differs between the two phrasings, so both
            // are checked independently rather than as one substring.
            if ((lower.Contains("already have") && lower.Contains("such"))
                || lower.Contains("item with such name already"))
                return TikTrapKind.AlreadyHaveSuchItem;

            return TikTrapKind.Generic;
        }
    }
}
