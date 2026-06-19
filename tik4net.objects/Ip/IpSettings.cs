using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/settings: Global IPv4 stack settings (singleton). Controls IP forwarding,
    /// ICMP behaviour, ARP timeouts, fast-path, TCP options and multipath hashing.
    /// Use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load.
    /// </summary>
    [TikEntity("/ip/settings", IsSingleton = true)]
    public class IpSettings
    {
        // --- Writable properties ---

        /// <summary>ip-forward — enables packet forwarding between interfaces. Default: yes.</summary>
        [TikProperty("ip-forward", DefaultValue = "yes")]
        public bool IpForward { get; set; }

        /// <summary>send-redirects — whether to send ICMP redirect messages. Default: yes.</summary>
        [TikProperty("send-redirects", DefaultValue = "yes")]
        public bool SendRedirects { get; set; }

        /// <summary>accept-redirects — whether to accept ICMP redirect messages. Default: no.</summary>
        [TikProperty("accept-redirects", DefaultValue = "no")]
        public bool AcceptRedirects { get; set; }

        /// <summary>accept-source-route — whether to accept packets with the SRR option. Default: no.</summary>
        [TikProperty("accept-source-route", DefaultValue = "no")]
        public bool AcceptSourceRoute { get; set; }

        /// <summary>secure-redirects — restrict ICMP redirects to recognised gateways only. Default: yes.</summary>
        [TikProperty("secure-redirects", DefaultValue = "yes")]
        public bool SecureRedirects { get; set; }

        /// <summary>rp-filter — reverse-path filter mode for source address validation.
        /// <seealso cref="RpFilterMode"/></summary>
        [TikProperty("rp-filter", DefaultValue = "no")]
        public RpFilterMode RpFilter { get; set; }

        /// <summary>allow-fast-path — enables Fast Path processing (automatically disabled when route-cache is off). Default: yes.</summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes")]
        public bool AllowFastPath { get; set; }

        /// <summary>tcp-syncookies — enables SYN-cookie protection against SYN-flood attacks. Default: no.</summary>
        [TikProperty("tcp-syncookies", DefaultValue = "no")]
        public bool TcpSyncookies { get; set; }

        /// <summary>tcp-timestamps — TCP timestamp behaviour.
        /// <seealso cref="TcpTimestampsMode"/></summary>
        [TikProperty("tcp-timestamps", DefaultValue = "random-offset")]
        public TcpTimestampsMode TcpTimestamps { get; set; }

        /// <summary>arp-timeout — base reachable time for ARP cache entries across interfaces. Default: 30s.</summary>
        [TikProperty("arp-timeout", DefaultValue = "30s")]
        public string/*time*/ ArpTimeout { get; set; }

        /// <summary>max-neighbor-entries — maximum ARP/NDP neighbour table size. Defaults are RAM-dependent; 0 = not set (use router default).</summary>
        [TikProperty("max-neighbor-entries", DefaultValue = "0")]
        public int MaxNeighborEntries { get; set; }

        /// <summary>icmp-rate-limit — minimum millisecond spacing between ICMP responses matching the rate mask. Default: 10.</summary>
        [TikProperty("icmp-rate-limit", DefaultValue = "10")]
        public int IcmpRateLimit { get; set; }

        /// <summary>icmp-rate-mask — hex bitmask of ICMP types subject to rate limiting. Default: 0x1818.</summary>
        [TikProperty("icmp-rate-mask", DefaultValue = "0x1818")]
        public string IcmpRateMask { get; set; }

        /// <summary>icmp-errors-use-inbound-interface-address — when yes, ICMP error replies use the primary address of the receiving interface as source. Default: no.</summary>
        [TikProperty("icmp-errors-use-inbound-interface-address", DefaultValue = "no")]
        public bool IcmpErrorsUseInboundInterfaceAddress { get; set; }

        /// <summary>ipv4-multipath-hash-policy — hash algorithm used for ECMP route selection.
        /// <seealso cref="MultipathHashPolicy"/></summary>
        [TikProperty("ipv4-multipath-hash-policy", DefaultValue = "l3")]
        public MultipathHashPolicy Ipv4MultipathHashPolicy { get; set; }

        // --- Read-only properties ---

        /// <summary>ipv4-fast-path-active — whether Fast Path is currently active.</summary>
        [TikProperty("ipv4-fast-path-active", IsReadOnly = true)]
        public bool Ipv4FastPathActive { get; private set; }

        /// <summary>ipv4-fast-path-packets — cumulative packets processed via Fast Path.</summary>
        [TikProperty("ipv4-fast-path-packets", IsReadOnly = true)]
        public long Ipv4FastPathPackets { get; private set; }

        /// <summary>ipv4-fast-path-bytes — cumulative bytes processed via Fast Path.</summary>
        [TikProperty("ipv4-fast-path-bytes", IsReadOnly = true)]
        public long Ipv4FastPathBytes { get; private set; }

        /// <summary>ipv4-fasttrack-active — whether Fasttrack is currently active.</summary>
        [TikProperty("ipv4-fasttrack-active", IsReadOnly = true)]
        public bool Ipv4FasttrackActive { get; private set; }

        /// <summary>ipv4-fasttrack-packets — cumulative packets processed via Fasttrack.</summary>
        [TikProperty("ipv4-fasttrack-packets", IsReadOnly = true)]
        public long Ipv4FasttrackPackets { get; private set; }

        /// <summary>ipv4-fasttrack-bytes — cumulative bytes processed via Fasttrack.</summary>
        [TikProperty("ipv4-fasttrack-bytes", IsReadOnly = true)]
        public long Ipv4FasttrackBytes { get; private set; }

        /// <summary>Human-readable settings summary.</summary>
        public override string ToString() => string.Format("ip-forward={0} fast-path={1} fasttrack={2}", IpForward, Ipv4FastPathActive, Ipv4FasttrackActive);
    }

    /// <summary>Reverse-path filter mode for <see cref="IpSettings.RpFilter"/>.</summary>
    public enum RpFilterMode
    {
        /// <summary>no — no reverse-path filtering (default).</summary>
        [TikEnum("no")] No,
        /// <summary>loose — loose RFC 3704 mode: packet source must be reachable via any interface.</summary>
        [TikEnum("loose")] Loose,
        /// <summary>strict — strict RFC 3704 mode: packet must arrive on the same interface that would be used to reach the source.</summary>
        [TikEnum("strict")] Strict,
    }

    /// <summary>TCP timestamp behaviour for <see cref="IpSettings.TcpTimestamps"/>.</summary>
    public enum TcpTimestampsMode
    {
        /// <summary>random-offset — timestamps enabled with a random offset per connection (default; mitigates some information leakage).</summary>
        [TikEnum("random-offset")] RandomOffset,
        /// <summary>enabled — TCP timestamps enabled without offset.</summary>
        [TikEnum("enabled")] Enabled,
        /// <summary>disabled — TCP timestamps disabled.</summary>
        [TikEnum("disabled")] Disabled,
    }

    /// <summary>ECMP multipath hash policy for <see cref="IpSettings.Ipv4MultipathHashPolicy"/>.</summary>
    public enum MultipathHashPolicy
    {
        /// <summary>l3 — hash on L3 (src/dst IP) — default.</summary>
        [TikEnum("l3")] L3,
        /// <summary>l4 — hash on L4 (src/dst IP + ports).</summary>
        [TikEnum("l4")] L4,
        /// <summary>l3-inner — hash on inner L3 headers (for tunnelled traffic).</summary>
        [TikEnum("l3-inner")] L3Inner,
    }
}
