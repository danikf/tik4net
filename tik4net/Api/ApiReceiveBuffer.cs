using System;
using System.IO;

namespace tik4net.Api
{
    /// <summary>
    /// The read side of one API reader session: what one receive from the socket brought, handed out a byte or a
    /// word body at a time. Writes do not go through it; they stay on the connection's stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framing reads a word's length prefix one byte at a time, and on a raw <c>NetworkStream</c> every
    /// <c>ReadByte</c> is a receive call of its own: about 80 per row of a firewall table, half of a plain-API
    /// load's client CPU. Here a receive fills the buffer with whatever the socket holds, and the prefixes and
    /// bodies are taken from it.
    /// </para>
    /// <para>
    /// The <c>onFill</c> callback is told of every receive, with the byte count, when the bytes arrive — which is
    /// what the connection's liveness counters (last byte, bytes received) mean, so a reply still trickling in reads
    /// as a busy socket rather than a silent one.
    /// </para>
    /// <para>
    /// Owned by the reader thread alone; not thread-safe. Made per session, with the stream that session was
    /// started on, so a reader outliving its session never reads another session's bytes.
    /// </para>
    /// </remarks>
    internal sealed class ApiReceiveBuffer
    {
        /// <summary>The buffer's size: well under the large-object heap's threshold, and more than a receive
        /// usually brings.</summary>
        internal const int DefaultSize = 64 * 1024;

        private readonly Stream _stream;
        private readonly Action<int> _onFill;
        private readonly byte[] _buffer;
        private int _position;
        private int _end;

        /// <param name="stream">The session's stream (the socket's, or TLS over it).</param>
        /// <param name="onFill">Told the byte count of every receive that brought bytes.</param>
        /// <param name="size">The buffer's size; tests use a small one.</param>
        internal ApiReceiveBuffer(Stream stream, Action<int> onFill, int size = DefaultSize)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            _stream = stream;
            _onFill = onFill;
            _buffer = new byte[size];
        }

        /// <summary>The next byte, or -1 when the stream has ended. Receives only when the buffer is empty.</summary>
        internal int ReadByte()
        {
            if (_position == _end && !Fill())
                return -1;
            return _buffer[_position++];
        }

        /// <summary>
        /// Up to <paramref name="count"/> bytes into <paramref name="destination"/>: what the buffer holds, or else one
        /// receive's worth. Returns 0 only when the stream has ended.
        /// </summary>
        internal int Read(byte[] destination, int offset, int count)
        {
            if (count == 0)
                return 0;
            if (_position == _end)
            {
                // A body at least the buffer's size goes straight into the caller's array rather than through ours.
                if (count >= _buffer.Length)
                {
                    int direct = _stream.Read(destination, offset, count);
                    if (direct > 0)
                        _onFill(direct);
                    return direct;
                }
                if (!Fill())
                    return 0;
            }

            int take = Math.Min(count, _end - _position);
            Buffer.BlockCopy(_buffer, _position, destination, offset, take);
            _position += take;
            return take;
        }

        private bool Fill()
        {
            int n = _stream.Read(_buffer, 0, _buffer.Length);
            if (n <= 0)
                return false;
            _position = 0;
            _end = n;
            _onFill(n);
            return true;
        }
    }
}
