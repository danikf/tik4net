using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// One Tab's reaction as both <see cref="ITikCliCompletion"/> readings — the listing, each word with the colour it was
    /// drawn in, and the completed line, which keeps the blank the router writes after a whole word.
    /// </summary>
    internal interface ICliCompletionReaction
    {
        (IReadOnlyList<CliCompletionItem> Items, string Raw) CompleteCliBoth(string partialInput);
    }

    /// <summary>
    /// A <see cref="TikMenuSchema"/> from Tab completion, for a router without <c>/console/inspect</c> (RouterOS 6).
    /// </summary>
    /// <remarks>
    /// One Tab is not quite one list (Docs/findings-cli.md §14): a long listing elides a family as <c>stem-...</c>, and a
    /// prefix all candidates share is completed inline instead of listed. So a list is walked: the bare Tab, then every
    /// <c>stem-...</c> by its stem. A listing of names is not cut (6.49.13 lists all 103 fields of <c>/interface
    /// wireless print where</c> in one Tab), so no Tab per initial is asked: each Tab costs a settle window of its own.
    /// <para>What a listed word is — sub-menu, command, argument — is in its colour (cyan, magenta, green), which the
    /// colour terminals (WinBox CLI, MAC-Telnet) show and Telnet and SSH do not (they log in with <c>+c</c>). Without it a
    /// word is told by a Tab on itself: a sub-menu lists <c>..</c>, a command its arguments.</para>
    /// </remarks>
    internal static class CliCompletionSchemaReader
    {
        internal static TikMenuSchema Read(ITikCliCompletion completion, string path)
        {
            string menu = "/" + string.Join(" ", TikMenuSchemaPath.Segments(path));

            // Every word of the menu, sub-menus and commands alike; '..' (the parent) is not one of them.
            var entries = WalkItems(completion, menu + " ").Where(i => i.Name != "..").ToList();
            if (entries.Count == 0)
                throw new TikNoSuchCommandException(((ITikConnection)completion).CreateCommand(path),
                    new TikTrapSentenceResult("no such menu " + path + " (Tab completion lists nothing for it)"));
            var names = new HashSet<string>(entries.Select(e => e.Name), StringComparer.Ordinal);

            return new TikMenuSchema(path, TikMenuSchemaSource.CliCompletion,
                () =>
                {
                    var submenus = entries.Where(e => IsSubmenu(completion, menu, e)).Select(e => e.Name).ToList();
                    var commands = entries.Select(e => e.Name).Where(n => !submenus.Contains(n)).ToList();
                    return (commands, submenus);
                },
                verb => names.Contains(verb)
                    ? WalkItems(completion, menu + " " + verb + " ")
                        .Where(i => i.Kind != CliCompletionKind.Submenu && i.Kind != CliCompletionKind.Command)
                        .Select(i => i.Name).ToList()
                    : null,
                // Always asked: a menu listing names 'get' only on the second Tab, and 'get value-name=' completes every
                // field. Nothing listed means it cannot say. Merged with 'print where ', which lists the same fields
                // unelided and with the flags: 'get value-name=' elides 'published...' on /ip arp, and 6.49.13 filters on
                // 'published' and on /certificate 'trusted'.
                () =>
                {
                    var fields = Walk(completion, menu + " get value-name=")
                        .Concat(Walk(completion, menu + " print where "))
                        .Where(f => !f.StartsWith(".", StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal).ToList();
                    return fields.Count > 0 ? fields : null;
                },
                () =>
                {
                    if (!names.Contains("unset"))
                        return null;
                    var fields = Walk(completion, menu + " unset value-name=");
                    return fields.Count > 0 ? fields : null;
                },
                (verb, argument) => Walk(completion, menu + " " + verb + " " + argument + "="));
        }

        // A sub-menu by its colour, or — drawn without one — by a Tab on itself: a sub-menu's listing has '..'.
        private static bool IsSubmenu(ITikCliCompletion completion, string menu, CliCompletionItem entry)
        {
            if (entry.Kind != CliCompletionKind.Unknown)
                return entry.Kind == CliCompletionKind.Submenu;
            return WalkItems(completion, menu + " " + entry.Name + " ").Any(i => i.Name == "..");
        }

        /// <summary>Every candidate after <paramref name="line"/>, the walk described on the class.</summary>
        internal static List<string> Walk(ITikCliCompletion completion, string line)
            => WalkItems(completion, line).Select(i => i.Name).ToList();

        /// <summary><see cref="Walk"/>, each word with what its colour says it is.</summary>
        internal static List<CliCompletionItem> WalkItems(ITikCliCompletion completion, string line)
        {
            var found = new List<CliCompletionItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in OneLevel(completion, line))
                if (item.Name.Length > 0 && seen.Add(item.Name))
                    found.Add(item);
            return found;
        }

        // One Tab after 'line': its listing with every 'stem-...' expanded, or the one candidate an inline completion
        // wrote. A listed token already carries the typed prefix (never prepend it); one that does not has walked into
        // the next parameter and is dropped (ref: the completion traps in findings-cli §14).
        private static List<CliCompletionItem> OneLevel(ITikCliCompletion completion, string line, int depth = 0)
        {
            var result = new List<CliCompletionItem>();
            if (depth > 4)
                return result;                                       // a stem that keeps answering itself
            string typed = Word(line);
            var (items, raw) = Complete(completion, line);
            if (items.Count == 0)
            {
                if (raw.Length <= line.Length || !raw.StartsWith(line, StringComparison.Ordinal))
                    return result;                                   // nothing to complete
                if (raw.EndsWith(" ", StringComparison.Ordinal) || raw.EndsWith("=", StringComparison.Ordinal))
                    result.Add(new CliCompletionItem(Word(raw.TrimEnd(' ', '=')), CliCompletionKind.Unknown));   // the one candidate
                else
                    result.AddRange(OneLevel(completion, raw, depth + 1));   // the prefix several share: ask again
                return result;
            }

            string before = line.Substring(0, line.Length - typed.Length);
            foreach (var item in items)
            {
                string token = item.Name;
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
                        expanded.Add(new CliCompletionItem(stem, item.Kind));
                    result.AddRange(expanded);
                }
                else
                    result.Add(item);
            }
            return result;
        }

        // One Tab read both ways; a completion that cannot hand over its reaction is asked twice, for the listing and
        // for the completed line, and says nothing about colour.
        private static (IReadOnlyList<CliCompletionItem> Items, string Raw) Complete(ITikCliCompletion completion, string line)
        {
            if (completion is ICliCompletionReaction reaction)
                return reaction.CompleteCliBoth(line);
            var tokens = completion.CompleteCli(line);
            return (tokens.Select(t => new CliCompletionItem(t, CliCompletionKind.Unknown)).ToList(),
                tokens.Count == 0 ? completion.CompleteCliRaw(line) : "");
        }

        // The partly typed word at the end of 'line' ("" after a space or '=').
        private static string Word(string line)
        {
            int start = line.LastIndexOfAny(new[] { ' ', '=' }) + 1;
            return line.Substring(start);
        }
    }
}
