using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>One Tab's reaction as both <see cref="ITikCliCompletion"/> readings: the listing and the completed line.</summary>
    internal interface ICliCompletionReaction
    {
        (IReadOnlyList<string> Tokens, string Raw) CompleteCliBoth(string partialInput);
    }

    /// <summary>
    /// A <see cref="TikMenuSchema"/> from Tab completion, for a router without <c>/console/inspect</c> (RouterOS 6).
    /// </summary>
    /// <remarks>
    /// One Tab is not quite one list (Docs/findings-cli.md §14): a long listing elides a family as <c>stem-...</c>, and a
    /// prefix all candidates share is completed inline instead of listed. So a list is walked: the bare Tab, then every
    /// <c>stem-...</c> by its stem. A listing of names is not cut (6.49.13 lists all 103 fields of <c>/interface
    /// wireless print where</c> in one Tab), so no Tab per initial is asked: each Tab costs a settle window of its own.
    /// </remarks>
    internal static class CliCompletionSchemaReader
    {
        internal static TikMenuSchema Read(ITikCliCompletion completion, string path)
        {
            string menu = "/" + string.Join(" ", TikMenuSchemaPath.Segments(path));

            var commands = Walk(completion, menu + " ");
            if (commands.Count == 0)
                throw new TikNoSuchCommandException(((ITikConnection)completion).CreateCommand(path),
                    new TikTrapSentenceResult("no such menu " + path + " (Tab completion lists nothing for it)"));

            return new TikMenuSchema(path, TikMenuSchemaSource.CliCompletion, commands,
                () => commands.Contains("add") ? Walk(completion, menu + " add ") : null,
                () => commands.Contains("set") ? Walk(completion, menu + " set ") : null,
                // Always asked: 6.49.13 leaves 'get' out of a menu's Tab listing (/ip route lists add … unset, no get)
                // and still completes 'get value-name=' with every field. Nothing listed means it cannot say.
                // Merged with 'print where ', which lists the same fields unelided and with the flags: 'get value-name='
                // elides 'published...' on /ip arp, and 6.49.13 filters on 'published' and on /certificate 'trusted'.
                () =>
                {
                    var fields = Walk(completion, menu + " get value-name=")
                        .Concat(Walk(completion, menu + " print where "))
                        .Where(f => !f.StartsWith(".", StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal).ToList();
                    return fields.Count > 0 ? fields : null;
                },
                (verb, argument) => Walk(completion, menu + " " + verb + " " + argument + "="));
        }

        /// <summary>Every candidate after <paramref name="line"/>, the walk described on the class.</summary>
        internal static List<string> Walk(ITikCliCompletion completion, string line)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string token)
            {
                if (token.Length > 0 && seen.Add(token))
                    found.Add(token);
            }

            foreach (var token in OneLevel(completion, line))
                Add(token);
            return found;
        }

        // One Tab after 'line': its listing with every 'stem-...' expanded, or the one candidate an inline completion
        // wrote. A listed token already carries the typed prefix (never prepend it); one that does not has walked into
        // the next parameter and is dropped (ref: the completion traps in findings-cli §14).
        private static List<string> OneLevel(ITikCliCompletion completion, string line, int depth = 0)
        {
            var result = new List<string>();
            if (depth > 4)
                return result;                                       // a stem that keeps answering itself
            string typed = Word(line);
            var (tokens, raw) = Complete(completion, line);
            if (tokens.Count == 0)
            {
                if (raw.Length <= line.Length || !raw.StartsWith(line, StringComparison.Ordinal))
                    return result;                                   // nothing to complete
                if (raw.EndsWith(" ", StringComparison.Ordinal) || raw.EndsWith("=", StringComparison.Ordinal))
                    result.Add(Word(raw.TrimEnd(' ', '=')));         // the one candidate, completed
                else
                    result.AddRange(OneLevel(completion, raw, depth + 1));   // the prefix several share: ask again
                return result;
            }

            string before = line.Substring(0, line.Length - typed.Length);
            foreach (var token in tokens)
            {
                if (!token.StartsWith(typed, StringComparison.Ordinal))
                    continue;
                if (token.EndsWith("...", StringComparison.Ordinal))
                {
                    string stem = token.Substring(0, token.Length - 3);
                    var expanded = OneLevel(completion, before + stem, depth + 1);
                    // A stem that is itself a word ('published...' for published and published2, 6.49.13) is accepted
                    // whole by the Tab, which then moves on to the next parameter: nothing comes back, and the stem is
                    // the one candidate learnt. A stem ending in '-' is only a prefix.
                    if (expanded.Count == 0 && !stem.EndsWith("-", StringComparison.Ordinal))
                        expanded.Add(stem);
                    result.AddRange(expanded);
                }
                else
                    result.Add(token);
            }
            return result;
        }

        // One Tab read both ways; a completion that cannot hand over its reaction is asked twice, for the listing and
        // for the completed line.
        private static (IReadOnlyList<string> Tokens, string Raw) Complete(ITikCliCompletion completion, string line)
        {
            if (completion is ICliCompletionReaction reaction)
                return reaction.CompleteCliBoth(line);
            var tokens = completion.CompleteCli(line);
            return (tokens, tokens.Count == 0 ? completion.CompleteCliRaw(line) : "");
        }

        // The partly typed word at the end of 'line' ("" after a space or '=').
        private static string Word(string line)
        {
            int start = line.LastIndexOfAny(new[] { ' ', '=' }) + 1;
            return line.Substring(start);
        }
    }
}
