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
    /// <remarks>
    /// The success message says "taken" too, so "taken" is no sign of a conflict. RouterOS 6.49.13 prints
    /// <c>[Safe Mode taken]</c> into the captured output; reading it as a refusal made the caller walk away from a
    /// session that DID hold Safe Mode (and a WinBox CLI session ending that way wedged the router's console). The
    /// conflict is the question RouterOS asks — see <see cref="IsTakeConflict"/> for the wordings.
    /// </remarks>
    internal static class CliSafeModeParser
    {
        /// <summary>
        /// Whether RouterOS answered the Safe Mode key with its question about another session's Safe Mode, rather
        /// than taking it. The terminal then waits for the answer.
        /// </summary>
        /// <remarks>
        /// Measured: 7.24.4 asks <c>Safe Mode is taken by current user in another session. Unroll, release or abort
        /// [u/r]?</c>, 6.49.13 asks <c>Hijacking Safe Mode from someone - unroll/release/don't take it [u/r/d]:</c>.
        /// Older wordings ("… which one?", "[d]on't take") are kept alongside the measured ones.
        /// </remarks>
        internal static bool IsTakeConflict(string output)
        {
            string lower = (output ?? string.Empty).ToLowerInvariant();
            return lower.Contains("another session")
                || lower.Contains("hijacking safe mode")
                || lower.Contains("[u/r]")
                || lower.Contains("[u/r/d]")
                || lower.Contains("which one")
                || lower.Contains("[d]on't take");
        }

        internal static void ThrowIfTakeFailed(string output, ITikCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(output))
                return;

            // Another session holds safe mode → RouterOS asks what to do with it rather than taking it. The caller
            // has already declined (CliConnectionBase.SafeModeTake); report it.
            if (IsTakeConflict(output))
                throw new TikCommandTrapException(cmd, new TikTrapSentenceResult(
                    "Safe mode is already held by another session — RouterOS would not grant it. " +
                    "Output: " + output.Trim()));

            // Fall back to the generic CLI error patterns (syntax/permission/etc.).
            CliErrorParser.ThrowIfError(output, cmd);
        }
    }
}
