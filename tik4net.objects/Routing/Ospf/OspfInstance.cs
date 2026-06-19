namespace tik4net.Objects.Routing.Ospf
{
    /// <summary>
    /// /routing/ospf/instance
    ///
    /// OSPF instance configuration (RouterOS 7+). Each instance groups OSPF areas and defines
    /// the global OSPF parameters: protocol version (OSPFv2 for IPv4, OSPFv3 for IPv6), the
    /// router-id, route redistribution policy, default-route origination, VRF binding, and
    /// optional MPLS-TE and route-filter chains.
    /// </summary>
    [TikEntity("/routing/ospf/instance", IncludeDetails = true)]
    public class OspfInstance
    {
        /// <summary>OSPF protocol version.</summary>
        public enum OspfVersion
        {
            /// <summary>version 2 — OSPFv2 (IPv4). Default.</summary>
            [TikEnum("2")] V2,
            /// <summary>version 3 — OSPFv3 (IPv6).</summary>
            [TikEnum("3")] V3,
        }

        /// <summary>Controls whether a default route is originated into the OSPF domain.</summary>
        public enum OriginateDefaultMode
        {
            /// <summary>never — do not originate a default route. Default.</summary>
            [TikEnum("never")] Never,
            /// <summary>always — always originate a default route.</summary>
            [TikEnum("always")] Always,
            /// <summary>if-installed — originate a default route only when one is present in the routing table.</summary>
            [TikEnum("if-installed")] IfInstalled,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — unique identifier for this OSPF instance.
        /// Referenced by /routing/ospf/area entries via the "instance" field.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// version — OSPF protocol version to use.
        /// OSPFv2 operates over IPv4; OSPFv3 operates over IPv6.
        /// Default: 2
        /// </summary>
        /// <seealso cref="OspfVersion"/>
        [TikProperty("version", DefaultValue = "2")]
        public OspfVersion Version { get; set; }

        /// <summary>
        /// router-id — the identifier used to represent this router in the OSPF domain.
        /// Accepts an IPv4 address (e.g. "1.2.3.4") or a symbolic routing-ID reference (e.g. "main").
        /// Default: main (auto-selected from the routing table).
        /// </summary>
        [TikProperty("router-id", DefaultValue = "main")]
        public string RouterId { get; set; }

        /// <summary>
        /// vrf — VRF (Virtual Routing and Forwarding) instance this OSPF instance is bound to.
        /// Default: main
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        /// <summary>
        /// routing-table — the routing table in which OSPF-learned routes are installed.
        /// Accepts a routing-table name (e.g. "main"). When unset, defaults to the VRF table.
        /// </summary>
        [TikProperty("routing-table")]
        public string RoutingTable { get; set; }

        /// <summary>
        /// originate-default — controls origination of a default route (0.0.0.0/0) into the OSPF domain.
        /// Default: never
        /// </summary>
        /// <seealso cref="OriginateDefaultMode"/>
        [TikProperty("originate-default", DefaultValue = "never")]
        public OriginateDefaultMode OriginateDefault { get; set; }

        /// <summary>
        /// redistribute — comma-separated list of route sources whose routes are redistributed
        /// into OSPF as external LSAs. Accepted values: bgp, bgp-mpls-vpn, connected, dhcp,
        /// fantasy, isis, modem, ospf, rip, slaac, static, vpn.
        /// Example: "connected,static"
        /// </summary>
        [TikProperty("redistribute")]
        public string Redistribute { get; set; }

        /// <summary>
        /// domain-id — BGP/MPLS VPN domain identifier attached to OSPF LSAs when the instance
        /// is used in a VPN context. Expressed as an extended-community value (e.g. "1.0.0.0:0").
        /// </summary>
        [TikProperty("domain-id")]
        public string DomainId { get; set; }

        /// <summary>
        /// domain-tag — OSPF domain tag value used in VPN route exchange to prevent routing loops.
        /// Numeric string (e.g. "100").
        /// </summary>
        [TikProperty("domain-tag")]
        public string DomainTag { get; set; }

        /// <summary>
        /// in-filter-chain — routing filter chain applied to routes received (imported) via OSPF
        /// before they are installed in the routing table.
        /// </summary>
        [TikProperty("in-filter-chain")]
        public string InFilterChain { get; set; }

        /// <summary>
        /// out-filter-chain — routing filter chain applied to routes being redistributed out into OSPF.
        /// </summary>
        [TikProperty("out-filter-chain")]
        public string OutFilterChain { get; set; }

        /// <summary>
        /// out-filter-select — routing filter chain used to select which routes are eligible for
        /// redistribution into OSPF (applied before out-filter-chain).
        /// </summary>
        [TikProperty("out-filter-select")]
        public string OutFilterSelect { get; set; }

        /// <summary>
        /// mpls-te-area — OSPF area used for MPLS Traffic Engineering extensions (opaque LSAs).
        /// Specify the area identifier (e.g. "backbone" or "0.0.0.0").
        /// </summary>
        [TikProperty("mpls-te-area")]
        public string MplsTeArea { get; set; }

        /// <summary>
        /// mpls-te-address — router address advertised in MPLS-TE LSAs. Typically an IPv4 loopback address.
        /// </summary>
        [TikProperty("mpls-te-address")]
        public string MplsTeAddress { get; set; }

        /// <summary>
        /// use-dn — when true, sets the DN (Down) bit on VPN LSAs to prevent routing loops in
        /// MPLS/BGP VPN scenarios. Default: yes (true).
        /// </summary>
        [TikProperty("use-dn", DefaultValue = "yes")]
        public bool UseDn { get; set; }

        /// <summary>
        /// disabled — when true the OSPF instance is administratively disabled.
        /// Default: false
        /// </summary>
        [TikProperty("disabled", DefaultValue = "false")]
        public bool Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// inactive — true when the OSPF instance is not active (e.g. no areas configured,
        /// or the routing package is not running).
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
