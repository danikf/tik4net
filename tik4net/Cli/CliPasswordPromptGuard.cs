namespace tik4net.Cli
{
    /// <summary>
    /// Whether a terminal may send its VT100 replies: never while RouterOS's change-password prompt (<c>new password&gt;</c>,
    /// <c>repeat new password&gt;</c>) is the line under the cursor. Fed the same decoded text the cursor model is.
    /// </summary>
    /// <remarks>
    /// A probe answer is keystrokes like any other, and typed into that prompt it becomes password input: two matching
    /// entries (the same cursor report answered twice, say) would change the router's admin password. While the prompt is
    /// up, the only byte a login sends is the Ctrl-C that skips it. The live check found the VT100 exchange always
    /// finishes before the prompt is drawn, so this withholds nothing in a normal login; it covers a re-opened terminal and
    /// a future RouterOS. The cursor model is still fed every chunk, so its position stays true.
    /// </remarks>
    internal sealed class CliPasswordPromptGuard
    {
        /// <summary>The longest line kept: a prompt is short, and a long line of output cannot be one.</summary>
        internal const int MaxLineLength = 256;

        private string _line = string.Empty;

        /// <summary>Appends <paramref name="decodedChunk"/> to the current screen line and says whether a reply may be sent now.</summary>
        internal bool MayAnswer(string decodedChunk)
        {
            string text = _line + (decodedChunk ?? string.Empty);
            int lineBreak = text.LastIndexOf('\n');
            if (lineBreak >= 0)
                text = text.Substring(lineBreak + 1);
            if (text.Length > MaxLineLength * 4)
                text = text.Substring(text.Length - MaxLineLength * 4);   // raw, before the escape sequences go

            string stripped = VtStripper.StripAnsi(text);
            if (stripped.Length > MaxLineLength)
                stripped = stripped.Substring(stripped.Length - MaxLineLength);
            _line = text;
            return !RouterOsCliLogin.IsChangePasswordNag(stripped);
        }
    }
}
