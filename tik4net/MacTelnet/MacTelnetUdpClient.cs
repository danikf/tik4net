using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.MacTelnet
{
    /// <summary>
    /// MAC-Telnet terminal client (UDP 20561, client_type=0x0015).
    /// After EC-SRP5 auth the DATA packets carry raw VT100 bytes (unencrypted).
    /// </summary>
    internal sealed class MacTelnetUdpClient : MacLayerTransport
    {
        private const ushort CLIENT_TYPE = 0x0015;
        private const int    SettleMs    = 120;

        // Wide terminal — prevents line-wrapping of long ':put … as-value' records.
        private readonly Vt100State    _vt100 = new Vt100State(4096, 25);
        private readonly Encoding      _encoding;
        private readonly int           _receiveTimeoutMs;
        private readonly Action<string> _diagnostic;

        internal MacTelnetUdpClient(Encoding encoding, int receiveTimeoutMs, string routerMac,
            Action<string> diagnostic = null)
        {
            _encoding         = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
            RouterMacOverride = routerMac;
            _diagnostic       = diagnostic;
        }

        // ── Login ─────────────────────────────────────────────────────────────

        internal async Task LoginAsync(string host, string user, string pass, CancellationToken ct)
        {
            BaseConnect(host, CLIENT_TYPE);
            await AuthenticateAsync(user, pass, ct).ConfigureAwait(false);
            await WaitForPromptAsync(_receiveTimeoutMs, ct).ConfigureAwait(false);
            await DrainAsync(250, ct).ConfigureAwait(false);
        }

        // ── Command execution ─────────────────────────────────────────────────

        internal async Task<string> SendCommandAndReadAsync(string command, CancellationToken ct)
        {
            string cmd = CliOutputHelper.InjectWithoutPaging(command);
            SendTerminalBytes(_encoding.GetBytes(cmd + "\r"));
            string raw = await ReadCommandResponseAsync(ct).ConfigureAwait(false);
            return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd);
        }

        // ── Private terminal I/O ──────────────────────────────────────────────

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

        private void SendTerminalBytes(byte[] data) => Send(PKT_DATA, data);

        /// <summary>
        /// Waits until the RouterOS shell prompt appears (post-auth VT100 startup).
        /// Handles the change-password nag (sends Ctrl-C) and VT100 cursor-probe replies.
        /// </summary>
        private async Task WaitForPromptAsync(int timeoutMs, CancellationToken ct)
        {
            var sb = new StringBuilder();
            bool nagSent = false;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            _diagnostic?.Invoke("[MacTelnet] WaitForPromptAsync start");

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var pkt = await TryReceivePacketAsync(500, ct).ConfigureAwait(false);
                if (pkt == null) { _diagnostic?.Invoke("."); continue; }

                var (type, counter, payload) = pkt.Value;
                _diagnostic?.Invoke($"[MacTelnet] pkt type={type} ctr={counter} paylen={payload.Length}");

                if (type == PKT_ACK) continue;
                if (type == PKT_PING) { SendPong(counter); continue; }
                if (type != PKT_DATA) continue;

                SendAck(counter);
                if (IsControlPacket(payload)) continue;

                string text = _encoding.GetString(payload);
                sb.Append(text);

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

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("MAC-Telnet: timed out waiting for shell prompt.");
        }

        /// <summary>
        /// Reads a command response, requiring the prompt to be stable for <see cref="SettleMs"/>
        /// before returning (same settle logic as TelnetClient).
        /// </summary>
        private async Task<string> ReadCommandResponseAsync(CancellationToken ct)
        {
            var sb       = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(_receiveTimeoutMs);
            DateTime? settleUntil = null;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var pkt = await TryReceivePacketAsync(500, ct).ConfigureAwait(false);

                bool gotData = pkt != null;
                if (gotData)
                {
                    var (type, counter, payload) = pkt.Value;

                    if (type == PKT_ACK) { gotData = false; }
                    else if (type == PKT_PING) { SendPong(counter); gotData = false; }
                    else if (type == PKT_DATA)
                    {
                        SendAck(counter);
                        if (!IsControlPacket(payload))
                        {
                            string text = _encoding.GetString(payload);
                            sb.Append(text);
                            foreach (string reply in _vt100.Process(text))
                                SendTerminalBytes(_encoding.GetBytes(reply));
                        }
                        else
                        {
                            gotData = false;
                        }
                    }
                    else
                    {
                        gotData = false;
                    }
                }

                if (gotData)
                    settleUntil = null;  // fresh data → prompt not yet stable

                string stripped = VtStripper.StripAnsi(sb.ToString());
                if (RouterOsCliLogin.IsShellPrompt(stripped))
                {
                    if (settleUntil == null)
                        settleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);
                    else if (DateTime.UtcNow >= settleUntil.Value)
                        return stripped;
                }
            }

            ct.ThrowIfCancellationRequested();
            return VtStripper.StripAnsi(sb.ToString());
        }

        /// <summary>
        /// Discards any residual bytes until the UDP socket stays quiet for <paramref name="quietMs"/>.
        /// Still processes VT100 probes and ACKs incoming DATA.
        /// </summary>
        private async Task DrainAsync(int quietMs, CancellationToken ct)
        {
            var quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
            while (DateTime.UtcNow < quietDeadline && !ct.IsCancellationRequested)
            {
                var pkt = await TryReceivePacketAsync(50, ct).ConfigureAwait(false);
                if (pkt == null) continue;  // poll returned nothing → still within quiet window

                var (type, counter, payload) = pkt.Value;
                if (type == PKT_DATA)
                {
                    SendAck(counter);
                    if (!IsControlPacket(payload))
                    {
                        string text = _encoding.GetString(payload);
                        foreach (string reply in _vt100.Process(text))
                            SendTerminalBytes(_encoding.GetBytes(reply));
                    }
                }
                else if (type == PKT_PING)
                {
                    SendPong(counter);
                }

                // Reset quiet window — keep draining while data arrives.
                quietDeadline = DateTime.UtcNow.AddMilliseconds(quietMs);
            }
        }
    }
}
