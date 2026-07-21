using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    /// <summary>
    /// Minimal scripted WinBox M2 peer for loopback protocol tests — the WinBox counterpart of
    /// <c>FakeRouterServer</c> (P1.7). It mirrors <see cref="WinboxTcpTransport"/>'s chunked framing from the
    /// server side and scripts the <b>legacy MD5</b> auth fallback, so tests can drive the real
    /// <see cref="WinboxM2Session.Open"/> with no production test seam and no EC-SRP5 crypto.
    /// </summary>
    /// <remarks>
    /// <para><b>How the auth shortcut works.</b> <c>WinboxM2Session.Authenticate</c> tries EC-SRP5 first and
    /// falls back to legacy MD5 when the exception message contains "EC-SRP5". A server that answers the
    /// EC-SRP5 hello with any frame tag other than <c>0x06</c> triggers exactly that
    /// ("EC-SRP5 not supported by server (tag=…)"), so <see cref="RejectEcSrp5"/> gets us onto the scriptable
    /// legacy path instantly — without waiting out the client's 3 s probe window.</para>
    /// <para><b>Coverage note.</b> The legacy path leaves the session <i>unencrypted</i>, so these tests
    /// exercise the raw framing (<c>tag 0x01</c>) rather than the AES path production uses on modern
    /// RouterOS. The chunk assembly (<c>RecvChunked</c>) is shared by both, and per
    /// <c>findings-winbox.md §12.3</c> the crypto is per-frame stateless, so message routing behaves
    /// identically. An encrypted mode (injected keys, no handshake) can be added when a test needs it.</para>
    /// </remarks>
    internal sealed class FakeWinboxServer : IDisposable
    {
        private const byte RawTag = 0x01;

        private readonly TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;

        /// <summary>Loopback port the fake is listening on (assigned by the OS).</summary>
        public int Port { get; }

        /// <summary>Every M2 message the client has sent, in order — for assertions.</summary>
        public List<byte[]> ReceivedMessages { get; } = new List<byte[]>();

        public FakeWinboxServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// Accepts one client connection, replacing any previous one. Called <b>twice</b> in a normal test:
        /// the legacy-MD5 fallback disposes the transport and reconnects after the EC-SRP5 attempt fails.
        /// </summary>
        public void AcceptClient(int timeoutMs = 5000)
        {
            var acceptTask = _listener.AcceptTcpClientAsync();
            if (!acceptTask.Wait(timeoutMs))
                throw new TimeoutException("Fake WinBox server did not receive a connection in time.");

            _stream?.Dispose();
            _client?.Dispose();
            _client = acceptTask.Result;
            _stream = _client.GetStream();
        }

        // ── EC-SRP5 rejection (drives the legacy fallback) ────────────────────

        /// <summary>
        /// Reads the client's EC-SRP5 hello (<c>[len][0x06][user\0 + xWA + parity]</c>) and answers with a
        /// 2-byte header carrying a non-0x06 tag, which makes the client abandon EC-SRP5 and reconnect for
        /// legacy MD5 auth.
        /// </summary>
        public void RejectEcSrp5()
        {
            byte[] hdr = ReadExact(2);
            ReadExact(hdr[0]);                       // discard the hello payload
            _stream.Write(new byte[] { 0x00, RawTag }, 0, 2);
            _stream.Flush();
        }

        // ── Raw (unencrypted) M2 frame I/O ────────────────────────────────────

        /// <summary>
        /// Reads one raw M2 frame sent by the client and returns the M2 message inside it.
        /// Mirrors <c>WinboxTcpTransport.BuildRawFrame</c>: <c>[b0][0x01][innerLenHi][innerLenLo][m2]</c>,
        /// where <c>b0 = n+2</c> for short messages and <c>0xFF</c> for long ones.
        /// </summary>
        public byte[] ReadRawFrame()
        {
            byte[] hdr = ReadExact(2);
            if (hdr[1] != RawTag)
                throw new InvalidOperationException($"Expected raw frame tag 0x01, got 0x{hdr[1]:x2}.");

            byte[] innerLen = ReadExact(2);
            int n = hdr[0] == 0xFF
                ? (innerLen[0] << 8) | innerLen[1]   // long form: real length in the inner header
                : hdr[0] - 2;                        // short form: chunk length minus the inner header

            byte[] m2 = ReadExact(n);
            ReceivedMessages.Add(m2);
            return m2;
        }

        /// <summary>
        /// Sends one M2 message as a raw frame the client's <c>RecvChunked(0x01)</c> can reassemble:
        /// a 2-byte inner length followed by the message, split into 255-byte chunks (first tagged
        /// <c>0x01</c>, continuations <c>0xFF</c>). Chunking matters — real responses exceed 255 bytes
        /// (the interface <c>getall</c> in findings-winbox.md §12.1 is 612 B).
        /// </summary>
        public void SendRawFrame(byte[] m2)
        {
            byte[] body = new byte[] { (byte)(m2.Length >> 8), (byte)(m2.Length & 0xFF) }
                .Concat(m2).ToArray();

            // Mirror of WinboxTcpTransport.SendChunked: a full 0xFF chunk always implies a continuation,
            // so a body that is an exact multiple of 255 ends with an explicit zero-length final chunk.
            byte tag = RawTag;
            int pos = 0;
            while (true)
            {
                int rem = body.Length - pos;
                if (rem >= 0xFF)
                {
                    WriteChunk(0xFF, tag, body, pos, 0xFF);
                    pos += 0xFF;
                }
                else
                {
                    WriteChunk((byte)rem, tag, body, pos, rem);
                    break;
                }
                tag = 0xFF;
            }
            _stream.Flush();
        }

        private void WriteChunk(byte chunkLen, byte tag, byte[] src, int offset, int count)
        {
            byte[] chunk = new byte[2 + count];
            chunk[0] = chunkLen;
            chunk[1] = tag;
            Buffer.BlockCopy(src, offset, chunk, 2, count);
            _stream.Write(chunk, 0, chunk.Length);
        }

        // ── Scripted legacy-MD5 authentication ────────────────────────────────

        /// <summary>
        /// Plays the server side of <c>WinboxM2Session.LegacyMd5Auth</c>: mproxy <c>list</c> → session id,
        /// challenge <c>setup</c> (no reply), <c>get-salt</c> → salt, <c>login</c> → status OK.
        /// </summary>
        /// <param name="sessionId">
        /// Session id handed to the client. Defaults to <b>265</b> deliberately: it exceeds a byte and so
        /// exercises the u8-vs-u32 encoding fixed in findings-winbox.md §1, where a u8-only implementation
        /// truncated 265 to 9 and addressed a dead session.
        /// </param>
        /// <param name="salt">16-byte challenge salt; a fixed non-zero pattern when omitted.</param>
        public void RunLegacyAuthHandshake(int sessionId = 265, byte[] salt = null)
        {
            salt = salt ?? Enumerable.Range(0, 16).Select(i => (byte)(i + 1)).ToArray();

            ReadRawFrame();                                            // mproxy "list"
            SendRawFrame(M2Message.BuildM2(
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.SessionIdField(sessionId)));

            ReadRawFrame();                                            // challenge "setup" — fire-and-forget,
                                                                       // the client drains instead of reading

            ReadRawFrame();                                            // "get-salt"
            SendRawFrame(M2Message.BuildM2(
                M2Message.SysToArr(0, 8), M2Message.SysFrom(),
                M2Message.RawUser(WinboxM2Protocol.LegacyAuth.Key.Salt, salt)));

            ReadRawFrame();                                            // "login"
            SendRawFrame(M2Message.BuildM2(                            // no ErrorCode field ⇒ status 0 ⇒ OK
                M2Message.SysToArr(0, 8), M2Message.SysFrom()));
        }

        /// <summary>
        /// Convenience: accept, reject EC-SRP5, accept the reconnect, and run the legacy handshake — i.e.
        /// everything <see cref="WinboxM2Session.Open"/> needs before a test can exchange real messages.
        /// Run this on a background task; <c>Open</c> blocks the calling thread until it completes.
        /// </summary>
        public void RunFullLoginSequence(int sessionId = 265)
        {
            AcceptClient();
            RejectEcSrp5();
            AcceptClient();               // legacy fallback disposes the transport and reconnects
            RunLegacyAuthHandshake(sessionId);
        }

        // ── Low-level ─────────────────────────────────────────────────────────

        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int total = 0;
            while (total < count)
            {
                int n = _stream.Read(buf, total, count - total);
                if (n <= 0)
                    throw new IOException("Client closed the connection while the fake server was reading.");
                total += n;
            }
            return buf;
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { /* teardown must not throw */ }
            try { _client?.Dispose(); } catch { /* teardown must not throw */ }
            try { _listener.Stop(); } catch { /* teardown must not throw */ }
        }
    }
}
