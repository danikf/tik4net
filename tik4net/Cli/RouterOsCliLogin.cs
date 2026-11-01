using System;
using System.Linq;
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
    internal static partial class RouterOsCliLogin
    {
        /// <summary>
        /// Shell prompt suffix. RouterOS prompt is <c>[user@identity] &gt; </c>; the identity is
        /// arbitrary so we match only the suffix. Compare against ANSI-stripped, right-trimmed text
        /// (note: no trailing space — the line is trimmed before the check).
        /// </summary>
        public const string PromptSuffix = "] >";

        /// <summary>
        /// How long a terminal is drained after login (ms). RouterOS repaints its prompt right after the login (the
        /// VT100 cursor probe); left unread, that second prompt reaches the first command's read and passes for its
        /// completion.
        /// </summary>
        internal const int PostLoginDrainMs = 250;

        /// <summary>
        /// The longest a close in Safe Mode waits for RouterOS to end the session itself (ms). <c>/quit</c> in Safe Mode
        /// asks a question; a client that types the answer and disconnects in the same instant can make RouterOS 6.49.13
        /// end the session inside the question, and its console then stops serving new Telnet, SSH and API logins until
        /// a reboot (measured over Telnet, MAC-Telnet and WinBox CLI). Every terminal therefore waits for the router's
        /// reaction, at most this long.
        /// </summary>
        internal const int SafeModeQuitWaitMs = 3000;

        /// <summary>
        /// The keystrokes that leave the console: <c>/quit</c> and the terminal's line end, plus the <c>y</c> that
        /// answers <c>/quit</c>'s question while the session holds Safe Mode.
        /// </summary>
        internal static string QuitKeys(bool answerSafeModeQuestion, string lineEnd)
            => "/quit" + lineEnd + (answerSafeModeQuestion ? "y" : "");

        /// <summary>
        /// Shell prompt suffix while RouterOS Safe Mode is active: the <c>&gt;</c> is <b>replaced</b> by a
        /// <c>&lt;SAFE&gt;</c> token, e.g. <c>[admin@MikroTik] &lt;SAFE&gt; </c>. Captured off the wire on
        /// 7.23.2 — see <see cref="SafePromptSuffixWithArrow"/> for why both forms are matched.
        /// </summary>
        public const string SafePromptSuffix = "] <SAFE>";

        /// <summary>
        /// The safe-mode prompt with the <c>&gt;</c> kept after the token
        /// (<c>[admin@MikroTik] &lt;SAFE&gt; &gt;</c>).
        /// </summary>
        /// <remarks>
        /// RouterOS 7.23.2 does not emit this form — its prompt is <c>"[admin@CHR] &lt;SAFE&gt; "</c>. A
        /// prompt that is never recognised does not fail loudly: every command inside safe mode runs to the
        /// full receive deadline and returns whatever accumulated, so a mismatch costs 30 s per command with
        /// nothing going red. Both spellings are therefore matched rather than one being chosen, because the
        /// evidence base for either is one router on one version, and matching a prompt shape current
        /// RouterOS does not emit costs nothing.
        /// </remarks>
        public const string SafePromptSuffixWithArrow = "] <SAFE> >";

        // Every accepted prompt ending, longest-lived first. Kept in one place so prompt detection cannot
        // drift between the read loops (IsShellPrompt) and the output cleaner (CliOutputHelper).
        private static readonly string[] PromptSuffixes =
        {
            PromptSuffix,
            SafePromptSuffix,
            SafePromptSuffixWithArrow,
        };

        /// <summary>
        /// True when <paramref name="text"/>, once trailing CR/LF/spaces are trimmed, ends with any accepted
        /// prompt suffix (normal or Safe Mode).
        /// </summary>
        internal static bool EndsWithPromptSuffix(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            string t = text.TrimEnd('\r', '\n', ' ');
            foreach (string suffix in PromptSuffixes)
                if (t.EndsWith(suffix, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// The index just past the LAST accepted prompt suffix in <paramref name="text"/> and the blank that
        /// follows it, or -1 when there is none: where the input line starts on a row the prompt was drawn on.
        /// </summary>
        internal static int IndexAfterPromptSuffix(string text)
        {
            if (string.IsNullOrEmpty(text))
                return -1;
            int best = -1;
            foreach (string suffix in PromptSuffixes)
            {
                int at = text.LastIndexOf(suffix, StringComparison.Ordinal);
                if (at >= 0)
                    best = Math.Max(best, at + suffix.Length);
            }
            if (best >= 0 && best < text.Length && text[best] == ' ')
                best++;
            return best;
        }

        /// <summary>True when <paramref name="text"/> contains any accepted prompt suffix anywhere.</summary>
        internal static bool ContainsPromptSuffix(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (string suffix in PromptSuffixes)
                if (text.IndexOf(suffix, StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }

        /// <summary>
        /// Login name suffix appended to the user name on every terminal login (Telnet, SSH, MAC-Telnet, a direct
        /// WinBox CLI). <c>+c</c> disables ANSI colour, which drastically reduces escape sequences in the output. We deliberately do NOT
        /// pin a fixed terminal width here — the transport answers RouterOS's cursor-probe negotiation
        /// (see <see cref="Vt100State"/>) advertising a wide terminal so long <c>:put</c> as-value
        /// records are not wrapped. See findings-cli.md §4.
        /// </summary>
        public const string TerminalLoginFlags = "+c";

        /// <summary>
        /// <paramref name="loginName"/> with the <see cref="TerminalLoginFlags"/> letters in its options group: appended
        /// as a group of their own to a bare name (<c>admin</c> → <c>admin+c</c>), and merged into the group a caller
        /// already wrote (<c>admin+t</c> → <c>admin+tc</c>), because RouterOS reads one group only — measured over
        /// MAC-Telnet on 7.25, <c>admin+t+c</c> logs in and ignores the <c>c</c>, the console comes back in colour.
        /// </summary>
        internal static string WithTerminalLoginFlags(string loginName)
        {
            int plus = loginName.IndexOf('+');
            if (plus < 0) return loginName + TerminalLoginFlags;
            string options = loginName.Substring(plus + 1);
            var missing = new System.Text.StringBuilder();
            foreach (char flag in TerminalLoginFlags.Substring(1))
                if (options.IndexOf(flag) < 0) missing.Append(flag);
            return loginName + missing;
        }

        /// <summary>Ctrl-C — used to dismiss the change-password nag.</summary>
        private const byte CtrlC = 0x03;

        private const int MaxNagRounds = 3;

        // ── Prompt / state detection (pure, unit-testable) ─────────────────────

        /// <summary>True when the ANSI-stripped text ends with the RouterOS shell prompt, including the
        /// <c>&lt;SAFE&gt;</c> variant shown while Safe Mode is active.</summary>
        public static bool IsShellPrompt(string strippedText)
            => EndsWithPromptSuffix(strippedText);

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

        /// <summary>
        /// Refusal phrases, checked only BEFORE the password is sent (after it the screen is the banner, which
        /// can quote a failed login from the router's log). <b>Lexical detection is the fast path, not the contract</b> — the authority is
        /// the positional signal in <see cref="ResolveToPromptAsync"/>: RouterOS restarts the login dialogue
        /// after a refusal, so a <c>Login:</c> prompt arriving once credentials have been sent means rejected,
        /// whatever the wording.
        /// </summary>
        /// <remarks>
        /// The list on its own is not enough, and cannot be: an unmatched phrase does not throw, it waits.
        /// RouterOS 7.23.2 answers a wrong password with <c>"Login failed, incorrect username or password"</c>,
        /// which matches <b>none</b> of the five phrases here; with the phrase table as the only signal that
        /// was measured at <b>30 193 ms</b> to report on Telnet against 127 ms on the binary API — the full
        /// receive deadline, then a login exception carrying the very text that went unrecognised. The older
        /// phrases are kept because the evidence base for each is one router on one version, and matching a
        /// wording current RouterOS does not emit costs nothing.
        /// <para>
        /// Measured by mutation: with this list emptied and only the positional signal left, the
        /// transcript tests still pass — the phrases are a fast path and a better exception message, not the
        /// contract. A temptation to extend the list is a sign that something has come to rely on it.
        /// </para>
        /// </remarks>
        public static bool IsLoginFailure(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("login failed", StringComparison.OrdinalIgnoreCase) >= 0       // 7.23.2, verified live
                || s.IndexOf("incorrect username", StringComparison.OrdinalIgnoreCase) >= 0 // 7.23.2, verified live
                || s.IndexOf("login failure", StringComparison.OrdinalIgnoreCase) >= 0
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
        /// <exception cref="TikConnectionLoginException">Credentials rejected (the message says the login failed), or
        /// the shell prompt did not arrive before a read gave up at the receive deadline or the end of the session (the
        /// message says so, with how long the read waited and what the router had sent).</exception>
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
                string loginName = useTerminalFlags ? WithTerminalLoginFlags(user) : user;
                await sendLine(loginName, ct).ConfigureAwait(false);

                // 2. Wait for the "Password:" prompt, send the password.
                string afterUser = await readUntil(
                    s => IsPasswordPrompt(s) || IsShellPrompt(s) || IsLoginFailure(s), ct).ConfigureAwait(false);
                if (IsLoginFailure(afterUser))
                    throw LoginException(afterUser);
                if (!IsShellPrompt(afterUser))
                {
                    // The terminal transports type the password as ordinary bytes, so the socket-level
                    // trace would otherwise carry it verbatim into whatever the user pastes into an issue.
                    using (Diagnostics.TikWireTrace.Secret())
                        await sendLine(password, ct).ConfigureAwait(false);
                }
            }

            // 3. Resolve to the shell prompt, dismissing the change-password nag with Ctrl-C. Credentials
            //    have been sent by now, so a fresh "Login:" is the router restarting the dialogue — i.e.
            //    a refusal, whatever wording it used.
            await ResolveToPromptAsync(readUntil, sendBytes, ct,
                loginPromptMeansFailure: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads until the RouterOS shell prompt appears, dismissing the "change your password" nag with
        /// Ctrl-C (up to <see cref="MaxNagRounds"/> times). Shared by transports that authenticate
        /// <b>before</b> the shell starts (e.g. SSH, where the transport layer does the login) and so skip
        /// the <c>Login:</c>/<c>Password:</c> exchange but still must settle the terminal to a usable prompt.
        /// Telnet/MAC-Telnet reach this as the final step of <see cref="LoginAsync"/>.
        /// </summary>
        /// <param name="readUntil">Reads (ANSI-stripped) until the predicate holds or the receive deadline expires.</param>
        /// <param name="sendBytes">Sends raw bytes (used for Ctrl-C).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="loginPromptMeansFailure">
        /// When <c>true</c>, a <c>Login:</c> prompt in this phase is treated as a refusal. Set by
        /// <see cref="LoginAsync"/>, where credentials have already been sent, so RouterOS re-offering the
        /// login dialogue can only mean it rejected them. Left <c>false</c> for transports that authenticate
        /// below the terminal (SSH, WinBox mepty) and merely settle an already-authenticated shell.
        /// </param>
        /// <remarks>
        /// The positional signal exists because the lexical one cannot be trusted across versions: it is the
        /// router's own dialogue state rather than its choice of words, so it holds on a RouterOS whose
        /// refusal text nobody here has ever seen.
        /// </remarks>
        /// <exception cref="TikConnectionLoginException">Credentials rejected (a re-offered <c>Login:</c>, or the
        /// change-password nag still on screen after <see cref="MaxNagRounds"/> Ctrl-Cs: the message says the login
        /// failed), or the shell prompt did not arrive before the read gave up at the receive deadline or the end of the
        /// session (the message says so, with how long the read waited and what the router had sent).</exception>
        public static async Task ResolveToPromptAsync(
            Func<Func<string, bool>, CancellationToken, Task<string>> readUntil,
            Func<byte[], CancellationToken, Task> sendBytes,
            CancellationToken ct,
            bool loginPromptMeansFailure = false)
        {
            // Deliberately NOT IsLoginFailure: after the password the screen carries the banner, and RouterOS 6
            // prints the account's unseen critical log lines under it — "login failure for user admin … via api"
            // on a login that SUCCEEDED (6.49.13). The refusal is the re-offered Login: (position), or the prompt
            // never arriving.
            Func<string, bool> settled = s =>
                IsShellPrompt(s) || IsChangePasswordNag(s)
                || (loginPromptMeansFailure && IsLoginPrompt(s));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            string result = await readUntil(settled, ct).ConfigureAwait(false);

            int nagRounds = 0;
            while (!IsShellPrompt(result) && IsChangePasswordNag(result) && nagRounds++ < MaxNagRounds)
            {
                await sendBytes(new[] { CtrlC }, ct).ConfigureAwait(false);
                watch.Restart();
                result = await readUntil(settled, ct).ConfigureAwait(false);
            }

            if (IsShellPrompt(result))
                return;
            // A read that returns text none of its predicates accepts gave up: the receive deadline passed or the
            // session ended. That is not a refusal — the router may only have been slow to draw the banner — and
            // reporting it as "login failed" sent a diagnosis after wrong credentials.
            if (!settled(result))
                throw PromptNeverArrived(result, watch.ElapsedMilliseconds);
            throw LoginException(result);
        }

        private static TikConnectionLoginException LoginException(string serverText)
            => new TikConnectionLoginException(new Exception(
                "RouterOS CLI login failed. Server response: " + (serverText ?? string.Empty).Trim()));

        // The read cannot say which of the two ended it (the delegate returns only the text), so the wording names both.
        private static TikConnectionLoginException PromptNeverArrived(string serverText, long waitedMs)
            => new TikConnectionLoginException(new TimeoutException(
                "RouterOS CLI login: the shell prompt did not arrive within " + waitedMs + " ms (the receive deadline "
                + "passed or the session ended first); the router had sent: '" + (serverText ?? string.Empty).Trim() + "'."));
    }
}
