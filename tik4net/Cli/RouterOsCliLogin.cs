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
        /// <exception cref="TikConnectionLoginException">Credentials rejected, or the shell prompt was never reached.</exception>
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

            string result = await readUntil(settled, ct).ConfigureAwait(false);

            int nagRounds = 0;
            while (!IsShellPrompt(result) && IsChangePasswordNag(result) && nagRounds++ < MaxNagRounds)
            {
                await sendBytes(new[] { CtrlC }, ct).ConfigureAwait(false);
                result = await readUntil(settled, ct).ConfigureAwait(false);
            }

            if (!IsShellPrompt(result))
                throw LoginException(result);
        }

        // ── RoMON: continue an agent session into a target (SSH relay) ─────────

        /// <summary>
        /// On an agent's shell prompt, opens <c>/tool romon ssh</c> to <paramref name="targetRomonId"/>, logs in
        /// as <paramref name="user"/>, and returns once the TARGET's shell prompt is on screen and the target has
        /// confirmed its RoMON id. From then on the terminal is the target's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The relay is typed as <c>/tool romon ssh …; /quit</c>.</b> When the relay ends — for any reason: the
        /// target logs the session out, reboots, or was never reached — RouterOS prints <c>Welcome back!</c> and
        /// hands the terminal back to the AGENT's shell. A prompt shape cannot tell the two routers apart (on
        /// factory defaults both are <c>[admin@MikroTik] &gt;</c>), so every command after that would run on the
        /// agent, silently. The trailing <c>/quit</c> ends the agent's session there instead, and the connection
        /// fails rather than changes router.
        /// </para>
        /// <para>
        /// Every failure before the password prompt looks the same on the wire: a pause, then <c>Welcome back!</c>
        /// (an unknown RoMON id and RoMON disabled on the agent both read exactly so), so RoMON being enabled is
        /// asked first, while the agent's shell can still answer. The rest is positional, and one read confirms:
        /// </para>
        /// <list type="bullet">
        ///   <item><c>Welcome back!</c> or a shell prompt before any <c>password:</c> means the relay never
        ///         started;</item>
        ///   <item>a target user with an empty password gets no <c>password:</c> at all — the change-password nag
        ///         comes first, and is declined with Ctrl-C like every other nag. Nothing is typed into it: it reads
        ///         a new password for the target's account;</item>
        ///   <item>a second <c>password:</c> after the password means refused — a wrong password and a user
        ///         without the <c>ssh</c> policy read identically (7.17rc3 target). Ctrl-C leaves the prompt and
        ///         nothing else is ever typed into it: each further line would be one more failed login in the
        ///         target's log;</item>
        ///   <item>at the prompt, <c>:put [/tool romon get current-id]</c> must answer the target's id, so a
        ///         relay that silently fell back to the agent can never pass as the target.</item>
        /// </list>
        /// <para>The target's IP <c>ssh</c> service is not involved (disabled on the measured target, login
        /// worked); the user's <c>ssh</c> policy is. The <c>+c</c> terminal flag passes through the relay.</para>
        /// </remarks>
        /// <returns>The AGENT's own RoMON id, read before the relay — for <see cref="TikRomonConnectionInfo"/>.</returns>
        /// <exception cref="ArgumentException"><paramref name="targetRomonId"/> is not a MAC-shaped id.</exception>
        /// <exception cref="TikRomonRelayException">The relay did not start, the target refused the login, the
        /// prompt reached is not the target's, or the connection failed mid-relay — see its
        /// <see cref="TikRomonRelayException.Reason"/>.</exception>
        public static async Task<string> RomonSshLoginAsync(
            string targetRomonId,
            string user,
            string password,
            bool useTerminalFlags,
            Func<Func<string, bool>, CancellationToken, Task<string>> readUntil,
            Func<string, CancellationToken, Task> sendLine,
            Func<byte[], CancellationToken, Task> sendBytes,
            CancellationToken ct)
        {
            string id = NormalizeRomonId(targetRomonId);
            try
            {
                return await RomonSshLoginCoreAsync(id, user, password, useTerminalFlags, readUntil, sendLine, sendBytes, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is TikRomonRelayException) && !(ex is OperationCanceledException))
            {
                throw new TikRomonRelayException(TikRomonRelayFailure.TransportFailed,
                    "RoMON SSH relay to " + id + ": the connection to the agent failed while relaying — " + ex.Message, ex);
            }
        }

        private static async Task<string> RomonSshLoginCoreAsync(
            string id,
            string user,
            string password,
            bool useTerminalFlags,
            Func<Func<string, bool>, CancellationToken, Task<string>> readUntil,
            Func<string, CancellationToken, Task> sendLine,
            Func<byte[], CancellationToken, Task> sendBytes,
            CancellationToken ct)
        {
            string loginName = useTerminalFlags ? user + TerminalLoginFlags : user;

            // 0. The agent's own id, while its shell is still the one answering. It is what the target records
            //    as 'by-romon' for this session, and it tells a fallen-back relay apart in step 4's message.
            const string idQuery = ":put [/tool romon get current-id]";
            await sendLine(idQuery, ct).ConfigureAwait(false);
            string agentAnswer = await readUntil(s => AnswerAfterEcho(s, idQuery) != null, ct).ConfigureAwait(false);
            string agentId = AnswerAfterEcho(agentAnswer, idQuery) ?? string.Empty;

            // Asked before the relay, because afterwards there is no agent shell left to ask (see the remarks).
            const string enabledQuery = ":put [/tool romon get enabled]";
            await sendLine(enabledQuery, ct).ConfigureAwait(false);
            string enabled = await readUntil(s => AnswerAfterEcho(s, enabledQuery) != null, ct).ConfigureAwait(false);
            if (string.Equals(AnswerAfterEcho(enabled, enabledQuery), "false", StringComparison.OrdinalIgnoreCase))
                throw Relay(TikRomonRelayFailure.RomonNotEnabledOnAgent, id,
                    "RoMON is not enabled on the agent (/tool romon set enabled=yes)", null);

            await sendLine("/tool romon ssh address=" + id + " user=" + CliCommandBuilder.QuoteIfNeeded(loginName)
                           + "; /quit", ct).ConfigureAwait(false);

            // 1. The relay starts with the target's password prompt — or, for a target user with an empty password,
            //    with no prompt at all: the ssh client is logged straight in and the target's change-password nag
            //    is the first thing on screen (7.24.4). Anything else first = it never started.
            string opened = await readUntil(
                    s => IsPasswordPrompt(s) || IsChangePasswordNag(s) || IsShellPrompt(s) || IsRomonRelayEnd(s), ct)
                .ConfigureAwait(false);
            bool loggedInWithoutPassword = !IsPasswordPrompt(opened) && IsChangePasswordNag(opened) && !IsRomonRelayEnd(opened);
            if (!IsPasswordPrompt(opened) && !loggedInWithoutPassword)
            {
                if (!IsShellPrompt(opened) && !IsRomonRelayEnd(opened))
                    throw Relay(TikRomonRelayFailure.TargetDidNotRespond, id,
                        "the agent did not answer /tool romon ssh with a password prompt", opened);
                throw Relay(TikRomonRelayFailure.TargetUnreachable, id,
                    "the agent could not reach RoMON id " + id + " (not in its RoMON overlay — see /tool romon discover on the agent)",
                    null);
            }

            // 3. The target's prompt — or the password prompt again, which is the refusal. Deliberately NOT
            //    IsLoginFailure: the target prints its recent critical log lines at login, and those read
            //    "login failure for user … by romon …" on a login that SUCCEEDED (7.17rc3). Position decides.
            Func<string, bool> settled = s =>
                IsShellPrompt(s) || IsPasswordPrompt(s) || IsChangePasswordNag(s) || IsRomonRelayEnd(s);
            string result;
            if (loggedInWithoutPassword)
            {
                // Nothing is typed here: the nag reads a NEW password for the target's account.
                result = opened;
            }
            else
            {
                // 2. The target's password. Traced as a secret, like the ordinary login's.
                using (Diagnostics.TikWireTrace.Secret())
                    await sendLine(password, ct).ConfigureAwait(false);
                result = await readUntil(settled, ct).ConfigureAwait(false);
            }

            int nagRounds = 0;
            while (!IsShellPrompt(result) && !IsPasswordPrompt(result) && IsChangePasswordNag(result)
                   && nagRounds++ < MaxNagRounds)
            {
                await sendBytes(new[] { CtrlC }, ct).ConfigureAwait(false);
                result = await readUntil(settled, ct).ConfigureAwait(false);
            }

            if (!IsShellPrompt(result) || IsRomonRelayEnd(result))
            {
                if (IsPasswordPrompt(result) && !IsRomonRelayEnd(result))
                {
                    await sendBytes(new[] { CtrlC }, ct).ConfigureAwait(false);   // leave the prompt, type nothing
                    throw Relay(TikRomonRelayFailure.TargetRefusedLogin, id,
                        "the target refused the login for user '" + user +
                        "': wrong password, or the user's group lacks the 'ssh' policy (RoMON SSH needs it)", null);
                }
                throw Relay(TikRomonRelayFailure.TargetDidNotRespond, id,
                    IsRomonRelayEnd(result)
                        ? "the relay ended before the target's shell prompt"
                        : "the target did not reach its shell prompt",
                    result);
            }

            // 4. Confirm it is the target: a relay that fell back to the agent shows the agent's own id.
            await sendLine(idQuery, ct).ConfigureAwait(false);
            string confirmed = await readUntil(s => AnswerAfterEcho(s, idQuery) != null, ct).ConfigureAwait(false);
            string? answeredId = AnswerAfterEcho(confirmed, idQuery);
            if (!string.Equals(answeredId, id, StringComparison.OrdinalIgnoreCase))
            {
                bool fellBack = !string.IsNullOrEmpty(agentId)
                                && string.Equals(answeredId, agentId, StringComparison.OrdinalIgnoreCase);
                throw Relay(TikRomonRelayFailure.NotTheTarget, id,
                    "the prompt reached is not the target's: its RoMON id is '" + answeredId + "'" +
                    (fellBack ? " — the agent's own; the relay fell back to the agent" : ""),
                    answeredId == null ? confirmed : null);
            }
            return agentId;
        }

        /// <summary>
        /// What the agent prints when a <c>/tool romon ssh</c> session ends and the terminal is its own again —
        /// whether the target logged out or was never reached.
        /// </summary>
        internal const string RomonRelayEndText = "Welcome back!";

        /// <summary>
        /// True once the RoMON relay has handed the terminal back to the agent — <see cref="RomonRelayEndText"/> on a
        /// line of its own, so a command's output that merely quotes it (a comment, a log line) does not count.
        /// </summary>
        internal static bool IsRomonRelayEnd(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int at = s.IndexOf(RomonRelayEndText, StringComparison.Ordinal); at >= 0;
                 at = s.IndexOf(RomonRelayEndText, at + 1, StringComparison.Ordinal))
            {
                int end = at + RomonRelayEndText.Length;
                bool lineStart = at == 0 || s[at - 1] == '\n' || s[at - 1] == '\r';
                bool lineEnd = end == s.Length || s[end] == '\r' || s[end] == '\n';
                if (lineStart && lineEnd) return true;
            }
            return false;
        }

        /// <summary>
        /// For a terminal relayed to <paramref name="relayedTo"/> (<c>null</c> when it is not relayed): throws
        /// <see cref="TikRomonRelayEndedException"/> when <paramref name="received"/> shows the relay ended.
        /// </summary>
        /// <param name="transport">Short transport name for the message.</param>
        /// <param name="relayedTo">The target's RoMON id, or <c>null</c> for a direct session (never throws).</param>
        /// <param name="command">The command concerned, if any.</param>
        /// <param name="received">Terminal text, ANSI-stripped.</param>
        /// <param name="commandSent">Whether <paramref name="command"/> had been sent — see
        /// <see cref="TikRomonRelayEndedException.CommandMayHaveRun"/>.</param>
        internal static void ThrowIfRomonRelayEnded(string transport, string? relayedTo, string? command, string received,
            bool commandSent)
        {
            if (relayedTo == null || !IsRomonRelayEnd(received))
                return;
            string what = string.IsNullOrEmpty(command) ? "the last request" : "'" + command!.Trim() + "'";
            throw new TikRomonRelayEndedException(
                transport + ": the RoMON relay to " + relayedTo + " ended — the target logged the session out, rebooted, "
                + "or dropped out of the agent's RoMON overlay — and the agent's session ended with it. "
                + (commandSent
                    ? what + " was running, and may have taken effect on the target."
                    : "It ended before " + what + " was sent; the command did not run.")
                + " Nothing ran on the agent. Open a new connection to continue.",
                commandSent, received.Length == 0 ? null : received);
        }

        /// <summary>
        /// The one-line answer to <paramref name="echoedCommand"/>, once the prompt that follows it is on screen;
        /// <c>null</c> until then. Keyed on the echo rather than on "a prompt": RouterOS repaints the prompt once
        /// more right after a login, and that stale prompt would otherwise end the read before the answer.
        /// </summary>
        internal static string? AnswerAfterEcho(string text, string echoedCommand)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int echo = text.LastIndexOf(echoedCommand, StringComparison.Ordinal);
            if (echo < 0) return null;
            string after = text.Substring(echo + echoedCommand.Length);
            if (!IsShellPrompt(after)) return null;
            foreach (string raw in after.Split('\r', '\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || ContainsPromptSuffix(line)) continue;
                return line;
            }
            return string.Empty;
        }

        /// <summary>
        /// A RoMON id as the CLI accepts it: six hex octets separated by ':' (or '-'), upper-cased. Anything else
        /// is refused before it reaches the terminal — the id is spliced into a command line.
        /// </summary>
        internal static string NormalizeRomonId(string romonId)
        {
            string s = (romonId ?? string.Empty).Trim().Replace('-', ':');
            string[] parts = s.Split(':');
            bool ok = parts.Length == 6;
            if (ok)
                foreach (string p in parts)
                    if (p.Length != 2 || !Uri.IsHexDigit(p[0]) || !Uri.IsHexDigit(p[1])) { ok = false; break; }
            if (!ok)
                throw new ArgumentException(
                    "A RoMON id is six hex octets, e.g. AA:BB:CC:DD:EE:FF; got '" + romonId + "'.", nameof(romonId));
            return s.ToUpperInvariant();
        }

        private static TikRomonRelayException Relay(TikRomonRelayFailure reason, string id, string what, string? serverText)
            => new TikRomonRelayException(reason,
                "RoMON SSH relay to " + id + ": " + what + "." +
                (string.IsNullOrWhiteSpace(serverText) ? "" : " Server response: " + serverText!.Trim()));

        private static TikConnectionLoginException LoginException(string serverText)
            => new TikConnectionLoginException(new Exception(
                "RouterOS CLI login failed. Server response: " + (serverText ?? string.Empty).Trim()));
    }
}
