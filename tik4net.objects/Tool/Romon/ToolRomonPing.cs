using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace tik4net.Objects.Tool.Romon
{
    /// <summary>
    /// /tool/romon/ping — echo over the RoMON overlay to a node identified by its RoMON id, the RoMON
    /// counterpart of <see cref="ToolPing"/>. One row per echo; the running totals (<c>sent</c>,
    /// <c>received</c>, <c>packet-loss</c>, the round-trip statistics) ride on each row. A timed-out echo has
    /// <see cref="Status"/> <c>timeout</c> and no <see cref="Time"/>.
    /// <para>Requires RoMON to be enabled on this router (<see cref="ToolRomon.Enabled"/>); otherwise the
    /// router refuses with <c>RoMON not running</c>. WinBox: Tools / RoMON / Ping.</para>
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/RoMON</para>
    /// </summary>
    [TikEntity("/tool/romon/ping", LoadCommand = "", LoadDefaultParameterFormat = TikCommandParameterFormat.NameValue,
        SupportedOperations = TikEntityOperations.None, IncludeProplist = false)]
    public class ToolRomonPing
    {
        /// <summary>seq — sequence number of the echo. WinBox: "Seq #".</summary>
        [TikProperty("seq", IsReadOnly = true)]
        public long SequenceNo { get; private set; }

        /// <summary>host — the RoMON id that answered (or was asked, on a timeout).</summary>
        [TikProperty("host", IsReadOnly = true)]
        public string?/*MAC*/ Host { get; private set; }

        /// <summary>time — the round-trip time; <c>null</c> when the echo timed out.</summary>
        [TikProperty("time", IsReadOnly = true)]
        public TikDuration? Time { get; private set; }

        /// <summary>size — packet size in bytes. WinBox: "Reply Size".</summary>
        [TikProperty("size", IsReadOnly = true)]
        public long Size { get; private set; }

        /// <summary>status — empty on a reply, <c>timeout</c> when none came back.</summary>
        [TikProperty("status", IsReadOnly = true)]
        public string? Status { get; private set; }

        /// <summary>sent — echoes sent so far.</summary>
        [TikProperty("sent", IsReadOnly = true)]
        public string? Sent { get; private set; }

        /// <summary>received — replies received so far.</summary>
        [TikProperty("received", IsReadOnly = true)]
        public string? Received { get; private set; }

        /// <summary>packet-loss — loss so far, in percent.</summary>
        [TikProperty("packet-loss", IsReadOnly = true)]
        public string? PacketLoss { get; private set; }

        /// <summary>min-rtt — shortest round trip so far. WinBox: "Min".</summary>
        [TikProperty("min-rtt", IsReadOnly = true)]
        public TikDuration? MinRtt { get; private set; }

        /// <summary>avg-rtt — average round trip so far. WinBox: "Avg".</summary>
        [TikProperty("avg-rtt", IsReadOnly = true)]
        public TikDuration? AvgRtt { get; private set; }

        /// <summary>max-rtt — longest round trip so far. WinBox: "Max".</summary>
        [TikProperty("max-rtt", IsReadOnly = true)]
        public TikDuration? MaxRtt { get; private set; }

        /// <summary>Human-readable identity: host and round trip, or the status when there was no reply.</summary>
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "{0} ....... {1}", Host,
                Time?.ToString() ?? (string.IsNullOrEmpty(Status) ? "(no reply)" : Status));
    }

    /// <summary>
    /// Connection extension class for <see cref="ToolRomonPing"/>
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class ToolRomonPingConnectionExtensions
    {
        /// <summary>
        /// Sends <paramref name="count"/> RoMON echoes to the node <paramref name="romonId"/> and returns one
        /// row per echo.
        /// </summary>
        /// <param name="connection">Connection to the router the echoes leave from (RoMON must be enabled on it).</param>
        /// <param name="romonId">The target's RoMON id, MAC-address format — see <see cref="ToolRomonDiscover.Address"/>.</param>
        /// <param name="count">Number of echoes.</param>
        public static IEnumerable<ToolRomonPing> RomonPing(this ITikConnection connection, string romonId, int count = 4)
        {
            if (string.IsNullOrEmpty(romonId)) throw new ArgumentException("A RoMON id is required.", nameof(romonId));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "At least one echo.");

            return connection.LoadList<ToolRomonPing>(
                connection.CreateParameter("id", romonId, TikCommandParameterFormat.NameValue),
                connection.CreateParameter("count", count.ToString(CultureInfo.InvariantCulture), TikCommandParameterFormat.NameValue));
        }
    }
}
