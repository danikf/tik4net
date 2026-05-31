// MacTelnetClient.cs — MAC-Telnet raw VT100 terminal client (client_type=0x0015)
// Uses MacLayerTransport for UDP framing + EC-SRP5 auth.
// Extracted from MacLayerTest.cs.

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
    /// MAC-Telnet client (UDP 20561, client_type=0x0015).
    /// After EC-SRP5 auth, DATA packets carry raw VT100 bytes (unencrypted).
    /// </summary>
    internal sealed class MacTelnetClient : MacLayerTransport
    {
        private const ushort CLIENT_TYPE = 0x0015;
        private readonly Vt100State _vt100 = new Vt100State(80, 25);

        public void Connect(string host, string user, string pass)
        {
            BaseConnect(host, CLIENT_TYPE);
            Authenticate(user, pass);
            WaitForPrompt(20000);
        }

        // ── Terminal I/O ─────────────────────────────────────────────────────────
        private void SendTerminalData(byte[] data) => Send(PKT_DATA, data);

        private void WaitForPrompt(int timeoutMs)
        {
            var sb = new StringBuilder();
            bool sentCtrlC = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                _udp.Client.ReceiveTimeout = 500;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (pkt.Length < 22) continue;
                    byte   type    = pkt[1];
                    uint   counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                                     ((uint)pkt[20] <<  8) |  pkt[21];
                    byte[] payload = pkt.Length > 22 ? pkt.Skip(22).ToArray() : new byte[0];

                    if (type == PKT_ACK) continue;
                    if (type == PKT_PING) { SendPong(counter); continue; }
                    if (type != PKT_DATA) continue;

                    SendAck(counter);
                    if (IsControlPacket(payload)) continue;

                    string text = Encoding.UTF8.GetString(payload);
                    sb.Append(text);
                    Console.Write($"[prompt] +{payload.Length}B");

                    foreach (string reply in _vt100.Process(text))
                    {
                        Console.Write($" →{reply.Length}B");
                        SendTerminalData(Encoding.UTF8.GetBytes(reply));
                    }
                    Console.WriteLine();

                    string stripped = CliParsing.StripAnsi(sb.ToString());

                    if (!sentCtrlC && stripped.Contains("password>"))
                    {
                        Console.WriteLine("[prompt] → sending Ctrl-C (skip password nag)");
                        SendTerminalData(new byte[] { 0x03 });
                        sentCtrlC = true;
                        sb.Clear();
                        continue;
                    }

                    if (stripped.Contains("] >")) { Console.WriteLine("[prompt] Ready."); return; }
                }
                catch (SocketException) { /* poll timeout */ }
            }
            throw new TimeoutException("Timed out waiting for MACTelnet CLI prompt (] >)");
        }

        public string ExecuteCommand(string command)
        {
            SendTerminalData(Encoding.UTF8.GetBytes(command + "\r"));

            var sb = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int minLen = command.Length + 5;

            while (sw.ElapsedMilliseconds < 10000)
            {
                _udp.Client.ReceiveTimeout = 500;
                try
                {
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] pkt = _udp.Receive(ref ep);
                    if (pkt.Length < 22) continue;
                    byte   type    = pkt[1];
                    uint   counter = ((uint)pkt[18] << 24) | ((uint)pkt[19] << 16) |
                                     ((uint)pkt[20] <<  8) |  pkt[21];
                    byte[] payload = pkt.Length > 22 ? pkt.Skip(22).ToArray() : new byte[0];

                    if (type == PKT_ACK) continue;
                    if (type == PKT_PING) { SendPong(counter); continue; }
                    if (type != PKT_DATA) continue;

                    SendAck(counter);
                    if (IsControlPacket(payload)) continue;

                    sb.Append(Encoding.UTF8.GetString(payload));
                    string stripped = CliParsing.StripAnsi(sb.ToString());
                    if (stripped.Contains("] >") && stripped.Length >= minLen) break;
                }
                catch (SocketException) { /* poll timeout */ }
            }
            return CliParsing.StripAnsi(sb.ToString());
        }

        // ── High-level commands ──────────────────────────────────────────────────
        public List<InterfaceEntry> ListInterfaces()
            => CliParsing.ParseInterfaceOutput(ExecuteCommand("/interface print"));

        public string GetInterfaceComment(string ifName)
        {
            string output = ExecuteCommand($"/interface print detail where name={ifName}");
            var m = Regex.Match(output, @"comment=""([^""]*)""");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(output, @"\bcomment=(\S+)");
            return m.Success ? m.Groups[1].Value : "";
        }

        public void SetInterfaceComment(string ifName, string comment)
        {
            string safe = (comment ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
            ExecuteCommand($"/interface set {ifName} comment=\"{safe}\"");
        }
    }
}
