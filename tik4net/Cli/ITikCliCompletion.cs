using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Cli
{
    /// <summary>
    /// Terminal Tab-completion probe, implemented by the PTY/CLI transports (Telnet, SSH, MAC-Telnet,
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
        /// they are the settable parameter names. Empty when RouterOS completes the word inline rather than
        /// listing — a unique completion (<c>/interface/vl</c> → <c>vlan/</c>), or the prefix every candidate
        /// shares (<c>frame-types=</c> → <c>admit-</c>) — and when there is nothing to complete.
        /// <see cref="CompleteCliRaw"/> tells those apart. Asked again with that prefix, it lists the candidates on
        /// every RouterOS version: RouterOS 6 lists only on a second Tab, which is sent when the first changed nothing.
        /// A listing is followed by one more Tab, which lists what the first left out — a menu's <c>..</c> and
        /// <c>get</c>, a command's less common arguments (<c>/tool ping </c>: <c>interface</c>, <c>vrf</c>) — and its
        /// words are included.
        /// </summary>
        /// <param name="partialInput">The partial command line to complete, e.g. <c>/interface/vlan add </c>
        /// (include the trailing space to list the next word — exactly as you would type before pressing Tab).</param>
        IReadOnlyList<string> CompleteCli(string partialInput);

        /// <summary>
        /// As <see cref="CompleteCli"/> but returns text rather than tokens. When RouterOS lists, it is the
        /// (ANSI-stripped) listing the router printed, both Tabs' rows, for callers that want to format or inspect the
        /// columns themselves. When it completes inline, it is the input line as completed —
        /// <c>/interface/vlan/</c> for <c>/interface/vl</c>, <c>… frame-types=admit-</c> for
        /// <c>… frame-types=</c> — with trailing blanks removed. When there is nothing to complete, it is empty.
        /// Each call is its own Tab round trip, so calling both methods asks the router twice.
        /// </summary>
        string CompleteCliRaw(string partialInput);
    }

    /// <summary>What a completion word is, from the colour the router drew it in.</summary>
    internal enum CliCompletionKind
    {
        /// <summary>Drawn without a colour that says (Telnet and SSH log in with <c>+c</c>).</summary>
        Unknown,
        /// <summary>Cyan: a sub-menu, <c>..</c> included.</summary>
        Submenu,
        /// <summary>Magenta: a command.</summary>
        Command,
        /// <summary>Green: an argument or a field.</summary>
        Argument,
    }

    /// <summary>One completion word and what it is.</summary>
    internal readonly struct CliCompletionItem
    {
        internal CliCompletionItem(string name, CliCompletionKind kind)
        {
            Name = name;
            Kind = kind;
        }

        internal string Name { get; }
        internal CliCompletionKind Kind { get; }
    }

    /// <summary>
    /// Pure parsing of a RouterOS Tab-completion terminal reaction into tokens. Kept separate from the
    /// transport so it is unit-testable without a router.
    /// </summary>
    /// <remarks>
    /// The reaction is taken with its escape sequences, because what RouterOS does to the input line is
    /// written in them. An inline completion rewrites the word in place — cursor-left n, n blanks, cursor-left
    /// n, the completed word (backspace-blank-backspace for a one-letter word) — and the colour terminals
    /// (WinBox CLI, MAC-Telnet) then repaint the whole line with syntax highlighting, either from the prompt
    /// after a carriage return or by moving back over the word. Stripped of its escapes, that is the echo, the
    /// word and a second copy of the line run together; replayed onto a line, it is the completed line.
    /// So the reaction is first replayed (<see cref="Render"/>), and everything else reads the screen.
    /// </remarks>
    internal static class CliCompletionParser
    {
        /// <summary>
        /// A settle read that has seen nothing at all for this many quiet windows answers empty instead of waiting for
        /// the receive deadline: RouterOS writes nothing for a Tab with nothing more to list (the Tab after a listing that
        /// left nothing out, 6.49.13's second Tab on <c>print where </c> of a menu without fields). A Tab that follows
        /// typed text is never silent — its echo comes first. The quiet window itself follows the link: twice the
        /// connection's measured Tab response, never under 300 ms (<c>CliConnectionBase.SettleTab</c>).
        /// </summary>
        internal const int NoReactionQuietFactor = 4;

        /// <summary>
        /// Cleans the completion reaction. For a <b>listing</b> it drops the echoed input, the prompt-redraw
        /// line(s) and blank lines, leaving the column listing rows joined by newlines. For an <b>inline
        /// completion</b> it returns the input line as RouterOS completed it, without the prompt and trailing
        /// blanks; and for a reaction that changed nothing, an empty string.
        /// </summary>
        /// <param name="reaction">The terminal's reaction to <c>&lt;partialInput&gt;&lt;Tab&gt;</c>, escape
        /// sequences included (text already stripped of them parses too, but loses the cursor moves).</param>
        /// <param name="partialInput">What was typed before the Tab.</param>
        /// <param name="keepWordEnd">Keep the one blank after an inline completion: the router writes it when the word
        /// is whole (<c>value-name=name </c>), and not after a prefix several candidates share (<c>src-</c>).</param>
        public static string Clean(string reaction, string partialInput, bool keepWordEnd = false)
        {
            if (string.IsNullOrEmpty(reaction))
                return string.Empty;
            string screen = Render(reaction);
            return IsListing(screen)
                ? CleanListing(screen, partialInput)
                : CompletedLine(screen, partialInput ?? string.Empty, keepWordEnd);
        }

        /// <summary>
        /// Splits a listing into deduplicated completion tokens. RouterOS prints completions in space-padded
        /// columns, so any run of whitespace separates tokens. The echoed input and prompt redraw are already
        /// stripped by <see cref="Clean"/>, so every remaining word is a real completion — note a parameter
        /// may legitimately share a name with a path segment (e.g. <c>interface</c> is a settable parameter of
        /// <c>/interface/vlan add </c>), so the typed words are NOT used to filter. An inline completion
        /// yields no tokens.
        /// </summary>
        public static IReadOnlyList<string> Tokens(string reaction, string partialInput)
        {
            if (string.IsNullOrEmpty(reaction))
                return Array.Empty<string>();
            string screen = Render(reaction);
            if (!IsListing(screen))
                return Array.Empty<string>();
            string cleaned = CleanListing(screen, partialInput);
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

        /// <summary>
        /// <see cref="Tokens"/> with what each one is, read from the colour the router drew it in (findings-cli §14):
        /// cyan a sub-menu, magenta a command, green an argument or field. <see cref="CliCompletionKind.Unknown"/> on a
        /// terminal without colour (Telnet and SSH log in with <c>+c</c>).
        /// </summary>
        internal static IReadOnlyList<CliCompletionItem> Items(string reaction, string partialInput)
        {
            var tokens = Tokens(reaction, partialInput);
            if (tokens.Count == 0)
                return Array.Empty<CliCompletionItem>();
            var kinds = KindsByToken(reaction, partialInput);
            return tokens.Select(t => new CliCompletionItem(t, kinds.TryGetValue(t, out var k) ? k : CliCompletionKind.Unknown))
                .ToList();
        }

        // The colour each word of the listing was drawn in; the prompt redraw and the echo are left out.
        private static Dictionary<string, CliCompletionKind> KindsByToken(string reaction, string partialInput)
        {
            var result = new Dictionary<string, CliCompletionKind>(StringComparer.Ordinal);
            var rows = RenderCells(reaction);
            string typed = (partialInput ?? string.Empty).TrimStart();
            bool first = true;
            foreach (var (text, kinds) in rows)
            {
                string line = text.ToString();
                if (line.Trim().Length == 0)
                    continue;
                int start = 0;
                if (first)
                {
                    int lead = line.Length - line.TrimStart().Length;
                    if (typed.Length > 0 && line.Substring(lead).StartsWith(typed, StringComparison.Ordinal))
                        start = lead + typed.Length;   // 7.24 glues the listing to the echo
                    first = false;
                }
                if (RouterOsCliLogin.ContainsPromptSuffix(line))
                    continue;
                for (int i = start; i < line.Length; i++)
                {
                    if (line[i] == ' ')
                        continue;
                    int end = line.IndexOf(' ', i);
                    if (end < 0) end = line.Length;
                    string word = line.Substring(i, end - i);
                    if (!result.ContainsKey(word))
                        result[word] = i < kinds.Count ? kinds[i] : CliCompletionKind.Unknown;
                    i = end;
                }
            }
            return result;
        }

        /// <summary>
        /// Replays a terminal reaction onto a screen and returns its rows joined by <c>\n</c>: printable
        /// characters overwrite at the cursor; CR returns to column 0; LF starts the next row; backspace and
        /// <c>ESC[nD</c> move left, <c>ESC[nC</c> right; <c>ESC[K</c> erases to the end of the row. Colour and
        /// every other sequence (including RouterOS's <c>ESC[9999B</c> "to the bottom") change nothing on the
        /// row and are dropped.
        /// </summary>
        internal static string Render(string reaction)
            => string.Join("\n", RenderCells(reaction).Select(r => r.Text.ToString()));

        // The replay, with the colour of every cell. RouterOS 6 sets a colour once and draws a run of words and rows
        // in it; 7.x sets it before every word. Either way it holds until the next SGR.
        private static List<(StringBuilder Text, List<CliCompletionKind> Kinds)> RenderCells(string reaction)
        {
            var rows = new List<(StringBuilder Text, List<CliCompletionKind> Kinds)> { (new StringBuilder(), new List<CliCompletionKind>()) };
            int row = 0, col = 0;
            var kind = CliCompletionKind.Unknown;
            for (int i = 0; i < reaction.Length; i++)
            {
                char c = reaction[i];
                var (line, kinds) = rows[row];
                switch (c)
                {
                    case '\r':
                        col = 0;
                        continue;
                    case '\n':
                        row++;
                        if (row == rows.Count) rows.Add((new StringBuilder(), new List<CliCompletionKind>()));
                        col = 0;
                        continue;
                    case '\b':
                        if (col > 0) col--;
                        continue;
                    case '\t':
                        c = ' ';
                        break;
                    case '\x1b':
                        i = Escape(reaction, i, line, kinds, ref col, ref kind);
                        continue;
                }
                if (c < ' ')
                    continue;
                while (line.Length < col) { line.Append(' '); kinds.Add(CliCompletionKind.Unknown); }
                if (col < line.Length) { line[col] = c; kinds[col] = kind; }
                else { line.Append(c); kinds.Add(kind); }
                col++;
            }
            return rows;
        }

        // An SGR's colour as a node type (measured on 6.49.13 and 7.24.4): 36 cyan a sub-menu (and '..'), 35 magenta a
        // command, 32 green an argument or field (bold: the unnamed one). A reset, or any other colour, is no type.
        private static CliCompletionKind KindOf(string parameters)
        {
            var kind = CliCompletionKind.Unknown;
            foreach (var p in parameters.Split(';'))
            {
                switch (p)
                {
                    case "36": kind = CliCompletionKind.Submenu; break;
                    case "35": kind = CliCompletionKind.Command; break;
                    case "32": kind = CliCompletionKind.Argument; break;
                    case "1": break;   // bold keeps the colour
                    default: kind = CliCompletionKind.Unknown; break;
                }
            }
            return kind;
        }

        // Applies the escape sequence starting at reaction[start] (an ESC) and returns the index of its last
        // character. CSI: ESC [ params final. OSC: ESC ] … BEL. Anything else: ESC + one character.
        private static int Escape(string reaction, int start, StringBuilder line, List<CliCompletionKind> kinds, ref int col,
            ref CliCompletionKind colour)
        {
            if (start + 1 >= reaction.Length)
                return start;
            char kind = reaction[start + 1];
            if (kind == ']')
            {
                int bel = reaction.IndexOf('\a', start + 2);
                return bel < 0 ? reaction.Length - 1 : bel;
            }
            if (kind != '[')
                return start + 1;

            int i = start + 2;
            while (i < reaction.Length && (char.IsDigit(reaction[i]) || reaction[i] == ';' || reaction[i] == '?'))
                i++;
            if (i >= reaction.Length)
                return reaction.Length - 1;
            string parameters = reaction.Substring(start + 2, i - start - 2);
            int n = int.TryParse(parameters, out int parsed) && parsed > 0 ? parsed : 1;
            switch (reaction[i])
            {
                case 'D': col = Math.Max(0, col - n); break;
                case 'C': col += n; break;
                case 'K':
                    if (parameters.Length == 0 || parameters == "0")
                    {
                        if (col < line.Length) { line.Length = col; kinds.RemoveRange(col, kinds.Count - col); }
                    }
                    else if (parameters == "2")
                    {
                        line.Length = 0;
                        kinds.Clear();
                    }
                    break;
                case 'm':
                    colour = KindOf(parameters);
                    break;
            }
            return i;
        }

        /// <summary>
        /// True when the router LISTED completions rather than completing inline. Measured on 7.19.6 and 7.24.4
        /// over Telnet, SSH, MAC-Telnet and WinBox CLI: a listing moves to new rows (the columns, then the prompt
        /// redrawn with the typed line), while an inline completion — and its colour repaint — stays on the
        /// input line.
        /// </summary>
        private static bool IsListing(string screen) => screen.IndexOf('\n') >= 0;

        // The listing rows of a rendered listing: the echo, the prompt redraw and blank rows dropped.
        private static string CleanListing(string screen, string partialInput)
        {
            string stem = (partialInput ?? string.Empty).Trim();
            string typed = (partialInput ?? string.Empty).TrimStart();
            var kept = new List<string>();
            bool first = true;
            foreach (var rawLine in screen.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                // RouterOS 7.24 prints the listing on the echo's own line, with no break: "/interface 6to4  bonding …".
                // The echo is the typed text exactly, and a listing glued to it starts with a token at once.
                if (first && typed.Length > 0 && line.Length > typed.Length
                    && line.StartsWith(typed, StringComparison.Ordinal) && !char.IsWhiteSpace(line[typed.Length]))
                    line = line.Substring(typed.Length).Trim();
                first = false;
                // Prompt / redraw line (e.g. "[admin@MikroTik] > /interface vlan add "), including the
                // Safe Mode form — completion works inside safe mode too.
                if (RouterOsCliLogin.ContainsPromptSuffix(line))
                    continue;
                // The bare echo of what we typed.
                if (stem.Length > 0 && (line == stem || line.EndsWith(stem, StringComparison.Ordinal)))
                    continue;
                kept.Add(line);
            }
            return string.Join("\n", kept);
        }

        // The single rendered row of an inline completion, less the prompt a colour repaint drew in front of
        // it and trailing blanks; empty when it is still just what was typed.
        private static string CompletedLine(string screen, string partialInput, bool keepWordEnd)
        {
            string line = screen;
            int afterPrompt = RouterOsCliLogin.IndexAfterPromptSuffix(line);
            if (afterPrompt >= 0)
                line = line.Substring(afterPrompt);
            string trimmed = line.TrimEnd();
            if (trimmed == partialInput.TrimEnd())
                return string.Empty;
            return keepWordEnd && trimmed.Length < line.Length ? trimmed + " " : trimmed;
        }
    }
}
