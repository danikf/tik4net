using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Cli
{
    /// <summary>The byte stream under a PTY console: a Telnet socket or an SSH shell channel (<see cref="CliPtySession"/>).</summary>
    internal interface ICliByteStream
    {
        /// <summary>True when a read would return at once. Never blocks; false once the carrier is gone.</summary>
        bool HasData { get; }

        /// <summary>Reads what is buffered. Called only while <see cref="HasData"/>; 0 means the carrier closed.</summary>
        Task<int> ReadAsync(byte[] buffer, CancellationToken ct);

        /// <summary>The carrier has closed and nothing it delivered is left: how a close shows while <see cref="HasData"/>
        /// polls, since neither the Telnet socket nor the SSH channel reports one as data.</summary>
        bool ClosedAndDrained { get; }

        /// <summary>Writes <paramref name="bytes"/> to the carrier.</summary>
        Task WriteAsync(byte[] bytes, CancellationToken ct);
    }

    /// <summary>Strips carrier negotiation from a received chunk and returns what to answer (Telnet IAC).</summary>
    internal delegate byte[] CliInboundFilter(byte[] chunk, out byte[] reply);
}
