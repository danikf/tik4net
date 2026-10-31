using System;

namespace tik4net.Cli
{
    /// <summary>
    /// The one rule for the screen a terminal login lands on once the transport has authenticated: answer the
    /// change-password nag with Ctrl-C (at most <see cref="MaxNagRounds"/> times), recognise the router's refusal, and
    /// know the shell prompt when it comes. Fed the ANSI-stripped text accumulated since the last Ctrl-C.
    /// </summary>
    /// <remarks>
    /// <para>MAC-Telnet and the WinBox CLI each had a loop of their own, and they answered differently (review D2):
    /// MAC-Telnet sent one Ctrl-C and then waited out the login timeout on a nag that came back, the WinBox CLI sent
    /// three and never looked for a refusal, so a refused login waited out the timeout too.</para>
    /// <para>The refusal is recognised by its text, <c>Login failed</c>, because these two transports have no
    /// positional signal: their authentication happened below the terminal (EC-SRP5), so there is no <c>Login:</c>
    /// prompt for the router to re-offer. Narrowly that phrase, not <see cref="RouterOsCliLogin.IsLoginFailure"/>,
    /// which also matches <c>login failure</c> — the wording of the router's own log lines, which the shipped logging
    /// rules echo onto any console at any moment (findings-cli.md §11). Telnet and SSH keep their positional rule in
    /// <see cref="RouterOsCliLogin.ResolveToPromptAsync"/>; aligning them is the CLI review's (D1).</para>
    /// <para>A nag still on screen after the third Ctrl-C is RouterOS insisting on a new password (an expired one:
    /// <c>/user expire-password</c>). Nothing but Ctrl-C is ever typed into that prompt — a probe answer typed there is
    /// password input (<see cref="CliPasswordPromptGuard"/>) — so the login stops and says so.</para>
    /// </remarks>
    internal sealed class CliLoginScreen
    {
        /// <summary>How many times the nag is answered with Ctrl-C before the login gives up.</summary>
        internal const int MaxNagRounds = 3;

        /// <summary>Ctrl-C, the one key sent into the change-password prompt.</summary>
        internal static readonly byte[] CtrlC = { 0x03 };

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
            if (IsRefusal(stripped))
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

        /// <summary>The first non-empty line of <paramref name="s"/>, trimmed.</summary>
        internal static string FirstLine(string s)
        {
            foreach (string line in s.Split('\r', '\n'))
                if (line.Trim().Length > 0) return line.Trim();
            return s.Trim();
        }
    }
}
