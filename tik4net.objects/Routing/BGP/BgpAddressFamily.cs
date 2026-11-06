namespace tik4net.Objects.Routing.Bgp
{
    /// <summary>An address family a RouterOS 6 BGP peer carries, a member of <see cref="BgpPeer.AddressFamilies"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>ipv6,ip</c> reads back <c>ip,ipv6</c>), so a list merge compares it by members.
    /// </remarks>
    public enum BgpAddressFamily
    {
        /// <summary>ip — IPv4 unicast.</summary>
        [TikEnum("ip")] Ip,
        /// <summary>ipv6 — IPv6 unicast.</summary>
        [TikEnum("ipv6")] Ipv6,
        /// <summary>l2vpn — VPLS (RFC 4761).</summary>
        [TikEnum("l2vpn")] L2vpn,
        /// <summary>vpnv4 — BGP/MPLS IPv4 VPN.</summary>
        [TikEnum("vpnv4")] Vpnv4,
        /// <summary>l2vpn-cisco — Cisco-style VPLS signalling.</summary>
        [TikEnum("l2vpn-cisco")] L2vpnCisco,
    }
}
