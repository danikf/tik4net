using System;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>One RouterOS console behind whatever carries it (TCP, SSH PTY, MAC layer, WinBox mepty).</summary>
    internal interface ICliTerminal : IDisposable
    {
        /// <summary>Types a command line and reads its cleaned answer; <paramref name="onLine"/> gets each completed line.</summary>
        Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct);

        /// <summary>Sends keystrokes with no line end and reads to the next prompt (Safe Mode keys).</summary>
        Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct);

        /// <summary>Sends keystrokes and reads until the console is quiet (Tab completion, F1, resync).</summary>
        Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct);

        /// <summary>Whether <see cref="SendRawAndReadUntilQuietAsync"/> works. All four clients: true.</summary>
        bool SupportsQuietRead { get; }

        /// <summary>Whether <c>onLine</c> is fed while the command runs. All four clients: true.</summary>
        bool SupportsLineStreaming { get; }

        /// <summary>
        /// Ends the console session — <c>/quit</c>, answering the Safe Mode question with <c>y</c> when held — and
        /// releases the carrier. Never throws. <see cref="IDisposable.Dispose"/> releases it without answering the
        /// question (Telnet and SSH still type <c>/quit</c>; the MAC-layer and WinBox clients just drop the carrier).
        /// </summary>
        void End(bool answerSafeModeQuestion);
    }
}
