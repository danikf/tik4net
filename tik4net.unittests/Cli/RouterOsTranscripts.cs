namespace tik4net.unittests.Cli
{
    /// <summary>
    /// RouterOS terminal transcripts, <b>captured off the wire</b> and stored verbatim (P2.24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Source: RouterOS <b>7.23.2</b> on the lab CHR over Telnet (TCP 23), 2026-08-14, recorded with the
    /// same IAC/VT100 answering <c>Tools/probes/telnet-cli-probe.ps1</c> does. Phase boundaries are the
    /// client's own sends, so each constant is exactly what one <c>readUntil</c> sees.
    /// </para>
    /// <para>
    /// Nothing here may be edited to make a test pass — a transcript is evidence. The only liberty taken is
    /// eliding the decorative ASCII logo lines from the banner, marked where it happens; every structural
    /// element (blank-line run, version line, <c>ESC[9999B</c> scroll, the doubled nag repaint, the prompt)
    /// is byte-faithful. To add another RouterOS version, capture it and add a block — that is the point of
    /// the file.
    /// </para>
    /// </remarks>
    internal static class RouterOsTranscripts
    {
        private const string Esc = "";

        // ── RouterOS 7.23.2, Telnet, admin with an EMPTY password ─────────────

        /// <summary>Connect → <c>Login:</c>. The leading bytes are the tail of the IAC negotiation.</summary>
        internal const string V7232_Login = "Login: ";

        /// <summary>After the user name: the echo, then the password prompt.</summary>
        internal const string V7232_Password = "admin+ct\r\nPassword: ";

        /// <summary>
        /// After the password: blank-line run, banner, <c>Press F1 for help</c>, the scroll escapes, the
        /// change-password nag — emitted <b>twice</b>, the second one a repaint of the same line.
        /// </summary>
        internal const string V7232_BannerAndNag =
            "\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n"
            + "\n  MMM      MMM       KKK                          TTTTTTTTTTT      KKK\r\n"
            /* logo lines elided — decorative only */
            + "\n  MikroTik RouterOS 7.23.2 (c) 1999-2026       https://www.mikrotik.com/\r\n\r\n\r\n"
            + "Press F1 for help\r\n\r" + Esc + "[9999B\r" + Esc + "[9999B\r\n\r\n\r\n"
            + "Change your password (Ctrl-C to skip)\r\n\r\r\r" + Esc + "[9999Bnew password> \rnew password> ";

        /// <summary>After Ctrl-C: the shell prompt, preceded by the usual redraw.</summary>
        internal const string V7232_PromptAfterNag = "\r\n\r\r\r\r" + Esc + "[9999B[admin@CHR] > ";

        /// <summary>
        /// After a <b>wrong</b> password: the refusal, then the login dialogue starts over.
        /// </summary>
        /// <remarks>
        /// The wording — <c>"Login failed, incorrect username or password"</c> — matched none of the five
        /// phrases <c>RouterOsCliLogin.IsLoginFailure</c> carried before P2.24, which cost the full 30 s
        /// receive deadline on every rejected CLI login. The trailing <c>Login:</c> is the signal that does
        /// not depend on wording at all.
        /// </remarks>
        internal const string V7232_LoginRefused =
            "\r\nLogin failed, incorrect username or password\r\n\r\nLogin: ";

        /// <summary>The shell prompt on its own, as a transport that authenticated below the terminal sees it.</summary>
        internal const string V7232_PromptOnly = "\r\n\r\r\r\r" + Esc + "[9999B[admin@CHR] > ";

        /// <summary>Safe Mode prompt as 7.23.2 emits it — the <c>&gt;</c> is replaced by the token (P2.31).</summary>
        internal const string V7232_SafeModePrompt = "\r\r\r" + Esc + "[9999B[admin@CHR] <SAFE> ";
    }
}
