using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// MAC-Telnet terminal client (UDP 20561, client_type=0x0015).
    /// After EC-SRP5 auth the DATA packets carry raw VT100 bytes (unencrypted).
    /// All terminal I/O is synchronous (matching the verified PoC), wrapped in
    /// Task.Run so callers remain async.
    /// </summary>
    internal sealed class MacTelnetUdpClient : MacLayerTransport
    {
        private const ushort CLIENT_TYPE = 0x0015;
        private const int    SettleMs    = 150;
        // Poll interval for UdpClient.Receive timeout (ms). Matches PoC.
        private const int    PollMs      = 500;

        // Very wide terminal — prevents line-wrapping of long ':put … as-value' records.
        // RouterOS probes terminal width with 'ESC[9999C ESC[6n', so the cursor-position reply is
        // capped at ~10000 columns; the width here must exceed that for the full width to be
        // advertised (a value <10000 would itself clamp the reply and re-introduce wrapping, which
        // corrupts as-value parsing — see findings-mactelnet.md).
        private readonly Vt100State     _vt100 = new Vt100State(65535, 25);
        private readonly Encoding       _encoding;
        private readonly int            _receiveTimeoutMs;
        private readonly int            _loginTimeoutMs;

        internal MacTelnetUdpClient(Encoding encoding, int receiveTimeoutMs, int loginTimeoutMs, string routerMac)
        {
            _encoding         = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
            _loginTimeoutMs   = loginTimeoutMs > 0 ? loginTimeoutMs : receiveTimeoutMs;
            RouterMacOverride = routerMac;
        }

        // ── Login ─────────────────────────────────────────────────────────────

        internal Task LoginAsync(string host, string user, string pass, CancellationToken ct)
        {
            // All socket operations are synchronous inside Task.Run so that
            // ReceiveTimeout (SO_RCVTIMEO) is never mixed with ReceiveAsync.
            // Mixing caused SO_RCVTIMEO to be ignored on .NET Framework 4.8.
            return Task.Run(() =>
            {
                BaseConnect(host, CLIENT_TYPE);
                Authenticate(user, pass);       // EC-SRP5 auth (sync, in base class)
                WaitForPromptSync();
                DrainSync(250);
            }, ct);
        }

        // ── Command execution ─────────────────────────────────────────────────

        internal Task<string> SendCommandAndReadAsync(string command, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                string cmd = CliOutputHelper.InjectWithoutPaging(command);
                SendTerminalBytes(_encoding.GetBytes(cmd + "\r"));
                string raw = ReadCommandResponseSync();
                return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd);
            }, ct);
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a graceful session-end sequence: first asks RouterOS to exit the console
        /// (<c>/quit</c>), then sends the MAC-layer <c>PKT_END</c>. Errors are ignored.
        /// </summary>
        internal void TryCloseSession()
        {
            try { Send(PKT_DATA, _encoding.GetBytes("/quit\r")); } catch { /* ignore */ }
            try { Send(PKT_END, null); } catch { /* ignore */ }
        }

        // ── Synchronous terminal I/O (PoC-style) ──────────────────────────────

        private void SendTerminalBytes(byte[] data) => Send(PKT_DATA, data);

        /// <summary>
        /// Waits until the RouterOS shell prompt appears (post-auth VT100 startup).
        /// Handles the change-password nag and VT100 cursor-probe replies.
        /// Pure synchronous socket receive with UdpClient.ReceiveTimeout — no ReceiveAsync.
        /// </summary>
        private void WaitForPromptSync()
        {
            var sb = new StringBuilder();
            bool nagSent = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < _loginTimeoutMs)
            {
                _udp.Client.ReceiveTimeout = PollMs;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (!TryParsePacket(pkt, out byte type, out uint counter, out byte[] payload))
                        continue;

                    if (type == PKT_ACK) continue;
                    if (type == PKT_PING) { SendPong(counter); continue; }
                    if (type != PKT_DATA) continue;

                    if (!AckData(counter, payload.Length)) continue;   // duplicate retransmit
                    if (IsControlPacket(payload))
                        continue;

                    string text = _encoding.GetString(payload);
                    sb.Append(text);

                    // Answer RouterOS cursor-probe negotiation immediately. This path is timing
                    // sensitive: if the cursor-position reply is delayed (e.g. by per-packet logging),
                    // RouterOS falls back to a tiny terminal width and wraps its output, breaking
                    // prompt detection. Keep this loop free of expensive work.
                    foreach (string reply in _vt100.Process(text))
                        SendTerminalBytes(_encoding.GetBytes(reply));

                    string stripped = VtStripper.StripAnsi(sb.ToString());

                    if (!nagSent && RouterOsCliLogin.IsChangePasswordNag(stripped))
                    {
                        SendTerminalBytes(new byte[] { 0x03 });  // Ctrl-C
                        nagSent = true;
                        sb.Clear();
                        continue;
                    }

                    if (RouterOsCliLogin.IsShellPrompt(stripped))
                        return;
                }
                catch (SocketException) { /* poll timeout — continue loop */ }
            }

            throw new TimeoutException("MAC-Telnet: timed out waiting for shell prompt.");
        }

        /// <summary>
        /// Reads a command response, requiring the prompt to be stable for
        /// <see cref="SettleMs"/> before returning.
        /// </summary>
        private string ReadCommandResponseSync()
        {
            var sb = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DateTime? settleUntil = null;

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                _udp.Client.ReceiveTimeout = PollMs;
                bool gotData = false;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (!TryParsePacket(pkt, out byte type, out uint counter, out byte[] payload))
                        continue;

                    if (type == PKT_ACK) continue;
                    if (type == PKT_PING) { SendPong(counter); continue; }
                    if (type != PKT_DATA) continue;

                    if (!AckData(counter, payload.Length)) continue;   // duplicate retransmit
                    if (!IsControlPacket(payload))
                    {
                        string text = _encoding.GetString(payload);
                        sb.Append(text);
                        foreach (string reply in _vt100.Process(text))
                            SendTerminalBytes(_encoding.GetBytes(reply));
                        gotData = true;
                    }
                }
                catch (SocketException) { /* poll timeout */ }

                if (gotData)
                    settleUntil = null;

                string stripped = VtStripper.StripAnsi(sb.ToString());
                if (RouterOsCliLogin.IsShellPrompt(stripped))
                {
                    if (settleUntil == null)
                        settleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
                    else if (DateTime.UtcNow >= settleUntil.Value)
                        return stripped;
                }
            }

            return VtStripper.StripAnsi(sb.ToString());
        }

        /// <summary>
        /// Discards residual bytes until the socket stays quiet for <paramref name="quietMs"/>.
        /// </summary>
        private void DrainSync(int quietMs)
        {
            var quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
            _udp.Client.ReceiveTimeout = 50;
            while (DateTime.UtcNow < quietDeadline)
            {
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (!TryParsePacket(pkt, out byte type, out uint counter, out byte[] payload))
                        continue;
                    if (type == PKT_DATA)
                    {
                        bool fresh = AckData(counter, payload.Length);
                        if (fresh && !IsControlPacket(payload))
                        {
                            string text = _encoding.GetString(payload);
                            foreach (string reply in _vt100.Process(text))
                                SendTerminalBytes(_encoding.GetBytes(reply));
                        }
                    }
                    else if (type == PKT_PING) { SendPong(counter); }
                    // Reset quiet window — keep draining while data arrives.
                    quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
                }
                catch (SocketException) { /* quiet timeout — exit */ break; }
            }
        }

        // Parses raw datagram. Skips own-echo. Returns false if invalid or own echo.
        private bool TryParsePacket(byte[] pkt, out byte type, out uint counter, out byte[] payload)
        {
            type = 0; counter = 0; payload = new byte[0];
            if (pkt == null || pkt.Length < 22) return false;

            // Skip own echo (srcMac == _localMac)
            bool ownEcho = true;
            for (int i = 0; i < 6; i++)
                if (pkt[2 + i] != _localMac[i]) { ownEcho = false; break; }
            if (ownEcho) return false;

            type    = pkt[1];
            counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                      ((uint)pkt[20] <<  8) |  pkt[21];
            payload = pkt.Length > 22 ? new byte[pkt.Length - 22] : new byte[0];
            if (payload.Length > 0) Buffer.BlockCopy(pkt, 22, payload, 0, payload.Length);
            return true;
        }
    }
}
