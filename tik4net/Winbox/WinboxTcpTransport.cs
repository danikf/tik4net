using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// The TCP socket of a WinBox session on port 8291, carrying the WinBox chunk framing
    /// (<see cref="WinboxChunkFraming"/>) both ways.
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
            // No close on failure here: the session's Dispose does that.
            TikTcp.Connect(_tcp, host, port, connectTimeoutMs);

            _tcp.ReceiveTimeout = ioTimeoutMs;
            // Falls back to ioTimeoutMs when the caller has no separate send bound, which is what this
            // always did - but a caller who sets ITikConnection.SendTimeout now gets that value applied
            // here instead of silently getting the receive one.
            _tcp.SendTimeout    = sendTimeoutMs > 0 ? sendTimeoutMs : ioTimeoutMs;

            // Nagle off, matching TelnetClient. Every M2 message is one small write and the next one is
            // not issued until this one is answered, so coalescing can only ever add latency waiting for
            // an acknowledgement. Hygiene rather than a fix: an A/B (six runs, 950 round trips)
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

            byte[] frame = WinboxChunkFraming.Chunk(data, firstTag);
            _ns.Write(frame, 0, frame.Length);
        }

        /// <summary>
        /// Reads one chunked frame. A frame with first tag <see cref="WinboxChunkFraming.EncryptedTag"/> is an
        /// encrypted one and ends at its declared length (see <see cref="WinboxChunkFraming.ReadChunkedFrame"/>) — on
        /// this carrier the EC-SRP5 handshake is read through <see cref="ReadExact"/> instead, so every 0x06 frame that
        /// reaches here is encrypted.
        /// </summary>
        public byte[] RecvChunked(byte expectedFirstTag)
        {
            byte[] result = WinboxChunkFraming.ReadChunkedFrame(ReadExact, expectedFirstTag,
                encrypted: expectedFirstTag == WinboxChunkFraming.EncryptedTag);

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

            byte[] frameBytes = WinboxChunkFraming.BuildRawFrame(m2);
            _ns.Write(frameBytes, 0, frameBytes.Length);
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

                int n;
                try { n = _ns.Read(buf, total, count - total); }
                catch (IOException ex) when (BytesRead == 0 && total == 0 && ex.InnerException is SocketException se
                    && (se.SocketErrorCode == SocketError.ConnectionReset || se.SocketErrorCode == SocketError.ConnectionAborted))
                {
                    throw ClosedBeforeAnyByte(ex);
                }

                if (TikWireTrace.Enabled)
                    TikWireTrace.Emit("wbxtcp.sock", TikWireDir.Recv, buf, total, n > 0 ? n : 0,
                        "got=" + n + " " + (total + (n > 0 ? n : 0)) + "/" + count);

                if (n <= 0)
                    throw BytesRead == 0 && total == 0 ? ClosedBeforeAnyByte(null) : new IOException("Connection closed unexpectedly");

                // Counted here, not once the frame is assembled: a waiter asking "is anything arriving?"
                // has to be answered while the frame is still incomplete, or the answer is useless.
                System.Threading.Interlocked.Add(ref _bytesRead, n);
                total += n;
            }
            return buf;
        }

        // RouterOS refuses a WinBox connection over its WinBox/Dude session limit by accepting it and closing or
        // resetting it at once, logging "too many winbox/dude sessions, drop connect from <client>". A connection that
        // ends before the router has sent one byte says so, and TikOpenRetry retries it.
        private static IOException ClosedBeforeAnyByte(Exception? inner) => new WinboxSessionLimitException(inner);

        public void SetReceiveTimeout(int ms) => _tcp.ReceiveTimeout = ms;
        public int  GetReceiveTimeout()       => _tcp.ReceiveTimeout;

        /// <summary>
        /// Runs <paramref name="read"/> with the socket's receive timeout (SO_RCVTIMEO) set to
        /// <paramref name="timeoutMs"/>, and puts the previous timeout back afterwards, whatever <paramref name="read"/>
        /// does.
        /// </summary>
        internal T WithReceiveTimeout<T>(int timeoutMs, Func<T> read)
        {
            int old = _tcp.ReceiveTimeout;
            _tcp.ReceiveTimeout = timeoutMs;
            try { return read(); }
            finally { _tcp.ReceiveTimeout = old; }
        }

        public void Dispose()
        {
            _ns?.Dispose();
            _tcp?.Dispose();
        }
    }
}
