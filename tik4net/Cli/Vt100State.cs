using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace tik4net.Cli
{
    /// <summary>
    /// Minimal VT100 cursor state machine used to answer RouterOS terminal dimension probes on PTY
    /// transports (Telnet, MAC-Telnet, WinBox mepty). RouterOS performs a multi-round cursor-probe
    /// negotiation before it will render command output: it moves the cursor and issues
    /// <c>ESC[6n</c> (Device Status Report) expecting the client to reply with the real cursor
    /// position (<c>ESC[row;colR</c>). If the client never answers — or always answers <c>1;1</c> —
    /// RouterOS assumes a 1×1 terminal and either repeats the probe indefinitely or fails to emit
    /// output (manifesting as <c>\r\r\r\r] &gt; </c> with no data). Tracking the cursor here lets us
    /// answer truthfully so negotiation completes and output flows. (See Docs/findings-winbox-terminal.md §3.)
    /// </summary>
    internal sealed class Vt100State
    {
        /// <summary>
        /// Terminal width every PTY transport advertises, and <see cref="RouterOsHeight"/> the height.
        /// </summary>
        /// <remarks>
        /// <para>RouterOS measures the width by parking the cursor at column 1, sending
        /// <c>ESC[9999C</c> (cursor forward) and asking where it ended up, then printing one space and
        /// asking again. The answer to the second question is the one that matters: a terminal that
        /// reports the next column has told RouterOS it does <b>not</b> wrap, so RouterOS wraps the
        /// output itself and inserts a <c>\r\n</c> into the byte stream; a terminal that reports row+1,
        /// column 1 has shown it wraps on its own, and RouterOS then leaves the stream alone.</para>
        /// <para>So the advertised width has to be a column the cursor-forward probe can actually
        /// <b>reach</b> — at most <c>1 + 9999</c>. Above that, <see cref="Width"/> never saturates, the
        /// wrap is never demonstrated, and RouterOS hard-wraps at 10 000 characters. Measured on 7.24
        /// against 681 queue trees: at 65535 the reply is <c>ESC[1;10000R</c> and a <c>\r\n</c> lands
        /// every 10 002 characters — mid-token, which the as-value parser reads as a multi-value
        /// continuation (findings-cli.md §1) and reports as a type error on a field the router never sent. At 4096 the
        /// reply is <c>ESC[1;4096R</c> followed by <c>ESC[2;1R</c> and the 316 KB response carries no
        /// wrap at all. See Docs/findings-cli.md §6.</para>
        /// </remarks>
        public const int RouterOsWidth = 4096;

        /// <summary>Terminal height advertised with <see cref="RouterOsWidth"/>.</summary>
        public const int RouterOsHeight = 25;

        /// <summary>
        /// The terminal every PTY transport advertises to RouterOS. Shared so the width cannot drift
        /// between transports: a transport advertising an unreachable width corrupts every response longer
        /// than 10 000 characters, and does it silently.
        /// </summary>
        public static Vt100State ForRouterOs() => new Vt100State(RouterOsWidth, RouterOsHeight);

        public int Width  { get; }
        public int Height { get; }
        public int Row    { get; private set; } = 1;
        public int Col    { get; private set; } = 1;

        private enum St { Normal, Esc, Csi }
        private St     _st    = St.Normal;
        private string _param = "";

        public Vt100State(int width, int height) { Width = width; Height = height; }

        /// <summary>Process incoming server text; returns the reply strings to send back (probe answers).</summary>
        public List<string> Process(string text)
        {
            var replies = new List<string>();
            if (string.IsNullOrEmpty(text))
                return replies;

            foreach (char c in text)
            {
                switch (_st)
                {
                    case St.Normal:
                        if      (c == '\x1B')  { _st = St.Esc; }
                        else if (c == '\x9B')  { _st = St.Csi; _param = ""; }  // 8-bit CSI
                        else if (c == '\r')    { Col = 1; }
                        else if (c == '\n')    { Row = Math.Min(Row + 1, Height); }
                        else if (c >= ' ')
                        {
                            Col++;
                            if (Col > Width) { Col = 1; Row = Math.Min(Row + 1, Height); }
                        }
                        break;

                    case St.Esc:
                        if      (c == '[')  { _st = St.Csi; _param = ""; }
                        else if (c == 'Z')  { replies.Add("\x1B[?1;0c"); _st = St.Normal; }  // DECID
                        else if (c == 'D')  { Row = Math.Min(Row + 1, Height); _st = St.Normal; } // IND
                        else if (c == 'M')  { Row = Math.Max(Row - 1, 1);      _st = St.Normal; } // RI
                        else                { _st = St.Normal; }
                        break;

                    case St.Csi:
                        if (c >= '0' && c <= '9' || c == ';' || c == '?')
                            { _param += c; }
                        else
                            { HandleCsi(c, replies); _st = St.Normal; _param = ""; }
                        break;
                }
            }
            return replies;
        }

        private void HandleCsi(char cmd, List<string> replies)
        {
            string p = _param.TrimStart('?');
            int[] ns = p.Split(new[] { ';' }, StringSplitOptions.None)
                        .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
                        .ToArray();
            int n1 = ns.Length > 0 ? ns[0] : 0;
            int n2 = ns.Length > 1 ? ns[1] : 0;

            switch (cmd)
            {
                case 'A': Row = Math.Max(1,      Row - Math.Max(1, n1)); break;  // CUU
                case 'B': Row = Math.Min(Height, Row + Math.Max(1, n1)); break;  // CUD
                case 'C': Col = Math.Min(Width,  Col + Math.Max(1, n1)); break;  // CUF
                case 'D': Col = Math.Max(1,      Col - Math.Max(1, n1)); break;  // CUB
                case 'H': case 'f':                                              // CUP
                    Row = n1 > 0 ? Math.Min(Height, n1) : 1;
                    Col = n2 > 0 ? Math.Min(Width,  n2) : 1;
                    break;
                case 'n':  // DSR — cursor position report request
                    if (n1 == 6) replies.Add($"\x1B[{Row};{Col}R");
                    break;
                // Mode set/reset (h/l) and erase commands — ignored for cursor tracking
            }
        }
    }
}
