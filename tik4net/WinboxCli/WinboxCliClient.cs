using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;
using tik4net.Winbox;

namespace tik4net.WinboxCli
{
    /// <summary>
    /// WinBox CLI terminal client. On top of any <see cref="IWinboxM2Channel"/> (TCP 8291 or MAC-layer
    /// UDP 20561) it opens the mepty (terminal PTY) handler and drives a persistent RouterOS CLI
    /// session — the encrypted-transport equivalent of <c>MacTelnetUdpClient</c>. All terminal I/O is
    /// synchronous, wrapped in <see cref="Task.Run(Action)"/> so callers stay async.
    /// </summary>
    /// <remarks>
    /// Transport-agnostic: the injected channel decides whether M2 messages travel over TCP or the MAC
    /// layer, so this same engine backs both <c>WinboxCliConnection</c> and <c>WinboxCliMacConnection</c>.
    /// </remarks>
    internal sealed class WinboxCliClient : IDisposable
    {
        // mepty (terminal PTY) handler + commands — see WinboxM2Protocol.Mepty.
        private const int SettleMs       = 150;
        // Receive timeout for one encrypted frame. Must be generous: a timeout that fires mid-frame
        // leaves the TCP stream misaligned and every subsequent read fails (see winbox findings §2).
        // We gate every read behind DataAvailable so this timeout only bounds a frame already arriving.
        private const int FrameTimeoutMs = 5000;
        private const int PollSleepMs    = 20;

        // Very wide terminal — prevents line-wrapping of long ':put … as-value' records. RouterOS probes
        // width with 'ESC[9999C ESC[6n', so the cursor reply caps near 10000 columns; the width here must
        // exceed that so the full width is advertised (see findings-mactelnet.md / chapter E).
        private readonly Vt100State _vt100 = new Vt100State(65535, 25);
        private readonly IWinboxM2Channel _session;
        private readonly Encoding _encoding;
        private readonly int _receiveTimeoutMs;
        private readonly int _loginTimeoutMs;

        private int _sessionId = -1;
        private int _counter = 1;

        internal WinboxCliClient(IWinboxM2Channel channel, Encoding encoding, int receiveTimeoutMs, int loginTimeoutMs)
        {
            _session          = channel ?? throw new ArgumentNullException(nameof(channel));
            _encoding         = encoding ?? Encoding.UTF8;
            _receiveTimeoutMs = receiveTimeoutMs;
            _loginTimeoutMs   = loginTimeoutMs > 0 ? loginTimeoutMs : receiveTimeoutMs;
        }

        // ── Login ─────────────────────────────────────────────────────────────

        internal Task LoginAsync(string host, int port, string user, string pass, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                int connectTimeout = Math.Max(_receiveTimeoutMs, _loginTimeoutMs);
                _session.Open(host, port, user, pass, connectTimeout);

                // Open one mepty terminal and keep it for the whole connection. The password is supplied
                // here (not via a Login:/Password: prompt) — auth already happened at the M2 layer, so the
                // session starts directly at the RouterOS shell (or the change-password nag). The login
                // size hint stays at the PoC-proven 80x25 (RouterOS rejects oversized values with an
                // error response carrying no SESSION_ID); the real width comes from the VT100 cursor-probe
                // answered by the wide _vt100 below.
                _sessionId = OpenTerminalSession(pass, "vt102", 80, 25);
                SendTerminalReady(_sessionId);

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
                SendInput(_encoding.GetBytes(cmd + "\r"));
                string raw = ReadCommandResponseSync();
                return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd);
            }, ct);
        }

        /// <summary>
        /// Sends raw bytes (a control key such as Ctrl+X — no carriage return, no paging injection) and
        /// returns the ANSI-stripped response read up to the next stable shell prompt. Used for Safe Mode.
        /// </summary>
        internal Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                SendInput(raw);
                return VtStripper.StripAnsi(ReadCommandResponseSync());
            }, ct);
        }

        /// <summary>
        /// Sends raw bytes (e.g. <c>&lt;stem&gt;&lt;Tab&gt;</c> for Tab-completion) and reads the reaction until
        /// the terminal goes quiet for <paramref name="quietMs"/> — the completion listing does not end in a
        /// shell prompt (RouterOS redraws the prompt with the echoed stem), so it must be read on a settle
        /// window rather than a prompt match. ANSI-stripped.
        /// </summary>
        internal Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                SendInput(raw);
                return ReadUntilQuietSync(quietMs);
            }, ct);
        }

        // ── Close ─────────────────────────────────────────────────────────────

        /// <summary>Asks RouterOS to leave the console (<c>/quit</c>); errors are ignored.</summary>
        internal void TryCloseSession()
        {
            try { if (_sessionId >= 0) SendInput(_encoding.GetBytes("/quit\r")); } catch { /* ignore */ }
        }

        public void Dispose() => _session.Dispose();

        // ── mepty message building (handler [76]) ─────────────────────────────

        private int OpenTerminalSession(string password, string terminalType, int cols, int rows)
        {
            if (!_session.IsEncrypted)
                throw new NotSupportedException(
                    "WinBox terminal (mepty) is only supported over the encrypted EC-SRP5 channel " +
                    "(RouterOS 6.43+). The legacy MD5 path does not carry an encrypted terminal session.");

            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.BoolSys(WinboxM2Protocol.SysKey.ReplyExpected, true),
                _session.NextReqIdField(),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Login),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Password, password),
                M2Message.StringUser(WinboxM2Protocol.Mepty.Key.Input, terminalType),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Cols, cols),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Rows, rows));
            byte[] resp = _session.SendReceive(msg, FrameTimeoutMs);
            return M2Message.ParseSessionId(resp);
        }

        private void SendTerminalReady(int sessionId)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Counter, 0));
            _session.Send(msg);
        }

        private void SendInput(byte[] keystrokes)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(WinboxM2Protocol.Mepty.Handler), M2Message.SysFrom(),
                M2Message.SessionIdField(_sessionId),
                M2Message.U32Sys(WinboxM2Protocol.SysKey.Command, WinboxM2Protocol.Mepty.Data),
                M2Message.RawUser(WinboxM2Protocol.Mepty.Key.Input, keystrokes),
                M2Message.U32User(WinboxM2Protocol.Mepty.Key.Counter, _counter++));
            _session.Send(msg);
        }

        /// <summary>Receives one frame and returns the terminal payload (user key 2), or null.</summary>
        private byte[] ReceiveTerminalChunk(int timeoutMs)
        {
            byte[] resp = _session.Receive(timeoutMs);
            if (resp == null) return null;
            return M2Message.ParseUserBytes(resp, WinboxM2Protocol.Mepty.Key.Input);
        }

        // ── Synchronous terminal loops ────────────────────────────────────────

        /// <summary>
        /// Waits until the RouterOS shell prompt appears, answering VT100 cursor-probe negotiation and
        /// dismissing the change-password nag with Ctrl-C.
        /// </summary>
        private void WaitForPromptSync()
        {
            var sb = new StringBuilder();
            bool nagSent = false;
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < _loginTimeoutMs)
            {
                if (!_session.DataAvailable) { Thread.Sleep(PollSleepMs); continue; }

                byte[] chunk;
                try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                catch (IOException) { break; }
                if (chunk == null) continue;

                string text = _encoding.GetString(chunk);
                sb.Append(text);

                foreach (string reply in _vt100.Process(text))
                    SendInput(_encoding.GetBytes(reply));

                string stripped = VtStripper.StripAnsi(sb.ToString());

                if (!nagSent && RouterOsCliLogin.IsChangePasswordNag(stripped))
                {
                    SendInput(new byte[] { 0x03 });  // Ctrl-C
                    nagSent = true;
                    sb.Clear();
                    continue;
                }

                if (RouterOsCliLogin.IsShellPrompt(stripped))
                    return;
            }

            throw new TimeoutException("WinBox: timed out waiting for shell prompt.");
        }

        /// <summary>
        /// Reads a command response, requiring the prompt to be stable for <see cref="SettleMs"/>
        /// before returning (the line-editor repaints the prompt, so a single prompt sighting is not
        /// proof the output is complete).
        /// </summary>
        private string ReadCommandResponseSync()
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            DateTime? settleUntil = null;

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                bool gotData = false;
                if (_session.DataAvailable)
                {
                    byte[] chunk;
                    try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                    catch (IOException) { break; }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        sb.Append(text);
                        foreach (string reply in _vt100.Process(text))
                            SendInput(_encoding.GetBytes(reply));
                        gotData = true;
                    }
                }
                else
                {
                    Thread.Sleep(PollSleepMs);
                }

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
        /// Accumulates the terminal reaction until the channel stays quiet for <paramref name="quietMs"/>
        /// after at least some data (or the receive deadline expires), answering VT100 probes. Returns the
        /// ANSI-stripped text. Used for Tab-completion (see <see cref="SendRawAndReadUntilQuietAsync"/>).
        /// </summary>
        private string ReadUntilQuietSync(int quietMs)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            DateTime lastData = DateTime.UtcNow;
            bool any = false;

            while (sw.ElapsedMilliseconds < _receiveTimeoutMs)
            {
                bool gotData = false;
                if (_session.DataAvailable)
                {
                    byte[] chunk;
                    try { chunk = ReceiveTerminalChunk(FrameTimeoutMs); }
                    catch (IOException) { break; }
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        sb.Append(text);
                        foreach (string reply in _vt100.Process(text))
                            SendInput(_encoding.GetBytes(reply));
                        gotData = true;
                        any = true;
                    }
                }
                else
                {
                    Thread.Sleep(PollSleepMs);
                }

                if (gotData)
                    lastData = DateTime.UtcNow;
                else if (any && (DateTime.UtcNow - lastData).TotalMilliseconds >= quietMs)
                    break;
            }

            return VtStripper.StripAnsi(sb.ToString());
        }

        /// <summary>Consumes residual frames until the channel stays quiet for <paramref name="quietMs"/>.</summary>
        private void DrainSync(int quietMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(quietMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!_session.DataAvailable) { Thread.Sleep(PollSleepMs); continue; }
                try
                {
                    byte[] chunk = ReceiveTerminalChunk(FrameTimeoutMs);
                    if (chunk != null)
                    {
                        string text = _encoding.GetString(chunk);
                        foreach (string reply in _vt100.Process(text))
                            SendInput(_encoding.GetBytes(reply));
                    }
                }
                catch (IOException) { break; }
                deadline = DateTime.UtcNow.AddMilliseconds(quietMs);  // keep draining while data flows
            }
        }
    }
}
