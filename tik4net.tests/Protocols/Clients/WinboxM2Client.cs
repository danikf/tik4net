// WinboxM2Client.cs — Winbox/TCP M2 client
// Uses WinboxTcpTransport + _Shared helpers.
// Extracted from WinboxM2CatalogTest.cs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using tik4net.Crypto;
using ECPoint = tik4net.Crypto.ECPoint;

namespace tik4net.tests
{
    /// <summary>
    /// Winbox M2 client over TCP port 8291.
    /// Implements EC-SRP5 authentication, AES-128-CBC encryption,
    /// mproxy file read, mepty terminal session.
    /// </summary>
    internal sealed class WinboxM2Client : IDisposable
    {
        private readonly WinboxTcpTransport _transport = new WinboxTcpTransport();
        private byte[] _sendAesKey, _sendHmacKey, _receiveAesKey, _receiveHmacKey;
        private bool _encrypted;
        private int _authSessionId = -1;
        private int _reqId;
        private const int SRC_ID = 8;

        // ── Public API ───────────────────────────────────────────────────────
        public void Connect(string host, int port)
            => _transport.Connect(host, port);

        public void Authenticate(string host, int port, string user, string pass)
        {
            try
            {
                EcSrp5Auth(user, pass);
                _encrypted = true;
            }
            catch (Exception ex) when (ex.Message.Contains("EC-SRP5"))
            {
                // Old RouterOS — reconnect and try legacy MD5 auth
                _transport.Dispose();
                _reqId = 0;
                Connect(host, port);
                LegacyMd5Auth(user, pass);
                _encrypted = false;
            }
        }

        // After auth, read /home/web/webfig/list via mproxy [2,2]
        public string ReadListCatalog()
        {
            int fileHandle;
            if (!_encrypted && _authSessionId >= 0)
                fileHandle = _authSessionId;
            else
                fileHandle = MproxyOpenFile("list");
            return MproxyReadFile(fileHandle);
        }

        // Send an arbitrary M2 request and return all parsed TLV fields.
        public Dictionary<int, Tuple<string, object>> GetM2Fields(int[] handlerPath, int cmd)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(handlerPath), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true),
                ReqId(),
                M2Message.U8Sys(0xFF0007, (byte)cmd));
            byte[] resp = _encrypted ? SendRecvEncrypted(msg) : SendRecvRaw(msg);
            return M2Message.ParseAllFields(resp);
        }

        // Download any file from /home/web/webfig/ by name (mproxy cmd=7).
        public byte[] ReadFileBytes(string filename)
        {
            int handle = MproxyOpenFile(filename);
            return MproxyReadFileBytes(handle);
        }

        // Download a catalog file.
        // .jg plugins live in /var/pckg/ → opened via cmd=3 (authenticated), content is gzip-compressed.
        // Static files (list, *.png) live in /home/web/webfig/ → opened via cmd=7.
        public byte[] ReadFileBytes(CatalogEntry entry)
        {
            if (entry.Name.EndsWith(".jg", StringComparison.OrdinalIgnoreCase))
            {
                // Use plain entry.Name — unique hash is the Windows-client cache key only.
                int handle = MproxyOpenVarPkgFile(entry.Name);
                byte[] compressed = MproxyReadFileBytes(handle);
                return compressed != null && compressed.Length > 0
                    ? GzipDecompress(compressed)
                    : compressed;
            }
            string openName = string.IsNullOrEmpty(entry.Unique) ? entry.Name : entry.Unique;
            return ReadFileBytes(openName);
        }

        public void Dispose() => _transport.Dispose();

        // ── Structured catalog parsing ────────────────────────────────────────
        public static List<CatalogEntry> ParseCatalog(string text)
        {
            var entries = new List<CatalogEntry>();
            foreach (Match m in Regex.Matches(text ?? "", @"\{([^}]+)\}"))
            {
                var body = m.Groups[1].Value;
                entries.Add(new CatalogEntry
                {
                    Crc     = ExtractLong(body, "crc"),
                    Size    = ExtractLong(body, "size"),
                    Name    = ExtractStr(body, "name"),
                    Unique  = ExtractStr(body, "unique"),
                    Version = ExtractStr(body, "version")
                });
            }
            return entries;
        }

        // Returns parsed system info from handler [13,4] cmd=7.
        public SystemInfo GetSystemInfo()
        {
            var f = GetM2Fields(new[] { 13, 4 }, 7);
            return new SystemInfo
            {
                Version      = GetStringField(f, 0x000016),
                Board        = GetStringField(f, 0x000015),
                Architecture = GetStringField(f, 0x000017),
                Identity     = GetStringField(f, 0x000018)
            };
        }

        public void SetInterfaceComment(string password, string ifName, string comment)
        {
            string safe = comment ?? "";
            string valueExpr = (safe.Length == 0)
                ? "\"\""
                : (safe.IndexOfAny(new[] { ' ', '"', '\\' }) >= 0)
                    ? "\"" + safe.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                    : safe;
            string cmd = $"/interface set {ifName} comment={valueExpr}";
            Console.WriteLine($"SET CMD: {cmd}");
            string setOut = RunTerminalCommand(password, cmd);
            Console.WriteLine($"SET OUTPUT: '{setOut.Trim()}'");
        }

        public string GetInterfaceComment(string password, string ifName)
        {
            string output = RunTerminalCommand(password,
                $"/interface print detail where name={ifName}");
            Console.WriteLine($"GET OUTPUT ({ifName}): '{output.Trim()}'");
            var m = Regex.Match(output, @";;;\s+(.+?)(?:\r|\n|$)", RegexOptions.Multiline);
            if (m.Success) return m.Groups[1].Value.Trim();
            return "";
        }

        public List<InterfaceEntry> ListInterfaces(string password)
        {
            string output = RunTerminalCommand(password, "/interface print");
            Console.WriteLine("--- raw terminal output (stripped) ---");
            Console.WriteLine(output);
            Console.WriteLine("--------------------------------------");
            return CliParsing.ParseInterfaceOutput(output);
        }

        // ── EC-SRP5 Authentication ────────────────────────────────────────────
        private void EcSrp5Auth(string user, string pass)
        {
            byte[] privA = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(privA);

            var (xWA, parityA) = EcSrp5.GenPublicKey(privA);
            byte[] userBytes = Encoding.UTF8.GetBytes(user);
            byte[] payload = userBytes.Concat(new byte[] { 0 }).Concat(xWA)
                .Concat(new byte[] { (byte)parityA }).ToArray();
            SendHandshake(payload);

            _transport.SetReceiveTimeout(3000);
            byte respLen, respTag;
            try
            {
                byte[] hdr = _transport.ReadExact(2);
                respLen = hdr[0];
                respTag = hdr[1];
            }
            catch (IOException)
            {
                throw new InvalidOperationException("EC-SRP5 not supported by server");
            }
            _transport.SetReceiveTimeout(10000);

            if (respTag != 0x06)
                throw new InvalidOperationException(
                    $"EC-SRP5 not supported by server (tag=0x{respTag:x2})");
            if (respLen != 49)
                throw new InvalidOperationException(
                    $"Unexpected challenge size {respLen}, expected 49");

            byte[] challenge = _transport.ReadExact(respLen);
            byte[] xWB   = challenge.Take(32).ToArray();
            int parityB  = challenge[32];
            byte[] salt  = challenge.Skip(33).Take(16).ToArray();

            byte[] valPriv  = EcSrp5.GenPasswordValidatorPriv(user, pass, salt);
            var (xGamma, _) = EcSrp5.GenPublicKey(valPriv);
            ECPoint v   = EcSrp5.Redp1(xGamma, 1);
            ECPoint wB  = EcSrp5.LiftX(EcSrp5.BEToBI(xWB), parityB);
            ECPoint sum = EcSrp5.ECAdd(wB, v);

            byte[] j   = EcSrp5.Sha256(xWA.Concat(xWB).ToArray());
            var iInt   = EcSrp5.BEToBI(valPriv);
            var jInt   = EcSrp5.BEToBI(j);
            var aInt   = EcSrp5.BEToBI(privA);
            var vh     = (iInt * jInt % EcSrp5.R + aInt) % EcSrp5.R;

            ECPoint zPt = EcSrp5.ECScalarMul(vh, sum);
            var (zMont, _) = EcSrp5.ToMontgomery(zPt);
            byte[] secret = EcSrp5.Sha256(zMont);

            byte[] clientCc = EcSrp5.Sha256(j.Concat(zMont).ToArray());
            SendHandshake(clientCc);

            byte[] srvHdr   = _transport.ReadExact(2);
            byte[] serverCc = _transport.ReadExact(srvHdr[0]);
            byte[] expectedCc = EcSrp5.Sha256(j.Concat(clientCc).Concat(zMont).ToArray());
            if (!serverCc.SequenceEqual(expectedCc))
                throw new UnauthorizedAccessException("Wrong username or password");

            WinboxStreamCrypto.DeriveStreamKeys(false, secret,
                out _sendAesKey, out _receiveAesKey,
                out _sendHmacKey, out _receiveHmacKey);
        }

        private void SendHandshake(byte[] payload)
        {
            byte[] frame = new byte[] { (byte)payload.Length, 0x06 }.Concat(payload).ToArray();
            _transport.Stream.Write(frame, 0, frame.Length);
        }

        // ── Legacy MD5 Authentication ─────────────────────────────────────────
        private void LegacyMd5Auth(string user, string pass)
        {
            byte[] listMsg = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 7),
                M2Message.BoolSys(0xFF0005, true), ReqId(), M2Message.StringUser(1, "list"));
            byte[] listResp = SendRecvRaw(listMsg);
            int sessionId = M2Message.ParseSessionId(listResp);
            _authSessionId = sessionId;

            byte[] challengeSetup = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 5),
                M2Message.BoolSys(0xFF0005, true), ReqId(), M2Message.SessionIdField(sessionId));
            SendRaw(challengeSetup);
            System.Threading.Thread.Sleep(200);
            DrainSocket(500);

            byte[] saltMsg = M2Message.BuildM2(
                M2Message.SysToArr(13, 4), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 4),
                M2Message.BoolSys(0xFF0005, true), ReqId(), M2Message.SessionIdField(sessionId));
            byte[] saltResp = SendRecvRaw(saltMsg);
            byte[] salt = M2Message.ParseRawUser(saltResp, 9);
            if (salt == null)
                throw new InvalidOperationException("No salt in challenge response");

            byte[] hashInput = new byte[] { 0 }
                .Concat(Encoding.UTF8.GetBytes(pass)).Concat(salt).ToArray();
            byte[] hash = new byte[] { 0 }
                .Concat(MD5.Create().ComputeHash(hashInput)).ToArray();

            byte[] loginMsg = M2Message.BuildM2(
                M2Message.SysToArr(13, 4), M2Message.SysFrom(), M2Message.U32Sys(0xFF0007, 1),
                M2Message.BoolSys(0xFF0005, true), ReqId(), M2Message.SessionIdField(sessionId),
                M2Message.StringUser(1, user), M2Message.RawUser(9, salt), M2Message.RawUser(10, hash));
            byte[] loginResp = SendRecvRaw(loginMsg);

            int status = M2Message.ParseSysStatus(loginResp);
            if (status != 0)
                throw new UnauthorizedAccessException("Wrong username or password (legacy auth)");
        }

        // ── Mproxy file operations ────────────────────────────────────────────
        // Open a file from /home/web/webfig/ (no-auth path, static assets only).
        private int MproxyOpenFile(string filename)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true),
                ReqId(),
                M2Message.U8Sys(0xFF0007, 7),
                M2Message.StringUser(1, filename));
            byte[] resp = SendRecvEncrypted(msg);
            return M2Message.ParseSessionId(resp);
        }

        // Open a file from /var/pckg/ (authenticated read, cmd=3). Used for .jg plugins.
        // NOTE: On CHR (RouterOS 7.x) this returns "cannot open source file" (0xFE0006)
        // because CHR does not expose package files via mproxy. Hardware routers may work.
        private int MproxyOpenVarPkgFile(string filename)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(2, 2), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true),
                ReqId(),
                M2Message.U8Sys(0xFF0007, 3),
                M2Message.StringUser(1, filename));
            byte[] resp = SendRecvEncrypted(msg);
            return M2Message.ParseSessionId(resp);
        }

        private static byte[] GzipDecompress(byte[] compressed)
        {
            using (var ms = new MemoryStream(compressed))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
        }

        private string MproxyReadFile(int handle)
        {
            byte[] raw = MproxyReadFileBytes(handle);
            if (raw == null) return "";
            return Encoding.UTF8.GetString(raw);
        }

        private const int MPROXY_CHUNK = 32768;

        private byte[] MproxyReadFileBytes(int handle)
        {
            // mproxy cmd=4 returns at most MPROXY_CHUNK bytes per read and advances the
            // file pointer server-side. Keep reading until a short/empty chunk = EOF.
            // (A single AES frame maxes at ~65535B, so large files MUST be multi-read.)
            var all = new List<byte>();
            for (int guard = 0; guard < 64; guard++)
            {
                byte[] msg = M2Message.BuildM2(
                    M2Message.SysToArr(2, 2), M2Message.SysFrom(),
                    M2Message.BoolSys(0xFF0005, true),
                    M2Message.U8Sys(0xFF0006, (byte)NextReqId()),
                    M2Message.SessionIdField(handle),
                    M2Message.U32User(2, MPROXY_CHUNK),
                    M2Message.U8Sys(0xFF0007, 4));
                byte[] resp = _encrypted ? SendRecvEncrypted(msg) : SendRecvRaw(msg);
                byte[] chunk = ExtractMproxyChunk(resp);
                if (chunk == null || chunk.Length == 0) break;
                all.AddRange(chunk);
                if (chunk.Length < MPROXY_CHUNK) break;
            }
            return all.Count > 0 ? all.ToArray() : null;
        }

        // Pull the raw/string file-content field (namespace 0x00) out of a read response.
        private static byte[] ExtractMproxyChunk(byte[] resp)
        {
            if (resp == null || resp.Length < 2) return null;
            if (resp[0] != 'M' || resp[1] != '2') return resp;
            int pos = 2;
            while (pos + 4 <= resp.Length)
            {
                int ns = resp[pos+2], type = resp[pos+3];
                pos += 4;
                if (ns == 0x00 && (type == 0x31 || type == 0x30 || type == 0x21 || type == 0x20))
                {
                    int len = (type == 0x31 || type == 0x21)
                        ? resp[pos++]
                        : (int)BitConverter.ToUInt16(resp, (pos += 2) - 2);
                    if (len > 0 && pos + len <= resp.Length)
                        return resp.Skip(pos).Take(len).ToArray();
                }
                pos += M2Message.SkipTypeBytes(type, resp, pos);
            }
            return null;
        }

        // ── Terminal session (mepty handler [76]) ────────────────────────────
        private int OpenTerminalSession(string password)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(76), M2Message.SysFrom(),
                M2Message.BoolSys(0xFF0005, true),
                ReqId(),
                M2Message.U32Sys(0xFF0007, 0x0A0065),
                M2Message.StringUser(1, password),
                M2Message.StringUser(2, "vt102"),
                M2Message.U32User(3, 80),
                M2Message.U32User(4, 25));
            byte[] resp = SendRecvEncrypted(msg, 5000);
            return M2Message.ParseSessionId(resp);
        }

        private void SendTerminalReady(int sessionId)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(76), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId),
                M2Message.U32Sys(0xFF0007, 0x0A0067),
                M2Message.U32User(3, 0));
            EncryptAndSend(msg);
        }

        private void SendTerminalInput(int sessionId, byte[] keystrokes, ref int counter)
        {
            byte[] msg = M2Message.BuildM2(
                M2Message.SysToArr(76), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId),
                M2Message.U32Sys(0xFF0007, 0x0A0067),
                M2Message.RawUser(2, keystrokes),
                M2Message.U32User(3, counter++));
            EncryptAndSend(msg);
        }

        public string RunTerminalCommand(string password, string command)
        {
            DrainEncryptedFrames(600);
            int sessionId = OpenTerminalSession(password);
            Console.WriteLine($"[mepty] Session {sessionId} opened for: {command}");
            SendTerminalReady(sessionId);

            int counter = 1;
            var initSb = new StringBuilder();
            var term   = new Vt100State(80, 25);
            var sw     = Stopwatch.StartNew();

            bool sentCtrlC = false;
            while (sw.ElapsedMilliseconds < 15000)
            {
                if (!_transport.DataAvailable) { System.Threading.Thread.Sleep(50); continue; }
                byte[] resp;
                try { resp = RecvAndDecrypt(5000); }
                catch (IOException) { break; }
                if (resp == null) continue;
                byte[] chunk = M2Message.ParseUserBytes(resp, 2);
                if (chunk == null) continue;
                string text = Encoding.UTF8.GetString(chunk);
                Console.WriteLine($"  [vt#] {BitConverter.ToString(chunk)}");
                initSb.Append(text);
                foreach (string reply in term.Process(text))
                {
                    Console.WriteLine($"  → {BitConverter.ToString(Encoding.UTF8.GetBytes(reply))}");
                    SendTerminalInput(sessionId, Encoding.UTF8.GetBytes(reply), ref counter);
                }
                string stripped = CliParsing.StripAnsi(initSb.ToString());
                if (stripped.Contains("] >")) break;
                if (!sentCtrlC && (stripped.Contains("new password>") || stripped.Contains("password>")))
                {
                    Console.WriteLine("  → sending Ctrl-C to skip password change nag");
                    SendTerminalInput(sessionId, new byte[] { 0x03 }, ref counter);
                    sentCtrlC = true;
                    initSb.Clear();
                }
            }
            string preOut = CliParsing.StripAnsi(initSb.ToString());
            Console.WriteLine("PROMPT: " + preOut.Contains("] >") + " | " + preOut.Trim());
            if (!preOut.Contains("] >")) return "";

            var cmdSb = new StringBuilder();
            SendTerminalInput(sessionId, Encoding.UTF8.GetBytes(command + "\r"), ref counter);
            sw.Restart();
            while (sw.ElapsedMilliseconds < 8000)
            {
                if (!_transport.DataAvailable) { System.Threading.Thread.Sleep(50); continue; }
                byte[] resp;
                try { resp = RecvAndDecrypt(5000); }
                catch (IOException) { break; }
                if (resp == null) continue;
                byte[] chunk = M2Message.ParseUserBytes(resp, 2);
                if (chunk != null)
                {
                    cmdSb.Append(Encoding.UTF8.GetString(chunk));
                    string stripped = CliParsing.StripAnsi(cmdSb.ToString()).TrimEnd();
                    if (stripped.EndsWith("] >")) break;
                }
            }
            string result = CliParsing.StripAnsi(cmdSb.ToString());
            Console.WriteLine($"CMD OUTPUT (len={result.Length}): " + result);
            return result;
        }

        // ── Encrypted frame I/O ───────────────────────────────────────────────
        private byte[] SendRecvEncrypted(byte[] m2, int timeoutMs = 5000)
        {
            EncryptAndSend(m2);
            return RecvAndDecrypt(timeoutMs);
        }

        private void EncryptAndSend(byte[] msg)
        {
            byte[] full = WinboxStreamCrypto.Encrypt(msg, _sendAesKey, _sendHmacKey);
            _transport.SendChunked(full, 0x06);
        }

        private byte[] RecvAndDecrypt(int timeoutMs)
        {
            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] assembled = _transport.RecvChunked(0x06);
                return WinboxStreamCrypto.Decrypt(assembled, _receiveAesKey);
            }
            finally { _transport.SetReceiveTimeout(old); }
        }

        private void SendRaw(byte[] m2) => _transport.SendRaw(m2);

        private byte[] SendRecvRaw(byte[] m2, int timeoutMs = 5000)
        {
            SendRaw(m2);
            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] assembled = _transport.RecvChunked(0x01);
                return assembled.Length >= 2 ? assembled.Skip(2).ToArray() : assembled;
            }
            finally { _transport.SetReceiveTimeout(old); }
        }

        private void DrainEncryptedFrames(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!_transport.DataAvailable) { System.Threading.Thread.Sleep(20); continue; }
                try { RecvAndDecrypt(500); }
                catch { break; }
            }
        }

        private void DrainSocket(int timeoutMs)
        {
            int old = _transport.GetReceiveTimeout();
            _transport.SetReceiveTimeout(timeoutMs);
            try
            {
                byte[] buf = new byte[4096];
                while (_transport.DataAvailable) _transport.Stream.Read(buf, 0, buf.Length);
            }
            catch (IOException) { }
            finally { _transport.SetReceiveTimeout(old); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private byte[] ReqId() => M2Message.U8Sys(0xFF0006, (byte)NextReqId());
        private int NextReqId() => ++_reqId;

        private static string GetStringField(
            Dictionary<int, Tuple<string, object>> fields, int key)
            => fields.TryGetValue(key, out var t) ? t.Item2?.ToString() ?? "" : "";

        private static long ExtractLong(string body, string key)
        {
            var m = Regex.Match(body, $@"\b{key}\s*:\s*(\d+)");
            return m.Success ? long.Parse(m.Groups[1].Value) : 0;
        }

        private static string ExtractStr(string body, string key)
        {
            var m = Regex.Match(body, $@"\b{key}\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
