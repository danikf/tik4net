// WinboxTcpTransport.cs — TCP 8291 chunked-frame transport for Winbox M2
// Extracted from WinboxM2CatalogTest.cs (was inline in WinboxM2Client).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace tik4net.tests
{
    /// <summary>
    /// TCP socket + Winbox chunked-frame send/receive for port 8291.
    /// Chunk format: [len 1B][tag 1B][data len-bytes]
    ///   len=0xFF → continuation chunk (full 255 bytes)
    ///   len&lt;0xFF → final chunk (that many bytes)
    /// </summary>
    internal sealed class WinboxTcpTransport : IDisposable
    {
        private TcpClient     _tcp;
        private NetworkStream _ns;

        public NetworkStream Stream => _ns;
        public TcpClient     Client  => _tcp;

        public void Connect(string host, int port, int timeoutMs = 10000)
        {
            _tcp = new TcpClient();
            _tcp.Connect(host, port);
            _tcp.ReceiveTimeout = timeoutMs;
            _tcp.SendTimeout    = timeoutMs;
            _ns = _tcp.GetStream();
        }

        public bool DataAvailable => _ns?.DataAvailable ?? false;

        // Encrypted path (tag 0x06 first chunk, 0xFF continuation)
        public void SendChunked(byte[] data, byte firstTag)
        {
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
            var assembled = new System.Collections.Generic.List<byte>();
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
            return assembled.ToArray();
        }

        // Unencrypted raw send (tag 0x01)
        public void SendRaw(byte[] m2)
        {
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
                // Big-endian
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
