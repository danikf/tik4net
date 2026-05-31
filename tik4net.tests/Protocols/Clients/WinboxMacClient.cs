// WinboxMacClient.cs — Winbox M2 over MAC transport (client_type=0x0f90) [EXPERIMENTAL]
// Uses MacLayerTransport for UDP framing + EC-SRP5 auth, M2Message + WinboxStreamCrypto for protocol.
// Extracted from MacLayerTest.cs.
//
// M2 frame in DATA packet (hypothesis — not verified against Wireshark capture):
//   [enc_len 2B BE][IV 16B][ciphertext]   — no TCP chunk wrapper
// Terminal access reuses the mepty handler [76] identical to Winbox TCP.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net.tests
{
    /// <summary>
    /// Winbox M2 client over MAC transport (UDP 20561, client_type=0x0f90).
    /// EXPERIMENTAL: M2 framing inside DATA packets is unverified against Wireshark capture.
    /// </summary>
    internal sealed class WinboxMacClient : MacLayerTransport
    {
        private const ushort CLIENT_TYPE = 0x0f90;
        private const int    SRC_ID      = 8;
        private int          _reqId;
        private string       _pass;

        public void Connect(string host, string user, string pass)
        {
            _pass = pass;
            BaseConnect(host, CLIENT_TYPE);
            Authenticate(user, pass);
        }

        // ── Encrypted M2 send / receive ──────────────────────────────────────────
        private byte[] SendRecvM2(byte[] m2, int timeoutMs = 5000)
        {
            byte[] frame = WinboxStreamCrypto.Encrypt(m2, _sendAesKey, _sendHmacKey);
            Send(PKT_DATA, frame);

            byte[] result = null;
            RecvUntil(timeoutMs, (type, payload, counter) =>
            {
                if (type == PKT_ACK) return false;
                if (type == PKT_PING) { SendPong(counter); return false; }
                if (type != PKT_DATA) return false;
                SendAck(counter);
                if (IsControlPacket(payload) || payload.Length < 18) return false;
                byte[] dec = TryDecrypt(payload);
                if (dec == null) return false;
                result = dec;
                return true;
            });
            return result;
        }

        private void SendM2(byte[] m2)
        {
            byte[] frame = WinboxStreamCrypto.Encrypt(m2, _sendAesKey, _sendHmacKey);
            Send(PKT_DATA, frame);
        }

        private byte[] TryDecrypt(byte[] payload)
        {
            try { return WinboxStreamCrypto.Decrypt(payload, _receiveAesKey); }
            catch { return null; }
        }

        // ── Terminal session via mepty [76] ──────────────────────────────────────
        private int NextReqId() => ++_reqId;

        private int OpenTerminalSession(string password)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(76), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true),
                M2Message.U8Sys(0xFF0006, (byte)NextReqId()),
                M2Message.U32Sys(0xFF0007, 0x0A0065),
                M2Message.StringUser(1, password),
                M2Message.StringUser(2, "vt102"),
                M2Message.U32User(3, 80),
                M2Message.U32User(4, 25));
            byte[] resp = SendRecvM2(msg, 8000);
            return M2Message.ParseSessionId(resp);
        }

        private void SendTerminalReady(int sessionId)
        {
            SendM2(M2Message.BuildM2(
                M2Message.SysToArr(76), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId),
                M2Message.U32Sys(0xFF0007, 0x0A0067),
                M2Message.U32User(3, 0)));
        }

        private void SendTerminalInput(int sessionId, byte[] keys, ref int counter)
        {
            SendM2(M2Message.BuildM2(
                M2Message.SysToArr(76), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId),
                M2Message.U32Sys(0xFF0007, 0x0A0067),
                M2Message.RawUser(2, keys),
                M2Message.U32User(3, counter++)));
        }

        private string RunTerminalCommand(string password, string command)
        {
            int sessionId = OpenTerminalSession(password);
            SendTerminalReady(sessionId);

            int counter = 1;
            var initSb = new StringBuilder();
            var term   = new Vt100State(80, 25);
            bool sentCtrlC = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Phase 1: negotiate terminal, wait for prompt
            while (sw.ElapsedMilliseconds < 15000)
            {
                _udp.Client.ReceiveTimeout = 500;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (pkt.Length < 22) continue;
                    byte   type   = pkt[1];
                    uint   ackCtr = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                                    ((uint)pkt[20] <<  8) |  pkt[21];
                    byte[] payload = pkt.Length > 22 ? pkt.Skip(22).ToArray() : new byte[0];

                    if (type == PKT_ACK) continue;
                    if (type == PKT_PING) { SendPong(ackCtr); continue; }
                    if (type != PKT_DATA) continue;
                    SendAck(ackCtr);

                    if (IsControlPacket(payload)) continue;
                    byte[] m2 = TryDecrypt(payload);
                    if (m2 == null) continue;
                    byte[] chunk = M2Message.ParseUserBytes(m2, 2);
                    if (chunk == null) continue;

                    string text = Encoding.UTF8.GetString(chunk);
                    initSb.Append(text);
                    foreach (string reply in term.Process(text))
                        SendTerminalInput(sessionId, Encoding.UTF8.GetBytes(reply), ref counter);

                    string stripped = CliParsing.StripAnsi(initSb.ToString());
                    if (!sentCtrlC && stripped.Contains("password>"))
                    {
                        SendTerminalInput(sessionId, new byte[] { 0x03 }, ref counter);
                        sentCtrlC = true; initSb.Clear(); continue;
                    }
                    if (stripped.Contains("] >")) break;
                }
                catch (SocketException) { /* poll timeout */ }
            }

            if (!CliParsing.StripAnsi(initSb.ToString()).Contains("] >")) return "";

            // Phase 2: send command, collect until prompt reappears
            var cmdSb = new StringBuilder();
            SendTerminalInput(sessionId, Encoding.UTF8.GetBytes(command + "\r"), ref counter);
            sw.Restart();
            while (sw.ElapsedMilliseconds < 8000)
            {
                _udp.Client.ReceiveTimeout = 500;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (pkt.Length < 22) continue;
                    byte   type   = pkt[1];
                    uint   ackCtr = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                                    ((uint)pkt[20] <<  8) |  pkt[21];
                    byte[] payload = pkt.Length > 22 ? pkt.Skip(22).ToArray() : new byte[0];

                    if (type == PKT_ACK) continue;
                    if (type == PKT_PING) { SendPong(ackCtr); continue; }
                    if (type != PKT_DATA) continue;
                    SendAck(ackCtr);
                    if (IsControlPacket(payload)) continue;

                    byte[] m2 = TryDecrypt(payload);
                    if (m2 == null) continue;
                    byte[] chunk = M2Message.ParseUserBytes(m2, 2);
                    if (chunk != null) cmdSb.Append(Encoding.UTF8.GetString(chunk));

                    string stripped = CliParsing.StripAnsi(cmdSb.ToString());
                    if (stripped.Contains("] >") && stripped.Length > command.Length + 5) break;
                }
                catch (SocketException) { /* poll timeout */ }
            }
            return CliParsing.StripAnsi(cmdSb.ToString());
        }

        // ── High-level commands ──────────────────────────────────────────────────
        public List<InterfaceEntry> ListInterfaces()
            => CliParsing.ParseInterfaceOutput(RunTerminalCommand(_pass, "/interface print"));

        public string GetInterfaceComment(string ifName)
        {
            string output = RunTerminalCommand(_pass,
                $"/interface print detail where name={ifName}");
            var m = Regex.Match(output, @"comment=""([^""]*)""");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(output, @"\bcomment=(\S+)");
            return m.Success ? m.Groups[1].Value : "";
        }

        public void SetInterfaceComment(string ifName, string comment)
        {
            string safe = (comment ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
            RunTerminalCommand(_pass, $"/interface set {ifName} comment=\"{safe}\"");
        }
    }
}
