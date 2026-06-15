using System;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// Inspects the terminal reaction to the Safe Mode <c>Ctrl+X</c> control key and surfaces failures
    /// as tik4net exceptions. The happy path prints <c>[Safe Mode taken]</c> and switches the prompt to the
    /// <c>&lt;SAFE&gt;</c> form; the notable failure is a conflict when another session already owns safe mode,
    /// where RouterOS asks an interactive question instead of entering safe mode.
    /// </summary>
    internal static class CliSafeModeParser
    {
        internal static void ThrowIfTakeFailed(string output, ITikCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(output))
                return;

            string lower = output.ToLowerInvariant();

            // Another session holds safe mode → RouterOS prompts e.g.
            //   "safe mode is taken by someone else, [u]ndo,[r]elease,[d]on't take – which one?"
            // We do not auto-answer (any choice has side effects on the other session); report it instead.
            if (lower.Contains("which one")
                || (lower.Contains("safe mode") && lower.Contains("taken"))
                || lower.Contains("[d]on't take"))
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(
                    "Safe mode is already held by another session — RouterOS would not grant it. " +
                    "Output: " + output.Trim()));

            // Fall back to the generic CLI error patterns (syntax/permission/etc.).
            CliErrorParser.ThrowIfError(output, cmd);
        }
    }
}
