using System;
using System.Globalization;
using System.IO;
using System.Text;
using tik4net.Diagnostics;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Opt-in byte-level wire trace for a whole suite run, written to a file.
    /// <para>
    /// Enabled only when the <c>TIK4NET_WIRETRACE</c> environment variable is set (to the target file
    /// path, or to <c>1</c> for a default under <c>TestResults</c>); otherwise nothing is installed and
    /// <see cref="TikWireTrace"/> stays at its one-null-check cost. It exists because some defects only
    /// appear in a FULL suite run against one long-lived shared connection and cannot be reproduced by a
    /// standalone command — P2.19's MAC-layer wedge is the case in point, where the MCP tool's
    /// per-call <c>traceLevel=bytes</c> cannot reach the failure at all.
    /// </para>
    /// <para>
    /// Test boundaries are written into the trace as <c>--- TEST &lt;name&gt;</c> markers so a failure can
    /// be located in it without correlating timestamps by hand.
    /// </para>
    /// </summary>
    internal static class WireTraceCapture
    {
        private static IDisposable _subscription;
        private static FileSink _sink;

        internal static string FilePath { get; private set; }

        internal static bool IsEnabled => _sink != null;

        internal static void StartIfRequested()
        {
            string setting = Environment.GetEnvironmentVariable("TIK4NET_WIRETRACE");
            if (string.IsNullOrEmpty(setting))
                return;

            FilePath = (setting == "1")
                ? Path.Combine(Path.GetDirectoryName(typeof(WireTraceCapture).Assembly.Location) ?? ".",
                               "wiretrace.txt")
                : setting;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)));
            _sink = new FileSink(FilePath);
            _subscription = TikWireTrace.Capture(_sink);
            _sink.Note("wire trace started " + DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
        }

        internal static void Stop()
        {
            if (_sink == null)
                return;
            _sink.Note("wire trace stopped");
            _subscription?.Dispose();
            _sink.Dispose();
            _subscription = null;
            _sink = null;
        }

        /// <summary>Writes a test-boundary marker so the trace can be sliced per test.</summary>
        internal static void MarkTest(string phase, string testName)
            => _sink?.Note("--- " + phase + " " + testName);

        private sealed class FileSink : ITikWireTraceSink, IDisposable
        {
            private readonly object _lock = new object();
            private readonly StreamWriter _writer;

            internal FileSink(string path)
            {
                _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true,   // the interesting run ends in a hang, so buffered output would be lost
                };
            }

            public void Emit(string channel, TikWireDir dir, byte[] data, int offset, int count, string note)
            {
                // Emit runs on whichever thread does the I/O — for WinBox-native that is the multiplexer's
                // reader thread, not the caller's — so this must be locked (see ITikWireTraceSink remarks).
                lock (_lock)
                {
                    var sb = new StringBuilder();
                    sb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                    sb.Append(' ').Append(channel).Append(' ').Append(dir.ToString().ToUpperInvariant());
                    if (!string.IsNullOrEmpty(note))
                        sb.Append(' ').Append(note);
                    if (data != null && count > 0)
                        sb.Append(" len=").Append(count).Append(" | ").Append(TikWireTrace.Escape(data, offset, count));
                    _writer.WriteLine(sb.ToString());
                }
            }

            internal void Note(string text)
            {
                lock (_lock)
                    _writer.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + text);
            }

            public void Dispose()
            {
                lock (_lock)
                    _writer.Dispose();
            }
        }
    }
}
