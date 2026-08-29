using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// TCP socket + WinBox chunked-frame send/receive for port 8291.
    /// Chunk format: <c>[len 1B][tag 1B][data len-bytes]</c> where
    /// <c>len=0xFF</c> marks a continuation chunk (full 255 bytes) and a shorter length
    /// marks the final chunk.
    /// </summary>
    internal sealed class WinboxTcpTransport : IDisposable
    {
        // Assigned in Connect(), not the constructor - the type is only usable after Connect() succeeds.
        private TcpClient     _tcp = null!;
        private NetworkStream _ns = null!;

        public NetworkStream Stream => _ns;
        public TcpClient     Client  => _tcp;

        /// <summary>
        /// Opens the TCP socket. <paramref name="connectTimeoutMs"/> bounds only the connect handshake;
        /// <paramref name="ioTimeoutMs"/> becomes the socket's receive/send timeout (individual reads
        /// override it temporarily via <see cref="SetReceiveTimeout"/>).
        /// </summary>
        public void Connect(string host, int port, int connectTimeoutMs = 10000, int ioTimeoutMs = 30000,
            int sendTimeoutMs = 0)
        {
            _tcp = new TcpClient();

            // ConnectAsync with manual timeout so we work on netstandard2.0 (no CancellationToken overload there).
            // NOTE: Task.Wait(timeout) throws AggregateException (not the original exception) when the
            // task completes faulted within the timeout window (e.g. an immediate "connection refused") —
            // unwrap it so callers see the same SocketException they would from a direct ConnectAsync await.
            var connectTask = _tcp.ConnectAsync(host, port);
            try
            {
                if (!connectTask.Wait(connectTimeoutMs))
                    throw new SocketException((int)SocketError.TimedOut);
            }
            catch (AggregateException aex)
            {
                throw aex.InnerException ?? aex;
            }

            _tcp.ReceiveTimeout = ioTimeoutMs;
            // Falls back to ioTimeoutMs when the caller has no separate send bound, which is what this
            // always did - but a caller who sets ITikConnection.SendTimeout now gets that value applied
            // here instead of silently getting the receive one.
            _tcp.SendTimeout    = sendTimeoutMs > 0 ? sendTimeoutMs : ioTimeoutMs;

            // Nagle off, matching TelnetClient. Every M2 message is one small write and the next one is
            // not issued until this one is answered, so coalescing can only ever add latency waiting for
            // an acknowledgement. Hygiene rather than a fix: the P2.46 A/B (six runs, 950 round trips)
            // found no significant difference, because the stall it was chasing is not ours at all —
            // see Docs/findings-router-throughput-ceiling.md.
            _tcp.NoDelay = true;

            _ns = _tcp.GetStream();
        }

        public bool DataAvailable => _ns?.DataAvailable ?? false;

        // Encrypted path (tag 0x06 first chunk, 0xFF continuation)
        public void SendChunked(byte[] data, byte firstTag)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", TikWireDir.Send, data, 0, data.Length,
                    "tag=0x" + firstTag.ToString("x2"));

            byte tag = firstTag;
            int pos = 0;
            while (true)
            {
                int rem = data.Length - pos;
                if (rem >= 0xFF)
                {
                    byte[] chunk = new byte[2 + 0xFF];
                    chunk[0] = 0xFF; chunk[1] = tag;
                    Buffer.BlockCopy(data, pos, chunk, 2, 0xFF);
                    _ns.Write(chunk, 0, chunk.Length);
                    pos += 0xFF;
                }
                else
                {
                    byte[] chunk = new byte[2 + rem];
                    chunk[0] = (byte)rem; chunk[1] = tag;
                    Buffer.BlockCopy(data, pos, chunk, 2, rem);
                    _ns.Write(chunk, 0, chunk.Length);
                    break;
                }
                tag = 0xFF;
            }
        }

        public byte[] RecvChunked(byte expectedFirstTag)
        {
            var assembled = new List<byte>();
            bool first = true;
            while (true)
            {
                byte[] hdr = ReadExact(2);
                int chunkLen = hdr[0];
                byte tag = hdr[1];
                if (first)
                {
                    if (tag != expectedFirstTag)
                        throw new InvalidOperationException(
                            $"Expected frame tag 0x{expectedFirstTag:x2}, got 0x{tag:x2}");
                    first = false;
                }
                int payloadLen = (chunkLen == 0xFF) ? 0xFF : chunkLen;
                assembled.AddRange(ReadExact(payloadLen));
                if (chunkLen < 0xFF) break;
            }
            byte[] result = assembled.ToArray();

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", TikWireDir.Recv, result, 0, result.Length,
                    "tag=0x" + expectedFirstTag.ToString("x2"));

            return result;
        }

        // Unencrypted raw send (tag 0x01)
        public void SendRaw(byte[] m2)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", TikWireDir.Send, m2, 0, m2.Length, "tag=0x01 raw");

            byte[] frameBytes = BuildRawFrame(m2);
            _ns.Write(frameBytes, 0, frameBytes.Length);
        }

        private static byte[] BuildRawFrame(byte[] m2)
        {
            int n = m2.Length;
            if (n < 0xFF)
            {
                byte[] f = new byte[4 + n];
                f[0] = (byte)(n + 2); f[1] = 0x01; f[2] = 0x00; f[3] = (byte)n;
                Buffer.BlockCopy(m2, 0, f, 4, n);
                return f;
            }
            else
            {
                byte[] lenBytes = BitConverter.GetBytes((ushort)n);
                // Big-endian length
                byte[] f = new byte[4 + n];
                f[0] = 0xFF; f[1] = 0x01; f[2] = lenBytes[1]; f[3] = lenBytes[0];
                Buffer.BlockCopy(m2, 0, f, 4, n);
                return f;
            }
        }

        public byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int total = 0;
            while (total < count)
            {
                int n = _ns.Read(buf, total, count - total);
                if (n <= 0) throw new IOException("Connection closed unexpectedly");
                total += n;
            }
            return buf;
        }

        public void SetReceiveTimeout(int ms) => _tcp.ReceiveTimeout = ms;
        public int  GetReceiveTimeout()       => _tcp.ReceiveTimeout;

        public void Dispose()
        {
            _ns?.Dispose();
            _tcp?.Dispose();
        }
    }
}
