using System;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Maps RouterOS CLI error text to the appropriate tik4net exception types.
    /// </summary>
    internal static class CliErrorParser
    {
        /// <summary>
        /// Inspects <paramref name="output"/> (and optional <paramref name="exitCode"/>) for known
        /// RouterOS error patterns and throws the corresponding tik4net exception if one is found.
        /// Does nothing if the output appears to be a successful response.
        /// </summary>
        /// <param name="output">Raw CLI output text (after ANSI stripping).</param>
        /// <param name="cmd">The command that produced the output — used to construct exceptions.</param>
        /// <param name="exitCode">Optional SSH exit code. Non-zero signals an error even if the output text is ambiguous.</param>
        /// <param name="silentOnSuccess">
        /// Set for a command RouterOS answers with <b>nothing at all</b> when it succeeds — see
        /// <see cref="IsSilentOnSuccessVerb"/>. Any leftover text is then an error by position, with no
        /// phrase list involved.
        /// </param>
        internal static void ThrowIfError(string output, ITikCommand cmd, int? exitCode = null,
                                          bool silentOnSuccess = false)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                // Non-zero exit with no output → generic trap
                if (exitCode.HasValue && exitCode.Value != 0)
                    throw new TikCommandTrapException(cmd, new TikTrapSentenceResult($"CLI command failed with exit code {exitCode.Value}."));
                return;
            }

            // RouterOS error lines typically start with a known prefix or contain known substrings.
            // Check is case-insensitive. The specific-kind classification (no such item/command/already have)
            // is shared with the API and REST transports via TikTrapClassifier.
            string lower = output.ToLowerInvariant();

            switch (TikTrapClassifier.Classify(output))
            {
                case TikTrapKind.NoSuchItem:
                    throw new TikNoSuchItemException(cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));
                case TikTrapKind.NoSuchCommand:
                    throw new TikNoSuchCommandException(cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));
                case TikTrapKind.AlreadyHaveSuchItem:
                    throw new TikAlreadyHaveSuchItemException(cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));
            }

            // Generic "failure:" or "error:" prefix
            if (lower.Contains("failure:") || lower.Contains("error:"))
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(ExtractErrorLine(output)));

            // Non-zero exit code with any non-empty output → treat as trap
            if (exitCode.HasValue && exitCode.Value != 0)
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(output.Trim()));

            // Positional detection (P2.12). The CLI has no structural error channel — output and errors are
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
        /// True for verbs RouterOS answers with no output at all when they succeed, so that any remaining
        /// text can be treated as an error without matching it against a phrase list.
        /// </summary>
        /// <remarks>
        /// Each was confirmed against a live RouterOS 7.23.2 rather than assumed. Deliberately excluded:
        /// <c>add</c> prints the new <c>.id</c> (and is read by <c>ExtractAddId</c> on its own call site),
        /// <c>run</c> returns whatever the script printed, and <c>print</c>/monitor verbs exist to produce
        /// output. Anything not listed keeps the phrase-matching behaviour — a verb has to be *verified*
        /// silent before it is added here, because a wrong entry turns ordinary output into a thrown error.
        /// </remarks>
        internal static bool IsSilentOnSuccessVerb(string verb)
        {
            switch (verb)
            {
                case "set":
                case "remove":
                case "enable":
                case "disable":
                case "move":
                case "unset":
                case "comment":
                    return true;
                default:
                    return false;
            }
        }

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
