using System;
using System.Text;
using System.Threading;

namespace tik4net.Diagnostics
{
    /// <summary>
    /// Direction of a wire-trace event relative to the client.
    /// </summary>
    public enum TikWireDir
    {
        /// <summary>Bytes the client sent to the router.</summary>
        Send,
        /// <summary>Bytes the client received from the router.</summary>
        Recv,
        /// <summary>A note with no payload (a decision point, e.g. a prompt/settle/timeout marker).</summary>
        Note,
    }

    /// <summary>
    /// Receives byte/frame-level wire-trace events emitted by the transports. Install one with
    /// <see cref="TikWireTrace.Capture"/> to diagnose a transport desync at a layer below the
    /// word/sentence <c>OnReadRow</c>/<c>OnWriteRow</c> events — which for the CLI family already
    /// show the ANSI-stripped, cleaned text and for WinBox-native the decoded M2 record, hiding the
    /// exact bytes, mepty frame batching, VT100 negotiation and prompt/settle timing that a
    /// terminal-level bug lives in.
    /// </summary>
    /// <remarks>
    /// <see cref="Emit"/> is called on whatever thread does the I/O — for WinBox-native that is the
    /// multiplexer's reader thread, not the caller's — so an implementation must be thread-safe if it
    /// mutates shared state. The diagnostic use is one command at a time, so a simple lock (or a
    /// concurrent collection) is enough. The passed buffer is <b>not</b> retained after the call
    /// returns; copy out (e.g. via <see cref="TikWireTrace.Escape(byte[],int,int)"/>) if you need to
    /// keep it.
    /// </remarks>
    public interface ITikWireTraceSink
    {
        /// <summary>
        /// A single wire event. <paramref name="channel"/> is a short emit-site id
        /// (<c>"wbxcli.mepty"</c>, <c>"wbxtcp.frame"</c>, <c>"telnet.sock"</c>, <c>"mactelnet.udp"</c>,
        /// <c>"ssh.pty"</c>, <c>"api.word"</c>). For <see cref="TikWireDir.Note"/> events <paramref name="data"/> is
        /// <c>null</c> and only <paramref name="note"/> carries meaning.
        /// </summary>
        void Emit(string channel, TikWireDir dir, byte[]? data, int offset, int count, string? note);
    }

    /// <summary>
    /// Process-wide ambient sink for byte/frame-level wire tracing. No-op (a single null check) unless a
    /// sink is installed via <see cref="Capture"/>, so the emit points can stay permanently in the
    /// transports at negligible cost.
    /// </summary>
    /// <remarks>
    /// The sink is a process-wide <c>volatile</c> field, deliberately not an <see cref="AsyncLocal{T}"/>:
    /// the WinBox-native multiplexer reads on its own thread, which an <c>AsyncLocal</c> flow would not
    /// reach. The diagnostic use is one command at a time (the MCP tool wraps a single call), so a single
    /// process sink is the correct and simplest model. Not intended for always-on production logging.
    /// </remarks>
    public static class TikWireTrace
    {
        private static volatile ITikWireTraceSink? _sink;   // null = disabled (the hot path)

        /// <summary>True while a sink is installed. Emit sites may check this to skip building a note string.</summary>
        public static bool Enabled => _sink != null;

        /// <summary>
        /// Installs <paramref name="sink"/> as the active wire-trace sink and returns an
        /// <see cref="IDisposable"/> that restores the previously-installed sink (usually none) on
        /// dispose. Wrap the call you want to trace:
        /// <code>using (TikWireTrace.Capture(mySink)) { /* run the command */ }</code>
        /// Nesting restores the outer sink; concurrent captures on different threads are not supported
        /// (there is one process-wide sink) and are not a design goal — trace one command at a time.
        /// </summary>
        public static IDisposable Capture(ITikWireTraceSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            var previous = _sink;
            _sink = sink;
            return new Restorer(previous);
        }

        private static readonly AsyncLocal<bool> _secret = new AsyncLocal<bool>();

        /// <summary>
        /// Suppresses the <b>payload</b> of every trace event emitted inside the returned scope, replacing it
        /// with a length-only note. Wrap the moment a credential goes onto the wire:
        /// <code>using (TikWireTrace.Secret()) await sendLine(password, ct);</code>
        /// </summary>
        /// <remarks>
        /// A wire trace is a debugging aid that people paste into bug reports, so it must not carry a
        /// password. The binary API sends the credential as a word this class can recognise by name (see
        /// <see cref="IsSecretWord"/>); a terminal transport just types it, and raw bytes on a socket say
        /// nothing about what they mean — only the code doing the typing knows, so it has to say so.
        /// <para>
        /// The scope is an <see cref="AsyncLocal{T}"/> so it survives the <c>await</c>s of a login handshake.
        /// It deliberately does <b>not</b> reach a transport's own background reader thread; nothing is lost
        /// by that, because RouterOS does not echo a password back. Notes are left alone — the point is to
        /// keep the shape of the handshake visible while removing only the secret.
        /// </para>
        /// </remarks>
        public static IDisposable Secret()
        {
            bool previous = _secret.Value;
            _secret.Value = true;
            return new SecretRestorer(previous);
        }

        /// <summary>
        /// True when <paramref name="word"/> is an API word whose value is a credential and must never be
        /// rendered — the plaintext <c>=password=</c> of the 6.43+ login, the <c>=response=</c> of the older
        /// challenge protocol (equivalent to the password for replay), and a password being changed.
        /// </summary>
        public static bool IsSecretWord(string? word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            foreach (string name in SecretWordNames)
            {
                if (word!.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static readonly string[] SecretWordNames =
        {
            "=password=", "=response=", "=new-password=", "=confirm-password=",
        };

        // Payload event. off/len bound the region of data to render; the buffer is not retained.
        internal static void Emit(string channel, TikWireDir dir, byte[]? data, int offset, int count, string? note = null)
        {
            var sink = _sink;
            if (sink == null) return;
            if (_secret.Value && count > 0)
            {
                sink.Emit(channel, dir, null, 0, 0, Redacted(count, note));
                return;
            }
            sink.Emit(channel, dir, data, offset, count, note);
        }

        /// <summary>
        /// Payload event for one API word, redacted by the word's own name rather than by a scope.
        /// </summary>
        /// <remarks>
        /// Preferred over a <see cref="Secret"/> scope around the whole login, which would also blank the
        /// challenge, the banner and the reply — the parts a login problem is actually diagnosed from.
        /// </remarks>
        internal static void EmitWord(string channel, TikWireDir dir, byte[] data, int offset, int count, string? word)
        {
            var sink = _sink;
            if (sink == null) return;
            if (IsSecretWord(word))
            {
                int eq = word!.IndexOf('=', 1);
                sink.Emit(channel, dir, null, 0, 0,
                    "len=" + count + " " + word.Substring(0, eq + 1) + "<redacted>");
                return;
            }
            Emit(channel, dir, data, offset, count, "len=" + count);
        }

        private static string Redacted(int count, string? note)
            => (string.IsNullOrEmpty(note) ? "" : note + " ") + "<redacted " + count + " bytes>";

        // Note-only event (no payload).
        internal static void Emit(string channel, TikWireDir dir, string note)
            => _sink?.Emit(channel, dir, null, 0, 0, note);

        /// <summary>
        /// Renders a byte span in the single canonical trace form: printable ASCII verbatim, and every
        /// other byte as a caret/escape token — <c>&lt;ESC&gt;</c> (0x1B), <c>&lt;CR&gt;</c> (0x0D),
        /// <c>&lt;LF&gt;</c> (0x0A), <c>&lt;TAB&gt;</c> (0x09), <c>&lt;NUL&gt;</c> (0x00), else
        /// <c>&lt;XX&gt;</c> in hex — so every channel reads identically.
        /// </summary>
        public static string Escape(byte[]? data, int offset, int count)
        {
            if (data == null || count <= 0) return string.Empty;
            var sb = new StringBuilder(count + count / 4);
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                byte b = data[i];
                switch (b)
                {
                    case 0x1B: sb.Append("<ESC>"); break;
                    case 0x0D: sb.Append("<CR>");  break;
                    case 0x0A: sb.Append("<LF>");  break;
                    case 0x09: sb.Append("<TAB>"); break;
                    case 0x00: sb.Append("<NUL>"); break;
                    default:
                        if (b >= 0x20 && b < 0x7F)
                            sb.Append((char)b);
                        else
                            sb.Append('<').Append(b.ToString("X2")).Append('>');
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Convenience overload rendering the whole array.</summary>
        public static string Escape(byte[]? data) => Escape(data, 0, data?.Length ?? 0);

        private sealed class SecretRestorer : IDisposable
        {
            private readonly bool _previous;
            private int _disposed;

            internal SecretRestorer(bool previous) => _previous = previous;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _secret.Value = _previous;
            }
        }

        private sealed class Restorer : IDisposable
        {
            private ITikWireTraceSink? _previous;
            private int _disposed;

            internal Restorer(ITikWireTraceSink? previous) => _previous = previous;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _sink = _previous;
                _previous = null;
            }
        }
    }
}
