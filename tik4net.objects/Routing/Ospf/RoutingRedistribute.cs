namespace tik4net.Objects.Routing.Ospf
{
    /// <summary>A source of routes a routing instance redistributes, a member of <see cref="OspfInstance.Redistribute"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>static,connected,bgp</c> reads back <c>connected,static,bgp</c>), so a list merge compares it by members.
    /// </remarks>
    public enum RoutingRedistribute
    {
        /// <summary>connected — routes of the router's own networks.</summary>
        [TikEnum("connected")] Connected,
        /// <summary>static — static routes.</summary>
        [TikEnum("static")] Static,
        /// <summary>rip — RIP routes.</summary>
        [TikEnum("rip")] Rip,
        /// <summary>ospf — OSPF routes (of another instance).</summary>
        [TikEnum("ospf")] Ospf,
        /// <summary>bgp — BGP routes.</summary>
        [TikEnum("bgp")] Bgp,
        /// <summary>vpn — VPN routes.</summary>
        [TikEnum("vpn")] Vpn,
        /// <summary>dhcp — routes from DHCP.</summary>
        [TikEnum("dhcp")] Dhcp,
        /// <summary>fantasy — routes the router makes up for testing.</summary>
        [TikEnum("fantasy")] Fantasy,
        /// <summary>modem — routes of a modem interface.</summary>
        [TikEnum("modem")] Modem,
        /// <summary>bgp-mpls-vpn — BGP MPLS VPN routes.</summary>
        [TikEnum("bgp-mpls-vpn")] BgpMplsVpn,
        /// <summary>isis — IS-IS routes.</summary>
        [TikEnum("isis")] Isis,
        /// <summary>slaac — routes learnt by IPv6 SLAAC.</summary>
        [TikEnum("slaac")] Slaac,
    }
}
