namespace tik4net.Objects.Ip.Firewall
{
    // The members of the firewall's multi-value matchers, shared by the filter and raw rules. The words are the router's
    // own (roteros.jg, 7.24.5); a word a later RouterOS adds reads as a word item of the list, not as a failure.

    /// <summary>A member of <c>connection-state</c> — the connection tracking state a packet is in.</summary>
    public enum FirewallConnectionState
    {
        /// <summary>A packet that could not be identified.</summary>
        [TikEnum("invalid")] Invalid,
        /// <summary>A packet of an existing connection.</summary>
        [TikEnum("established")] Established,
        /// <summary>A packet related to an existing connection (an ICMP error, FTP data).</summary>
        [TikEnum("related")] Related,
        /// <summary>A packet that started a new connection, or one not yet seen in both directions.</summary>
        [TikEnum("new")] New,
        /// <summary>A packet the raw table marked <c>notrack</c>.</summary>
        [TikEnum("untracked")] Untracked,
    }

    /// <summary>A member of <c>src-address-type</c> / <c>dst-address-type</c>.</summary>
    public enum FirewallAddressType
    {
        /// <summary>An address used for point-to-point transmission.</summary>
        [TikEnum("unicast")] Unicast,
        /// <summary>An address assigned to one of the router's interfaces.</summary>
        [TikEnum("local")] Local,
        /// <summary>A broadcast address.</summary>
        [TikEnum("broadcast")] Broadcast,
        /// <summary>A multicast address.</summary>
        [TikEnum("multicast")] Multicast,
        /// <summary>An address routed to a blackhole route.</summary>
        [TikEnum("blackhole")] Blackhole,
    }

    /// <summary>A member of <c>hotspot</c> — how a packet relates to the HotSpot. Each member can be negated on its own.</summary>
    public enum FirewallHotspotMatch
    {
        /// <summary>A packet from a HotSpot client.</summary>
        [TikEnum("from-client")] FromClient,
        /// <summary>A packet from an authenticated HotSpot client.</summary>
        [TikEnum("auth")] Auth,
        /// <summary>A packet to the HotSpot server itself.</summary>
        [TikEnum("local-dst")] LocalDst,
        /// <summary>A packet to a HotSpot client.</summary>
        [TikEnum("to-client")] ToClient,
        /// <summary>A packet the HotSpot's transparent proxy handles.</summary>
        [TikEnum("http")] Http,
    }

    /// <summary>A member of <c>tcp-flags</c>. Each member can be negated on its own, and so can the whole list.</summary>
    public enum FirewallTcpFlag
    {
        /// <summary>FIN.</summary>
        [TikEnum("fin")] Fin,
        /// <summary>SYN.</summary>
        [TikEnum("syn")] Syn,
        /// <summary>RST.</summary>
        [TikEnum("rst")] Rst,
        /// <summary>PSH.</summary>
        [TikEnum("psh")] Psh,
        /// <summary>ACK.</summary>
        [TikEnum("ack")] Ack,
        /// <summary>URG.</summary>
        [TikEnum("urg")] Urg,
        /// <summary>ECE.</summary>
        [TikEnum("ece")] Ece,
        /// <summary>CWR.</summary>
        [TikEnum("cwr")] Cwr,
    }
}
