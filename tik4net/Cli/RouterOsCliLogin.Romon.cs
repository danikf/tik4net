using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    internal static partial class RouterOsCliLogin
    {
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

        // How long a cancel that lands while the agent connects to the target waits for the target's answer
        // (RomonSshLoginCoreAsync, step 1). The agent answers within about 0.2 s; an unreachable id takes ~5 s to end
        // the relay, and the cancel does not wait for that.
        private const int RelayCancelGraceMs = 3000;

        // Each step of the relay login is one read, and the connection's ReceiveTimeout is its deadline.
        private const string WithinReceiveTimeout =
            "before the read gave up (each step of the relay login waits up to the connection's ReceiveTimeout; "
            + "a slow agent or target needs a longer one)";

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
            string loginName = useTerminalFlags ? WithTerminalLoginFlags(user) : user;

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

            string relayCommand = "/tool romon ssh address=" + id + " user=" + CliCommandBuilder.QuoteIfNeeded(loginName)
                                  + "; /quit";
            await sendLine(relayCommand, ct).ConfigureAwait(false);

            // 1. The relay starts with the target's password prompt — or, for a target user with an empty password,
            //    with no prompt at all: the ssh client is logged straight in and the target's change-password nag
            //    is the first thing on screen (7.24.4). Anything else first = it never started. An id the agent
            //    cannot reach ends the relay after ~5 s ("Welcome back!", measured 7.24.4). The agent's own prompt
            //    with nothing but console log lines before it is not an answer: RouterOS repaints its prompt after
            //    every log line it writes into the session, and under load those come often (another session's
            //    login failure) — taken for the end, it read as "could not reach" while the agent's discover listed
            //    the target at one hop (2026-10-02).
            Func<string, bool> relayAnswered = s => IsPasswordPrompt(s) || IsChangePasswordNag(s) || IsRomonRelayEnd(s)
                                                    || (IsShellPrompt(s) && CliOutputHelper.CleanOutput(s, relayCommand).Length > 0);
            string opened;
            try
            {
                opened = await readUntil(relayAnswered, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled while the agent is still connecting to the target. Closing the agent's console now leaves
                // the target a session that finishes its login a moment later and that nothing ever ends (a 7.25
                // agent and a 7.21.5 target, measured: findings-romon.md §4). Once the target has answered, the agent
                // ends that session with its console, so the cancel waits for the answer, within a bound.
                using (var grace = new CancellationTokenSource(RelayCancelGraceMs))
                {
                    try { await readUntil(relayAnswered, grace.Token).ConfigureAwait(false); }
                    catch { /* the bound, or a failed read: the caller's cancel is reported either way */ }
                }
                throw;
            }
            bool loggedInWithoutPassword = !IsPasswordPrompt(opened) && IsChangePasswordNag(opened) && !IsRomonRelayEnd(opened);
            if (!IsPasswordPrompt(opened) && !loggedInWithoutPassword)
            {
                // Nothing recognisable before the read's deadline — a slow prompt reads exactly like no prompt.
                if (!IsShellPrompt(opened) && !IsRomonRelayEnd(opened))
                    throw Relay(TikRomonRelayFailure.TargetDidNotRespond, id,
                        "the agent did not answer /tool romon ssh with a password prompt " + WithinReceiveTimeout, opened);
                // An id the agent cannot reach prints nothing before 'Welcome back!'. A line there is the agent's own
                // SSH client refusing the session — the target IS in the overlay (7.25 agent to a 6.x target:
                // "packet not aligned").
                string agentSaid = RomonRelayAgentMessage(opened, relayCommand);
                throw Relay(TikRomonRelayFailure.TargetUnreachable, id,
                    agentSaid.Length > 0
                        ? "the agent's SSH session to RoMON id " + id + " failed: " + agentSaid
                          + " (the agent's own SSH client refused the target)"
                        : "the agent could not reach RoMON id " + id + " (not in its RoMON overlay — see /tool romon discover on the agent)",
                    opened);
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

            var screen = new CliLoginScreen(CliLoginScreen.RefusalRule.None);
            while (!IsShellPrompt(result) && !IsPasswordPrompt(result) && screen.Feed(result) == CliLoginScreen.Step.SendCtrlC)
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
                        : "the target did not reach its shell prompt " + WithinReceiveTimeout,
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
        /// A status line the RoMON relay writes into the relayed session's byte stream, at any byte — inside a
        /// field name or a value of the target's output, not at a line boundary — ended by a bare LF
        /// (findings-romon.md §4, "Large reads").
        /// </summary>
        internal const string RomonRelayNoiseText = "waiting for head\n";

        /// <summary>
        /// For a terminal relayed to <paramref name="relayedTo"/> (<c>null</c> when it is not relayed): the raw
        /// terminal text <paramref name="raw"/> without the <see cref="RomonRelayNoiseText"/> the relay splices
        /// into it. The splice is a pure insertion, so removing it gives back exactly the target's bytes. A direct
        /// session is returned unchanged. Apply it before <see cref="VtStripper.StripAnsi"/>: the splice can
        /// land inside an escape sequence.
        /// </summary>
        internal static string WithoutRomonRelayNoise(string? relayedTo, string raw)
        {
            if (relayedTo == null || string.IsNullOrEmpty(raw)
                || raw.IndexOf(RomonRelayNoiseText, StringComparison.Ordinal) < 0)
                return raw;
            return raw.Replace(RomonRelayNoiseText, string.Empty);
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
            throw RomonRelayEnded(transport, relayedTo, command, received, commandSent);
        }

        /// <summary>
        /// The agent's session ended under a read relayed to <paramref name="relayedTo"/>. The relay is typed as
        /// <c>/tool romon ssh …; /quit</c>, so the agent's session ends only once the relay has: that is the relay's
        /// end even when its <c>Welcome back!</c> line never arrived — a 6.49.13 agent closed an SSH channel without
        /// it, and on the MAC layer the line is a datagram the router can drop.
        /// </summary>
        internal static TikRomonRelayEndedException RomonRelayEndedWithTheSession(string transport, string relayedTo,
            string? command, string received)
            => RomonRelayEnded(transport, relayedTo, command, received, commandSent: true);

        private static TikRomonRelayEndedException RomonRelayEnded(string transport, string relayedTo, string? command,
            string received, bool commandSent)
        {
            string what = string.IsNullOrEmpty(command) ? "the last request" : "'" + command!.Trim() + "'";
            return new TikRomonRelayEndedException(
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

        // What the agent printed between the relay command's echo and 'Welcome back!', on one line; empty when it
        // printed nothing there.
        private static string RomonRelayAgentMessage(string opened, string relayCommand)
        {
            int end = opened.IndexOf(RomonRelayEndText, StringComparison.Ordinal);
            string before = end >= 0 ? opened.Substring(0, end) : opened;
            return string.Join("; ", CliOutputHelper.CleanOutput(before, relayCommand)
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !IsShellPrompt(l)));
        }

        private static TikRomonRelayException Relay(TikRomonRelayFailure reason, string id, string what, string? serverText)
            => new TikRomonRelayException(reason,
                "RoMON SSH relay to " + id + ": " + what + "." +
                (string.IsNullOrWhiteSpace(serverText) ? "" : " Server response: " + serverText!.Trim()));
    }
}
