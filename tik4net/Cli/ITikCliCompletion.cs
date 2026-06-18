using System;
using System.Collections.Generic;

namespace tik4net.Cli
{
    /// <summary>
    /// Terminal Tab-completion probe, implemented by the PTY/CLI transports (Telnet, MAC-Telnet,
    /// WinBox CLI, WinBox CLI-MAC). Drives RouterOS's interactive completion to enumerate, for a given
    /// partial command line, what the router would offer next:
    /// <list type="bullet">
    ///   <item>after a <b>menu path</b> (<c>/interface </c>) — the child menus and command verbs;</item>
    ///   <item>after <c>add </c> or <c>set </c> in a menu (<c>/interface/vlan add </c>) — the
    ///         <b>settable parameter names</b> (the writable field set, the gold source for entity generation).</item>
    /// </list>
    /// This is the scriptable way to "walk the whole RouterOS menu tree" / resolve an entity's fields from a
    /// live router, beyond what <c>print</c> exposes (it lists parameters that have no value on any current row).
    ///
    /// A connection that does <b>not</b> implement this interface does not support completion (fail-closed):
    /// only the CLI terminal transports do — the binary API, REST and WinBox-native have no terminal to drive.
    /// </summary>
    public interface ITikCliCompletion
    {
        /// <summary>
        /// Returns the completion tokens RouterOS offers for <paramref name="partialInput"/> (deduplicated,
        /// echo/prompt removed). For a menu path these are child menus + verbs; after <c>add </c>/<c>set </c>
        /// they are the settable parameter names. Empty when the input completes to a single unique token
        /// (RouterOS completes it inline rather than listing) or has no completions.
        /// </summary>
        /// <param name="partialInput">The partial command line to complete, e.g. <c>/interface/vlan add </c>
        /// (include the trailing space to list the next word — exactly as you would type before pressing Tab).</param>
        IReadOnlyList<string> CompleteCli(string partialInput);

        /// <summary>
        /// As <see cref="CompleteCli"/> but returns the raw (ANSI-stripped) listing text the router printed,
        /// for callers that want to format or inspect the columns themselves.
        /// </summary>
        string CompleteCliRaw(string partialInput);
    }

    /// <summary>
    /// Pure parsing of a RouterOS Tab-completion terminal reaction into tokens. Kept separate from the
    /// transport so it is unit-testable without a router.
    /// </summary>
    internal static class CliCompletionParser
    {
        /// <summary>
        /// Cleans the ANSI-stripped completion reaction: drops the echoed input, the prompt-redraw line(s)
        /// and blank lines, leaving the column listing rows joined by newlines.
        /// </summary>
        public static string Clean(string ansiStripped, string partialInput)
        {
            if (string.IsNullOrEmpty(ansiStripped))
                return string.Empty;

            string stem = (partialInput ?? string.Empty).Trim();
            var kept = new List<string>();
            foreach (var rawLine in ansiStripped.Split('\r', '\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                // Prompt / redraw line (e.g. "[admin@MikroTik] > /interface vlan add ").
                if (line.Contains(RouterOsCliLogin.PromptSuffix))
                    continue;
                // The bare echo of what we typed.
                if (stem.Length > 0 && (line == stem || line.EndsWith(stem, StringComparison.Ordinal)))
                    continue;
                kept.Add(line);
            }
            return string.Join("\n", kept);
        }

        /// <summary>
        /// Splits the cleaned listing into deduplicated completion tokens. RouterOS prints completions in
        /// space-padded columns, so any run of whitespace separates tokens. The echoed input and prompt
        /// redraw are already stripped by <see cref="Clean"/>, so every remaining word is a real completion —
        /// note a parameter may legitimately share a name with a path segment (e.g. <c>interface</c> is a
        /// settable parameter of <c>/interface/vlan add </c>), so the typed words are NOT used to filter.
        /// </summary>
        public static IReadOnlyList<string> Tokens(string ansiStripped, string partialInput)
        {
            string cleaned = Clean(ansiStripped, partialInput);
            if (cleaned.Length == 0)
                return Array.Empty<string>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var token in cleaned.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(token))
                    result.Add(token);
            }
            return result;
        }
    }
}
