using System;

namespace tik4net.Cli
{
    /// <summary>
    /// The one rule for the screen a terminal login lands on once the transport has authenticated: answer the
    /// change-password nag with Ctrl-C (at most <see cref="MaxNagRounds"/> times), recognise the router's refusal, and
    /// know the shell prompt when it comes. Fed the ANSI-stripped text accumulated since the last Ctrl-C.
    /// </summary>
    /// <remarks>
    /// <para>Every terminal login settles here, and what differs is how a refusal shows (<see cref="RefusalRule"/>).
    /// MAC-Telnet and the WinBox CLI recognise it by its text, <c>Login failed</c>, because they have no positional
    /// signal: their authentication happened below the terminal (EC-SRP5), so there is no <c>Login:</c> prompt for the
    /// router to re-offer. Narrowly that phrase, not <see cref="RouterOsCliLogin.IsLoginFailure"/>, which also matches
    /// <c>login failure</c> — the wording of the router's own log lines, which the shipped logging rules echo onto any
    /// console at any moment (findings-cli.md §11). Telnet has the position: credentials have been sent, so a fresh
    /// <c>Login:</c> is the router refusing them, whatever words it used. SSH's refusal happened in the SSH layer.</para>
    /// <para>An expired password (<c>/user expire-password</c>) shows the same nag — <c>Change your password (Ctrl-C to
    /// skip)</c>, <c>new password&gt;</c> — and one Ctrl-C skips it (measured on 7.25 over MAC-Telnet and the WinBox
    /// CLI). A nag still on screen after the third Ctrl-C would be RouterOS insisting on a new password. Nothing but
    /// Ctrl-C is ever typed into that prompt — a probe answer typed there is password input
    /// (<see cref="CliPasswordPromptGuard"/>) — so the login stops and says so.</para>
    /// </remarks>
    internal sealed class CliLoginScreen
    {
        /// <summary>How many times the nag is answered with Ctrl-C before the login gives up.</summary>
        internal const int MaxNagRounds = 3;

        /// <summary>Ctrl-C, the one key sent into the change-password prompt.</summary>
        internal static readonly byte[] CtrlC = { CliTerminalText.CtrlC };

        /// <summary>How the router's refusal shows on this transport's screen.</summary>
        internal enum RefusalRule
        {
            /// <summary>The line <c>Login failed</c>: an authentication below the terminal (MAC-Telnet, WinBox CLI).</summary>
            Text,
            /// <summary>A re-offered <c>Login:</c> prompt: credentials typed into the terminal (Telnet).</summary>
            LoginPrompt,
            /// <summary>None: the transport refused before the shell started (SSH, a RoMON relay's nag).</summary>
            None,
        }

        private readonly RefusalRule _rule;

        internal CliLoginScreen(RefusalRule rule = RefusalRule.Text)
        {
            _rule = rule;
        }

        /// <summary>What the caller does next.</summary>
        internal enum Step
        {
            /// <summary>Keep reading.</summary>
            Continue,
            /// <summary>Send <see cref="CtrlC"/> and start the text again from empty: the nag is on screen.</summary>
            SendCtrlC,
            /// <summary>The shell prompt: the login is done.</summary>
            Prompt,
            /// <summary>The router refused the login (<see cref="RefusalLine"/>).</summary>
            Refused,
            /// <summary>The nag came back after <see cref="MaxNagRounds"/> Ctrl-Cs: RouterOS demands a new password.</summary>
            NagPersists,
        }

        /// <summary>The Ctrl-Cs sent so far.</summary>
        internal int NagRounds { get; private set; }

        /// <summary>The refusal's first line, once <see cref="Feed"/> answered <see cref="Step.Refused"/>.</summary>
        internal string? RefusalLine { get; private set; }

        /// <summary>Judges the screen as it stands.</summary>
        /// <param name="stripped">The ANSI-stripped text since the login began or the last <see cref="Step.SendCtrlC"/>.</param>
        internal Step Feed(string stripped)
        {
            if (string.IsNullOrEmpty(stripped)) return Step.Continue;

            if (RouterOsCliLogin.IsChangePasswordNag(stripped))
            {
                if (NagRounds >= MaxNagRounds) return Step.NagPersists;
                NagRounds++;
                return Step.SendCtrlC;
            }

            // Checked before the prompt, and on every chunk: the refusal is the last thing the router says before it
            // stops talking, so nothing later will arrive to be examined.
            if (_rule == RefusalRule.Text ? IsRefusal(stripped)
                : _rule == RefusalRule.LoginPrompt && RouterOsCliLogin.IsLoginPrompt(stripped) && !RouterOsCliLogin.IsShellPrompt(stripped))
            {
                RefusalLine = FirstLine(stripped);
                return Step.Refused;
            }

            return RouterOsCliLogin.IsShellPrompt(stripped) ? Step.Prompt : Step.Continue;
        }

        /// <summary>RouterOS's refusal line after an authentication below the terminal: <c>Login failed, …</c>.</summary>
        internal static bool IsRefusal(string stripped)
            => !string.IsNullOrEmpty(stripped)
            && stripped.IndexOf("Login failed", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The exception for <see cref="Step.Refused"/>.</summary>
        internal TikConnectionLoginRefusedException Refusal(string transport)
            => new TikConnectionLoginRefusedException(transport, RefusalLine ?? "Login failed");

        /// <summary>The exception for <see cref="Step.NagPersists"/>.</summary>
        internal static TikConnectionLoginException NagPersisted(string transport)
            => new TikConnectionLoginException(new InvalidOperationException(
                transport + ": RouterOS keeps asking for a new password after " + MaxNagRounds + " Ctrl-C — the account's "
                + "password has expired or must be changed before this user can use a console. Nothing was typed into the "
                + "prompt. Change the password (for example over the binary API: /user set … password=…) and connect again."));

        /// <summary>
        /// The exception for a login that never reached the prompt. What it was waiting for is only half the story; the
        /// other half is what it had already been given, and without that the failure cannot tell "the router said nothing
        /// at all" (a lost session start, a dead path) from "the router is mid-negotiation and one packet went missing" (a
        /// stalled VT100 probe exchange). One diagnosis spent nine reproduction runs on a message that carried neither.
        /// </summary>
        /// <param name="transport">Named in the message.</param>
        /// <param name="elapsedMs">How long the login waited.</param>
        /// <param name="packets">Terminal packets (datagrams, frames) that carried text.</param>
        /// <param name="screen">Everything received since the last nag was skipped, escapes included.</param>
        internal TimeoutException TimedOut(string transport, long elapsedMs, int packets, string screen)
        {
            string seen = VtStripper.StripAnsi(screen);
            if (seen.Length > 200)
                seen = seen.Substring(seen.Length - 200);
            return new TimeoutException(
                transport + ": timed out waiting for shell prompt after " + elapsedMs + " ms; "
                + packets + " terminal packet(s) received, " + screen.Length + " char(s)"
                + (NagRounds > 0 ? ", change-password nag dismissed " + NagRounds + "x" : "")
                + (seen.Length == 0 ? ", nothing on screen." : ", screen tail: " + seen.Replace("\r", "\\r").Replace("\n", "\\n")));
        }

        /// <summary>The first non-empty line of <paramref name="s"/>, trimmed.</summary>
        internal static string FirstLine(string s)
        {
            foreach (string line in s.Split('\r', '\n'))
                if (line.Trim().Length > 0) return line.Trim();
            return s.Trim();
        }
    }
}
