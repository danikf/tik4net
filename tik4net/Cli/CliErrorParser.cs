using System;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Maps RouterOS CLI error text to the appropriate tik4net exception types. The phrases are for an answer that may be
    /// a refusal; a read that shows it ran to its end (a count marker, as-value records, a table header) is data and does
    /// not come here. <c>get</c> and raw commands do, because one line has no shape to tell the two apart.
    /// </summary>
    internal static class CliErrorParser
    {
        /// <summary>
        /// Inspects <paramref name="output"/> for known
        /// RouterOS error patterns and throws the corresponding tik4net exception if one is found.
        /// Does nothing if the output appears to be a successful response.
        /// </summary>
        /// <param name="output">Raw CLI output text (after ANSI stripping).</param>
        /// <param name="cmd">The command that produced the output — used to construct exceptions.</param>
        /// <param name="silentOnSuccess">
        /// Set for a command RouterOS answers with <b>nothing at all</b> when it succeeds — see
        /// <see cref="IsSilentOnSuccessVerb"/>. Any leftover text is then an error by position, with no
        /// phrase list involved.
        /// </param>
        internal static void ThrowIfError(string output, ITikCommand cmd, bool silentOnSuccess = false)
        {
            if (string.IsNullOrWhiteSpace(output))
                return;

            // RouterOS error lines typically start with a known prefix or contain known substrings.
            // Check is case-insensitive. The specific-kind classification (no such item/command/already have)
            // is shared with the API and REST transports via TikTrapClassifier.
            string lower = output.ToLowerInvariant();

            var kind = TikTrapClassifier.Classify(output);
            if (kind != TikTrapKind.Generic)
                throw TikTrapClassifier.ToException(kind, cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));

            // Generic "failure:" or "error:" prefix
            if (lower.Contains("failure:") || lower.Contains("error:"))
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));

            // Positional detection. The CLI has no structural error channel — output and errors are
            // the same text — so phrase matching can never be complete, and every phrase it misses is
            // reported to the caller as SUCCESS. Verified live on 7.23.2: a rejected write prints its
            // diagnostic and nothing else ("value of mtu contains invalid trailing characters",
            // "input does not match any value of interface"), while the same command succeeding prints
            // nothing whatsoever. So on the verbs that are silent when they succeed, any surviving text is
            // an error regardless of how it is worded.
            //
            // This runs last on purpose: the classified kinds above still produce their specific exception
            // types, and only the unrecognised remainder falls through to a generic trap.
            //
            // Safe against the two known sources of legitimate chatter: CliOutputHelper.CleanOutput strips
            // the command echo and both the normal and <SAFE> prompts before this sees the text, and the
            // verb whitelist keeps action verbs that legitimately print (notably 'run', which returns a
            // script's own output) out of this path entirely.
            if (silentOnSuccess && output.Trim().Length > 0)
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));
        }

        /// <summary>
        /// True for verbs RouterOS answers with no output at all when they succeed (<see cref="CliVerbs.IsRowWrite"/>), so that any remaining
        /// text can be treated as an error without matching it against a phrase list.
        /// </summary>
        /// <remarks>
        /// Each was confirmed against a live RouterOS 7.23.2 rather than assumed. Deliberately excluded:
        /// <c>add</c> prints the new <c>.id</c> (and is read by <c>ExtractAddId</c> on its own call site),
        /// <c>run</c> returns whatever the script printed, and <c>print</c>/monitor verbs exist to produce
        /// output. Anything not listed keeps the phrase-matching behaviour — a verb has to be *verified*
        /// silent before it is added here, because a wrong entry turns ordinary output into a thrown error.
        /// </remarks>
        internal static bool IsSilentOnSuccessVerb(string verb) => CliVerbs.IsRowWrite(verb);

        /// <summary>
        /// Reads the verb out of a <b>raw</b> CLI line so that the positional error check can apply there
        /// too, and returns false when the line is anything the check would be unsafe on.
        /// </summary>
        /// <remarks>
        /// The structured path knows its verb because it built the command; raw mode is handed a finished
        /// line, and without a verb it can only phrase-match — which reports every wording the list misses
        /// as SUCCESS. The verb is in the text, so it can be read.
        /// <para>
        /// Deliberately conservative, because the two failure directions are not equal: missing a verb
        /// costs a refusal that keeps being phrase-matched, exactly as before, while a wrong verb turns
        /// legitimate output into a thrown exception. So this recognises only a plain single command —
        /// leading <c>/</c>, and none of <c>[ ] $ ; { }</c>, which is what scripting,
        /// <c>:put [ … ]</c> and command chaining need — and within it only a word that is already a
        /// confirmed <see cref="IsSilentOnSuccessVerb"/>. Everything else returns false and keeps the old
        /// behaviour. Scanning stops at the first argument (a <c>name=value</c> word, a quoted value or a
        /// <c>*id</c> selector) so a value can never be mistaken for a verb.
        /// </para>
        /// </remarks>
        internal static bool TryGetRawSilentVerb(string cliText, out string verb)
        {
            verb = string.Empty;
            if (string.IsNullOrWhiteSpace(cliText))
                return false;

            string text = cliText.Trim();
            if (text[0] != '/')
                return false;                                   // ':put …', ':foreach …', a bare word
            if (text.IndexOfAny(new[] { '[', ']', '$', ';', '{', '}' }) >= 0)
                return false;                                   // scripting or chained commands

            foreach (string word in text.Split(' '))
            {
                if (word.Length == 0)
                    continue;
                if (word.IndexOf('=') >= 0 || word[0] == '"' || word[0] == '*')
                    break;                                      // arguments start here

                // '/interface set' and '/interface/set' are the same command.
                int slash = word.LastIndexOf('/');
                string candidate = slash >= 0 ? word.Substring(slash + 1) : word;
                if (IsSilentOnSuccessVerb(candidate))
                {
                    verb = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A line ending in the parser's position: <c>… (line N column M)</c>.</summary>
        internal static readonly System.Text.RegularExpressions.Regex SyntaxErrorLine =
            new System.Text.RegularExpressions.Regex(@"\(line \d+ column \d+\)$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// The router's refusal of an argument value it cannot resolve — <c>input does not match any value of peer</c>
        /// for the <c>as-value</c> a 6.x <c>/routing bgp advertisements print</c> takes for a peer name. A whole answer
        /// of that one line is the router's error, not a read that lost its count.
        /// </summary>
        internal static readonly System.Text.RegularExpressions.Regex RefusedArgumentLine =
            new System.Text.RegularExpressions.Regex(@"^input does not match any value of [\w-]+$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static System.Collections.Generic.List<string> ContentLines(string? output)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (string line in (output ?? string.Empty).Split((char)10))
                if (line.Trim().Length > 0)
                    lines.Add(line.Trim());
            return lines;
        }

        /// <summary>True when the whole answer is one parse error: <c>… (line N column M)</c>.</summary>
        internal static bool IsSyntaxErrorOnly(string? output)
        {
            var lines = ContentLines(output);
            return lines.Count == 1 && SyntaxErrorLine.IsMatch(lines[0]);
        }

        /// <summary>
        /// Whether a read failed because the menu has no <c>detail</c> modifier — a singleton answers
        /// <c>bad parameter detail (line 1 column 27)</c> and no count marker.
        /// </summary>
        internal static bool IsDetailRefusal(string? response)
            => response != null && response.IndexOf("bad parameter detail", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A one-line refusal of the command's text rather than of what it asks for.</summary>
        internal static bool IsParseRefusal(string message)
            => SyntaxErrorLine.IsMatch(message.Trim()) || RefusedArgumentLine.IsMatch(message.Trim());

        /// <summary>
        /// True when the router refused a window because the menu's <c>print</c> takes no <c>from=</c> —
        /// <c>bad parameter from (line 1 column N)</c>, which is what <c>/log print</c> answers (7.24). Such a
        /// menu cannot be windowed and is read in one command, like one without <c>find</c>.
        /// </summary>
        internal static bool IsPrintWithoutFrom(string? output)
            => (output ?? string.Empty).Trim().StartsWith("bad parameter from (line ", StringComparison.Ordinal);

        /// <summary>
        /// True when RouterOS aborted the command line — what it answers, with no rows, no marker and no error
        /// text, when a window's <c>print from=</c> names an id that vanished after its <c>find</c> (measured on
        /// 7.24 under conntrack churn; Docs/findings-cli.md §1). The word alone on a line, so a field value
        /// cannot be mistaken for it.
        /// </summary>
        internal static bool IsInterruptedAnswer(string? output)
            => ContentLines(output).Contains("interrupted");

        /// <summary>
        /// Throws the router's own complaint when a read's whole answer is one parse error — a line ending in
        /// <c>(line N column M)</c>, such as <c>expected yes or no (line 1 column 62)</c> for a boolean compared
        /// with <c>true</c>, or the refusal of an argument value, such as <c>input does not match any value of peer</c>.
        /// None of the phrases this class knows covers them, and they are the router's error rather than an answer
        /// that lost its count.
        /// </summary>
        /// <remarks>
        /// Called where the answer is already known to lack its closing marker, so a record whose text
        /// happens to end the same way cannot be taken for an error: a real answer always ends with the marker.
        /// The one other caller is the raw text path, which has no marker: there a one-line answer of that shape is
        /// taken for the refusal it almost always is.
        /// </remarks>
        internal static void ThrowIfSyntaxError(string? output, ITikCommand cmd)
        {
            var lines = ContentLines(output);
            if (lines.Count == 1 && (SyntaxErrorLine.IsMatch(lines[0]) || RefusedArgumentLine.IsMatch(lines[0])))
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(lines[0]));
        }

        /// <summary>
        /// Extracts the first non-blank line from <paramref name="output"/> to use as the error message.
        /// </summary>
        internal static string ExtractErrorLine(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return string.Empty;

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    return trimmed;
            }
            return output.Trim();
        }
    }
}
