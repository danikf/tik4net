using System;
using System.Collections.Generic;
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
    /// answer truthfully so negotiation completes and output flows. (See winbox-terminal-findings.md §3.)
    /// </summary>
    internal sealed class Vt100State
    {
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
                        .Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
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
