using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace tik4net.Objects.Tool.Romon
{
    /// <summary>
    /// /tool/romon/discover — the RoMON neighbours this router can reach over the overlay, with the hop count,
    /// path cost and the path itself. A scan, not a table: it runs for <c>duration</c> and reports the
    /// neighbour set once a second, so the same neighbour arrives once per refresh over the binary API and
    /// REST, and once in total over the CLI transports. Use
    /// <see cref="ToolRomonDiscoverConnectionExtensions.RomonDiscover"/>, which returns each neighbour once.
    /// <para>Requires RoMON to be enabled on this router (<see cref="ToolRomon.Enabled"/>); otherwise the
    /// router refuses with <c>RoMON not running</c>. WinBox: Tools / RoMON / Discovery.</para>
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/RoMON</para>
    /// </summary>
    [TikEntity("/tool/romon/discover", LoadCommand = "", LoadDefaultParameterFormat = TikCommandParameterFormat.NameValue,
        SupportedOperations = TikEntityOperations.None, IncludeProplist = false)]
    public class ToolRomonDiscover
    {
        /// <summary>address — the neighbour's RoMON id (MAC-address format). The id a RoMON ping or a
        /// RoMON connection is addressed to.</summary>
        [TikProperty("address", IsReadOnly = true)]
        public string?/*MAC*/ Address { get; private set; }

        /// <summary>cost — the summed port cost of the path to the neighbour.</summary>
        [TikProperty("cost", IsReadOnly = true)]
        public long Cost { get; private set; }

        /// <summary>hops — number of RoMON hops to the neighbour (1 = directly adjacent).</summary>
        [TikProperty("hops", IsReadOnly = true)]
        public long Hops { get; private set; }

        /// <summary>path — the RoMON ids of the hops on the way to the neighbour, comma-separated.</summary>
        [TikProperty("path", IsReadOnly = true)]
        public string? Path { get; private set; }

        /// <summary>l2mtu — the smallest L2 MTU along the path. WinBox: "L2MTU".</summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public long L2Mtu { get; private set; }

        /// <summary>identity — the neighbour's system identity.</summary>
        [TikProperty("identity", IsReadOnly = true)]
        public string? Identity { get; private set; }

        /// <summary>version — the neighbour's RouterOS version.</summary>
        [TikProperty("version", IsReadOnly = true)]
        public string? Version { get; private set; }

        /// <summary>board — the neighbour's board name.</summary>
        [TikProperty("board", IsReadOnly = true)]
        public string? Board { get; private set; }

        /// <summary>uptime — the neighbour's uptime.</summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public TikDuration? Uptime { get; private set; }

        /// <summary>active — flag A. Only the CLI transports report it; it is <c>null</c> over the binary API
        /// and REST, which do not send the field.</summary>
        [TikProperty("active", IsReadOnly = true)]
        public bool? Active { get; private set; }

        /// <summary>Human-readable identity: RoMON id, identity and hop count.</summary>
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "{0} {1} (hops={2}, cost={3})", Address, Identity, Hops, Cost);
    }

    /// <summary>
    /// Connection extension class for <see cref="ToolRomonDiscover"/>
    /// </summary>
    [RequiresUnreferencedCode(TikTrimming.MapperMessage)]
    [RequiresDynamicCode(TikTrimming.DynamicCodeMessage)]
    public static class ToolRomonDiscoverConnectionExtensions
    {
        /// <summary>
        /// Scans the RoMON overlay for <paramref name="durationSeconds"/> seconds and returns each neighbour
        /// once — the last report the scan produced for it.
        /// </summary>
        /// <param name="connection">Connection to the router whose neighbours are wanted (RoMON must be enabled on it).</param>
        /// <param name="durationSeconds">How long to scan. At least 2: over the CLI transports a shorter scan
        /// reports nothing.</param>
        public static IEnumerable<ToolRomonDiscover> RomonDiscover(this ITikConnection connection, int durationSeconds = 3)
        {
            if (durationSeconds < 2)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds,
                    "A RoMON discover shorter than 2 seconds reports nothing over the CLI transports.");

            var rows = connection.LoadList<ToolRomonDiscover>(
                connection.CreateParameter("duration", durationSeconds.ToString(CultureInfo.InvariantCulture),
                    TikCommandParameterFormat.NameValue));

            // The API and REST repeat the whole neighbour set once per refresh; keep the latest report.
            var byAddress = new Dictionary<string, ToolRomonDiscover>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var row in rows)
            {
                string key = row.Address ?? string.Empty;
                if (!byAddress.ContainsKey(key)) order.Add(key);
                byAddress[key] = row;
            }
            return order.Select(k => byAddress[k]).ToList();
        }
    }
}
