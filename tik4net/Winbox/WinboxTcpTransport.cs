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
    /// marks the final chunk — except that an encrypted frame also ends at the length it declares, see
    /// <see cref="ReadChunkedFrame"/>.
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

        private long _bytesRead;

        /// <summary>
        /// Bytes taken off the socket since it opened, counted per <see cref="ReadExact"/> read rather than
        /// per assembled frame — see <see cref="IWinboxM2Channel.BytesReceived"/> for why the difference is
        /// the whole point.
        /// </summary>
        public long BytesRead => System.Threading.Interlocked.Read(ref _bytesRead);

        // Encrypted path (tag 0x06 first chunk, 0xFF continuation)
        public void SendChunked(byte[] data, byte firstTag)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", TikWireDir.Send, data, 0, data.Length,
                    "tag=0x" + firstTag.ToString("x2"));

            byte[] frame = Chunk(data, firstTag);
            _ns.Write(frame, 0, frame.Length);
        }

        /// <summary>
        /// Splits <paramref name="data"/> into WinBox chunks — <c>[len 1B][tag 1B][data]</c>, the first
        /// carrying <paramref name="firstTag"/> and every continuation <c>0xFF</c>.
        /// </summary>
        /// <remarks>
        /// The one place the chunk rule is written down: both the encrypted and the raw send path go through
        /// it, because a length of exactly <c>0xFF</c> means "255 bytes and more to come" to
        /// <see cref="RecvChunked"/> and to the router. A payload of 255 bytes or more written unchunked is
        /// unparseable by either — which is what the raw path did until this was factored out. A body whose
        /// length is an exact multiple of 255 therefore ends with an explicit zero-length final chunk.
        /// </remarks>
        internal static byte[] Chunk(byte[] data, byte firstTag)
        {
            int chunks = data.Length / 0xFF + 1;
            byte[] frame = new byte[data.Length + 2 * chunks];

            byte tag = firstTag;
            int pos = 0, outPos = 0;
            while (true)
            {
                int rem = data.Length - pos;
                int take = rem >= 0xFF ? 0xFF : rem;
                frame[outPos++] = (byte)take;
                frame[outPos++] = tag;
                Buffer.BlockCopy(data, pos, frame, outPos, take);
                outPos += take;
                pos += take;
                if (take < 0xFF) break;
                tag = 0xFF;
            }
            return frame;
        }

        /// <summary>
        /// Reads one chunked frame. A frame with first tag <see cref="EncryptedFrameTag"/> is an encrypted one
        /// and ends at its declared length (see <see cref="ReadChunkedFrame"/>) — on this carrier the EC-SRP5
        /// handshake is read through <see cref="ReadExact"/> instead, so every 0x06 frame that reaches here is
        /// encrypted.
        /// </summary>
        public byte[] RecvChunked(byte expectedFirstTag)
        {
            byte[] result = ReadChunkedFrame(ReadExact, expectedFirstTag,
                encrypted: expectedFirstTag == EncryptedFrameTag);

            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", TikWireDir.Recv, result, 0, result.Length,
                    "tag=0x" + expectedFirstTag.ToString("x2"));

            return result;
        }

        /// <summary>First tag of an encrypted frame (and of an EC-SRP5 handshake frame).</summary>
        internal const byte EncryptedFrameTag = 0x06;

        /// <summary>Tag of every chunk after a frame's first.</summary>
        internal const byte ContinuationTag = 0xFF;

        /// <summary>
        /// Bytes of an encrypted frame beyond the ciphertext length it declares: the 2-byte length itself and
        /// the 16-byte IV (<c>[enc_len 2B BE][IV 16B][ciphertext]</c>, see <c>WinboxStreamCrypto</c>).
        /// </summary>
        internal const int EncryptedFrameOverhead = 18;

        /// <summary>
        /// Reads one chunked frame through <paramref name="readExact"/> — the one reassembly rule both carriers
        /// share (the MAC layer applies it to its datagram buffer via <see cref="TryTakeChunkedFrame"/>).
        /// </summary>
        /// <remarks>
        /// A chunk shorter than <c>0xFF</c> ends a frame. So does reaching the length an encrypted frame declares
        /// in its first two bytes, and that second rule is not optional: RouterOS sends <b>no</b> zero-length
        /// final chunk after an encrypted frame whose size is an exact multiple of 255 (the one reachable size
        /// is 3570 bytes, 14 full chunks, and a terminal pull of ~3.4 KB lands on it). Waiting for a short chunk
        /// there swallows the next frame into this one, both fail to decrypt, and the session stalls or dies.
        /// The router does accept such a terminator from us, and <see cref="Chunk"/> still sends one; a
        /// zero-length continuation chunk where a frame should start is that terminator, and is skipped.
        /// <para>
        /// A handshake frame (tag 0x06 but not yet encrypted) declares no length, so
        /// <paramref name="encrypted"/> is the caller's to say. The raw (0x01) frame's inner length is not used
        /// either: that path serves only pre-6.43 routers and has never been observed at this boundary.
        /// </para>
        /// </remarks>
        internal static byte[] ReadChunkedFrame(Func<int, byte[]> readExact, byte expectedFirstTag, bool encrypted)
        {
            var assembled = new List<byte>();
            bool first = true;
            while (true)
            {
                byte[] hdr = readExact(2);
                int chunkLen = hdr[0];
                byte tag = hdr[1];
                if (first)
                {
                    if (chunkLen == 0 && tag == ContinuationTag) continue;   // the previous frame's terminator
                    if (tag != expectedFirstTag)
                        throw new InvalidOperationException(
                            $"Expected frame tag 0x{expectedFirstTag:x2}, got 0x{tag:x2}");
                    first = false;
                }
                assembled.AddRange(readExact(chunkLen));
                if (chunkLen < 0xFF || (encrypted && DeclaredLengthReached(assembled))) break;
            }
            return assembled.ToArray();
        }

        /// <summary>
        /// Takes one complete chunked frame off the front of <paramref name="buffer"/> by the same rule as
        /// <see cref="ReadChunkedFrame"/>, or returns <c>null</c> and leaves the buffer untouched when it does
        /// not hold a whole frame yet. The first tag is not checked: the MAC layer never has.
        /// </summary>
        internal static byte[]? TryTakeChunkedFrame(List<byte> buffer, bool encrypted)
        {
            int pos = 0;
            // A zero-length continuation chunk where a frame starts is the previous frame's terminator.
            while (buffer.Count - pos >= 2 && buffer[pos] == 0 && buffer[pos + 1] == ContinuationTag) pos += 2;
            int start = pos;

            var frame = new List<byte>();
            while (true)
            {
                if (buffer.Count - pos < 2) break;                  // need a chunk header
                int chunkLen = buffer[pos];
                if (buffer.Count - pos - 2 < chunkLen) break;       // incomplete chunk
                for (int i = 0; i < chunkLen; i++) frame.Add(buffer[pos + 2 + i]);
                pos += 2 + chunkLen;
                if (chunkLen < 0xFF || (encrypted && DeclaredLengthReached(frame)))
                {
                    buffer.RemoveRange(0, pos);
                    return frame.ToArray();
                }
            }
            if (start > 0) buffer.RemoveRange(0, start);            // the terminator is consumed either way
            return null;
        }

        private static bool DeclaredLengthReached(List<byte> assembled)
            => assembled.Count >= 2
               && assembled.Count == EncryptedFrameOverhead + ((assembled[0] << 8) | assembled[1]);

        // Unencrypted raw send (tag 0x01)
        public void SendRaw(byte[] m2)
        {
            if (TikWireTrace.Enabled)
                TikWireTrace.Emit("wbxtcp.frame", TikWireDir.Send, m2, 0, m2.Length, "tag=0x01 raw");

            byte[] frameBytes = BuildRawFrame(m2);
            _ns.Write(frameBytes, 0, frameBytes.Length);
        }

        /// <summary>
        /// Builds one unencrypted frame: a 2-byte big-endian message length followed by the message,
        /// carried by the same chunk layer as the encrypted path with <c>0x01</c> as the first tag.
        /// </summary>
        /// <remarks>
        /// The inner length is redundant with the chunk lengths and the receive side ignores it
        /// (<c>WinboxM2Session</c> skips the two bytes), but the router sends it and expects it.
        /// </remarks>
        internal static byte[] BuildRawFrame(byte[] m2)
        {
            int n = m2.Length;
            if (n > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(m2),
                    $"A raw WinBox frame carries a 2-byte length, so it cannot hold {n} bytes.");

            byte[] body = new byte[2 + n];
            body[0] = (byte)(n >> 8);
            body[1] = (byte)n;
            Buffer.BlockCopy(m2, 0, body, 2, n);
            return Chunk(body, 0x01);
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes, looping until the socket has delivered them all.
        /// </summary>
        /// <remarks>
        /// The <c>wbxtcp.sock</c> trace events are the layer <b>below</b> <c>wbxtcp.frame</c>, which is
        /// emitted only once a whole frame has been assembled and therefore cannot show a reader parked
        /// here waiting for the rest of one. A note stamps the moment we enter the blocking read and a
        /// <see cref="TikWireDir.Recv"/> event stamps what came back, so a gap in the series is
        /// unambiguous: a note with no following <c>Recv</c> is us waiting on a router that sent nothing,
        /// while a <c>Recv</c> short of <c>want</c> followed by a long wait is a frame arriving in pieces.
        /// Distinguishing those two is what a mid-read stall diagnosis turns on, and it is why this exists
        /// instead of a packet capture.
        /// </remarks>
        public byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int total = 0;
            while (total < count)
            {
                if (TikWireTrace.Enabled)
                    TikWireTrace.Emit("wbxtcp.sock", TikWireDir.Note,
                        "read want=" + (count - total) + " into " + total + "/" + count);

                int n = _ns.Read(buf, total, count - total);

                if (TikWireTrace.Enabled)
                    TikWireTrace.Emit("wbxtcp.sock", TikWireDir.Recv, buf, total, n > 0 ? n : 0,
                        "got=" + n + " " + (total + (n > 0 ? n : 0)) + "/" + count);

                if (n <= 0) throw new IOException("Connection closed unexpectedly");

                // Counted here, not once the frame is assembled: a waiter asking "is anything arriving?"
                // has to be answered while the frame is still incomplete, or the answer is useless.
                System.Threading.Interlocked.Add(ref _bytesRead, n);
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
