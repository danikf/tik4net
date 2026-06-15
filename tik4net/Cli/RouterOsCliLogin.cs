using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>
    /// Shared RouterOS CLI prompt detection and interactive login sequence for all PTY-based
    /// transports (Telnet, MAC-Telnet, SSH PTY, WinBox mepty terminal). The transport supplies the
    /// raw byte I/O via delegates; this class owns the RouterOS-specific terminal semantics that are
    /// identical regardless of transport:
    /// <list type="bullet">
    ///   <item><c>Login:</c> / <c>Password:</c> prompt handling,</item>
    ///   <item>the "change your password" nag (RouterOS shows <c>new password&gt;</c> for routers with a
    ///         default/empty password) — answered with Ctrl-C to skip,</item>
    ///   <item>login-failure detection,</item>
    ///   <item>shell-prompt detection (<c>] &gt;</c>).</item>
    /// </list>
    /// Keeping this logic here (rather than in a single transport) means Telnet, MAC-Telnet and any
    /// future SSH-PTY transport share one battle-tested login routine.
    /// </summary>
    internal static class RouterOsCliLogin
    {
        /// <summary>
        /// Shell prompt suffix. RouterOS prompt is <c>[user@identity] &gt; </c>; the identity is
        /// arbitrary so we match only the suffix. Compare against ANSI-stripped, right-trimmed text
        /// (note: no trailing space — the line is trimmed before the check).
        /// </summary>
        public const string PromptSuffix = "] >";

        /// <summary>
        /// Shell prompt suffix while RouterOS Safe Mode is active. The prompt gains a <c>&lt;SAFE&gt;</c>
        /// token between the <c>]</c> and the <c>&gt;</c>, e.g. <c>[admin@MikroTik] &lt;SAFE&gt; &gt;</c>.
        /// Recognising it keeps every command's read-until-prompt working once
        /// <see cref="ITikConnection.SafeModeTake"/> has been called.
        /// </summary>
        public const string SafePromptSuffix = "] <SAFE> >";

        /// <summary>
        /// Login name suffix appended to the user name on PTY transports. <c>+c</c> disables ANSI
        /// colour, which drastically reduces escape sequences in the output. We deliberately do NOT
        /// pin a fixed terminal width here — the transport answers RouterOS's cursor-probe negotiation
        /// (see <see cref="Vt100State"/>) advertising a wide terminal so long <c>:put</c> as-value
        /// records are not wrapped. See findings-cli.md §4.
        /// </summary>
        public const string TerminalLoginFlags = "+c";

        /// <summary>Ctrl-C — used to dismiss the change-password nag.</summary>
        private const byte CtrlC = 0x03;

        private const int MaxNagRounds = 3;

        // ── Prompt / state detection (pure, unit-testable) ─────────────────────

        /// <summary>True when the ANSI-stripped text ends with the RouterOS shell prompt, including the
        /// <c>&lt;SAFE&gt;</c> variant shown while Safe Mode is active.</summary>
        public static bool IsShellPrompt(string strippedText)
        {
            if (string.IsNullOrEmpty(strippedText))
                return false;
            string t = strippedText.TrimEnd('\r', '\n', ' ');
            return t.EndsWith(PromptSuffix, StringComparison.Ordinal)
                || t.EndsWith(SafePromptSuffix, StringComparison.Ordinal);
        }

        public static bool IsLoginPrompt(string s)
            => !string.IsNullOrEmpty(s) && s.IndexOf("ogin:", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Real password request ends with a colon (<c>Password:</c>). The change-password nag uses an
        /// angle bracket (<c>new password&gt;</c>) and is deliberately NOT matched here.
        /// </summary>
        public static bool IsPasswordPrompt(string s)
            => !string.IsNullOrEmpty(s) && s.IndexOf("assword:", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// The "change your password" nag. RouterOS prompts with <c>new password&gt;</c> (and
        /// <c>repeat new password&gt;</c>) when the account still uses a default/empty password.
        /// </summary>
        public static bool IsChangePasswordNag(string s)
            => !string.IsNullOrEmpty(s) && s.IndexOf("password>", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsLoginFailure(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("login failure", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("incorrect login", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("invalid user name", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("bad password", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── Interactive login sequence (I/O via delegates) ─────────────────────

        /// <summary>
        /// Drives the RouterOS interactive login. Transport supplies the I/O primitives:
        /// </summary>
        /// <param name="user">User name.</param>
        /// <param name="password">Password (may be empty).</param>
        /// <param name="useTerminalFlags">Append <see cref="TerminalLoginFlags"/> to the login name.</param>
        /// <param name="readUntil">Reads (ANSI-stripped) until the predicate holds or the transport's
        /// receive deadline expires; returns the accumulated stripped text.</param>
        /// <param name="sendLine">Sends a line of text followed by the transport's line terminator.</param>
        /// <param name="sendBytes">Sends raw bytes (used for Ctrl-C).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="TikConnectionLoginException">Credentials rejected or the shell prompt was never reached.</exception>
        public static async Task LoginAsync(
            string user,
            string password,
            bool useTerminalFlags,
            Func<Func<string, bool>, CancellationToken, Task<string>> readUntil,
            Func<string, CancellationToken, Task> sendLine,
            Func<byte[], CancellationToken, Task> sendBytes,
            CancellationToken ct)
        {
            // 1. Wait for the "Login:" prompt (or an already-present shell prompt), send the user name.
            string banner = await readUntil(s => IsLoginPrompt(s) || IsShellPrompt(s), ct).ConfigureAwait(false);
            if (!IsShellPrompt(banner))
            {
                string loginName = useTerminalFlags ? user + TerminalLoginFlags : user;
                await sendLine(loginName, ct).ConfigureAwait(false);

                // 2. Wait for the "Password:" prompt, send the password.
                string afterUser = await readUntil(
                    s => IsPasswordPrompt(s) || IsShellPrompt(s) || IsLoginFailure(s), ct).ConfigureAwait(false);
                if (IsLoginFailure(afterUser))
                    throw LoginException(afterUser);
                if (!IsShellPrompt(afterUser))
                    await sendLine(password, ct).ConfigureAwait(false);
            }

            // 3. Resolve to the shell prompt, dismissing the change-password nag with Ctrl-C.
            string result = await readUntil(
                s => IsShellPrompt(s) || IsChangePasswordNag(s) || IsLoginFailure(s), ct).ConfigureAwait(false);

            int nagRounds = 0;
            while (!IsShellPrompt(result) && IsChangePasswordNag(result) && nagRounds++ < MaxNagRounds)
            {
                await sendBytes(new[] { CtrlC }, ct).ConfigureAwait(false);
                result = await readUntil(
                    s => IsShellPrompt(s) || IsChangePasswordNag(s) || IsLoginFailure(s), ct).ConfigureAwait(false);
            }

            if (!IsShellPrompt(result))
                throw LoginException(result);
        }

        private static TikConnectionLoginException LoginException(string serverText)
            => new TikConnectionLoginException(new Exception(
                "RouterOS CLI login failed. Server response: " + (serverText ?? string.Empty).Trim()));
    }
}
