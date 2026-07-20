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
        internal static void ThrowIfError(string output, ITikCommand cmd, int? exitCode = null)
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
        }

        /// <summary>
        /// Extracts the first non-blank line from <paramref name="output"/> to use as the error message.
        /// </summary>
        private static string ExtractErrorLine(string output)
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
