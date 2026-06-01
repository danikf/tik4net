using System;
using System.Collections.Generic;

namespace tik4net.Telnet
{
    /// <summary>
    /// Handles Telnet IAC (Interpret As Command) option negotiation.
    /// Strategy: refuse every option — reply WONT to DO requests, DONT to WILL requests.
    /// RouterOS Telnet works in line/char mode without any options negotiated.
    /// </summary>
    internal static class TelnetNegotiator
    {
        // IAC byte codes
        private const byte IAC  = 255;
        private const byte WILL = 251;
        private const byte WONT = 252;
        private const byte DO   = 253;
        private const byte DONT = 254;
        private const byte SB   = 250;  // sub-negotiation begin
        private const byte SE   = 240;  // sub-negotiation end

        /// <summary>
        /// Processes <paramref name="incoming"/> bytes: strips IAC sequences from the data
        /// and builds the negotiation reply bytes to send back to the server.
        /// </summary>
        /// <param name="incoming">Raw bytes received from the server.</param>
        /// <param name="reply">Bytes that must be sent back to the server (may be empty).</param>
        /// <returns>Cleaned data bytes with all IAC sequences removed.</returns>
        internal static byte[] FilterAndRespond(byte[] incoming, out byte[] reply)
        {
            var data    = new List<byte>(incoming.Length);
            var replyBuf = new List<byte>(12);

            int i = 0;
            while (i < incoming.Length)
            {
                byte b = incoming[i];

                if (b != IAC)
                {
                    data.Add(b);
                    i++;
                    continue;
                }

                // b == IAC
                if (i + 1 >= incoming.Length)
                {
                    // Truncated IAC at end of buffer — skip it (will be re-processed if needed)
                    i++;
                    break;
                }

                byte cmd = incoming[i + 1];

                if (cmd == IAC)
                {
                    // IAC IAC → literal 0xFF
                    data.Add(0xFF);
                    i += 2;
                    continue;
                }

                if (cmd == SB)
                {
                    // Skip IAC SB ... IAC SE
                    i += 2; // skip IAC SB
                    while (i < incoming.Length)
                    {
                        if (incoming[i] == IAC && i + 1 < incoming.Length && incoming[i + 1] == SE)
                        {
                            i += 2; // skip IAC SE
                            break;
                        }
                        i++;
                    }
                    continue;
                }

                if (cmd == DO || cmd == WILL)
                {
                    // Need option byte
                    if (i + 2 >= incoming.Length)
                    {
                        // Truncated — skip what we have
                        i += 2;
                        break;
                    }
                    byte option = incoming[i + 2];
                    if (cmd == DO)
                    {
                        // IAC DO x → reply IAC WONT x
                        replyBuf.Add(IAC);
                        replyBuf.Add(WONT);
                        replyBuf.Add(option);
                    }
                    else // WILL
                    {
                        // IAC WILL x → reply IAC DONT x
                        replyBuf.Add(IAC);
                        replyBuf.Add(DONT);
                        replyBuf.Add(option);
                    }
                    i += 3;
                    continue;
                }

                if (cmd == DONT || cmd == WONT)
                {
                    // Acknowledgements — skip silently (option byte follows)
                    i += (i + 2 < incoming.Length) ? 3 : 2;
                    continue;
                }

                // Other 2-byte IAC commands (NOP, DM, BRK, GA, EOR, …) — skip
                i += 2;
            }

            reply = replyBuf.ToArray();
            return data.ToArray();
        }
    }
}
