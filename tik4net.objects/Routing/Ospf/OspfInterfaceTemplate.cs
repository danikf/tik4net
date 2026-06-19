namespace tik4net.Objects.Routing.Ospf
{
    /// <summary>
    /// /routing/ospf/interface-template
    ///
    /// OSPF interface template (RouterOS 7+). Templates define OSPF parameters for interfaces or
    /// networks that match the template. Each template is bound to an OSPF area. Router assigns
    /// OSPF settings (hello/dead intervals, cost, type, auth, etc.) to matching interfaces based
    /// on the first matching template in the ordered list.
    /// </summary>
    [TikEntity("/routing/ospf/interface-template", IncludeDetails = true, IsOrdered = true)]
    public class OspfInterfaceTemplate
    {
        /// <summary>OSPF network/interface type.</summary>
        public enum OspfNetworkType
        {
            /// <summary>broadcast — Ethernet-like segment with DR/BDR election. Router default.</summary>
            [TikEnum("broadcast")] Broadcast,
            /// <summary>nbma — Non-Broadcast Multi-Access; requires manual neighbour configuration.</summary>
            [TikEnum("nbma")] Nbma,
            /// <summary>ptmp — Point-to-Multipoint; each neighbour is treated as a separate point-to-point link.</summary>
            [TikEnum("ptmp")] Ptmp,
            /// <summary>ptmp-broadcast — Point-to-Multipoint Broadcast.</summary>
            [TikEnum("ptmp-broadcast")] PtmpBroadcast,
            /// <summary>ptp — Point-to-Point; no DR/BDR election.</summary>
            [TikEnum("ptp")] Ptp,
            /// <summary>ptp-unnumbered — Point-to-Point unnumbered link.</summary>
            [TikEnum("ptp-unnumbered")] PtpUnnumbered,
            /// <summary>virtual-link — OSPF virtual link through a transit area.</summary>
            [TikEnum("virtual-link")] VirtualLink,
        }

        /// <summary>OSPF authentication method.</summary>
        public enum OspfAuthType
        {
            /// <summary>md5 — HMAC-MD5 cryptographic authentication.</summary>
            [TikEnum("md5")] Md5,
            /// <summary>sha1 — HMAC-SHA1 cryptographic authentication.</summary>
            [TikEnum("sha1")] Sha1,
            /// <summary>sha256 — HMAC-SHA256 cryptographic authentication.</summary>
            [TikEnum("sha256")] Sha256,
            /// <summary>sha384 — HMAC-SHA384 cryptographic authentication.</summary>
            [TikEnum("sha384")] Sha384,
            /// <summary>sha512 — HMAC-SHA512 cryptographic authentication.</summary>
            [TikEnum("sha512")] Sha512,
            /// <summary>simple — plain-text password authentication.</summary>
            [TikEnum("simple")] Simple,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// area — name of the OSPF area this template is assigned to.
        /// Must reference an existing /routing/ospf/area entry.
        /// This field is mandatory — the router rejects add without it.
        /// </summary>
        [TikProperty("area", IsMandatory = true)]
        public string Area { get; set; }

        /// <summary>
        /// interfaces — comma-separated list of interface names this template applies to.
        /// Mutually exclusive with networks. Leave empty to match all interfaces in the area.
        /// </summary>
        [TikProperty("interfaces")]
        public string Interfaces { get; set; }

        /// <summary>
        /// networks — IP network (prefix) this template applies to.
        /// Mutually exclusive with interfaces.
        /// </summary>
        [TikProperty("networks")]
        public string Networks { get; set; }

        /// <summary>
        /// type — OSPF network type for matched interfaces. Controls DR/BDR election and neighbour discovery.
        /// Default: broadcast
        /// </summary>
        /// <seealso cref="OspfNetworkType"/>
        [TikProperty("type", DefaultValue = "broadcast")]
        public OspfNetworkType Type { get; set; }

        /// <summary>
        /// cost — interface metric (link cost) advertised in LSAs.
        /// Valid range: 1..65535. DefaultValue="0" is a CLR sentinel so an unset field is omitted on add;
        /// set to a real value to override the router's built-in default (1).
        /// </summary>
        [TikProperty("cost", DefaultValue = "0")]
        public int Cost { get; set; }

        /// <summary>
        /// priority — router priority used in DR/BDR election on broadcast/NBMA networks.
        /// Higher value = more likely to be elected DR. Range 0..255. 0 prevents DR election.
        /// DefaultValue="0" is a CLR sentinel so an unset field is omitted on add;
        /// set to a real value to override the router default (128).
        /// </summary>
        [TikProperty("priority", DefaultValue = "0")]
        public int Priority { get; set; }

        /// <summary>
        /// hello-interval — interval between OSPF Hello packets. Must match all neighbours on the segment.
        /// Default: 10s
        /// </summary>
        [TikProperty("hello-interval", DefaultValue = "10s")]
        public string/*time*/ HelloInterval { get; set; }

        /// <summary>
        /// dead-interval — time after which a silent neighbour is declared dead. Typically 4× hello-interval.
        /// Must match all neighbours on the segment. Default: 40s
        /// </summary>
        [TikProperty("dead-interval", DefaultValue = "40s")]
        public string/*time*/ DeadInterval { get; set; }

        /// <summary>
        /// retransmit-interval — time between LSA retransmissions to a neighbour. Default: 5s
        /// </summary>
        [TikProperty("retransmit-interval", DefaultValue = "5s")]
        public string/*time*/ RetransmitInterval { get; set; }

        /// <summary>
        /// transmit-delay — estimated time to transmit an LSA; added to the age of LSAs before flooding.
        /// Default: 1s
        /// </summary>
        [TikProperty("transmit-delay", DefaultValue = "1s")]
        public string/*time*/ TransmitDelay { get; set; }

        /// <summary>
        /// instance-id — OSPF instance ID used in OSPFv3 to separate multiple instances on the same link.
        /// Default: 0
        /// </summary>
        [TikProperty("instance-id", DefaultValue = "0")]
        public int InstanceId { get; set; }

        /// <summary>
        /// auth — authentication type for OSPF packets on matched interfaces.
        /// Leave unset (or omit) for no authentication.
        /// </summary>
        /// <seealso cref="OspfAuthType"/>
        [TikProperty("auth")]
        public string Auth { get; set; }

        /// <summary>
        /// auth-id — key ID used with cryptographic authentication (md5/sha*). Range 1..255.
        /// DefaultValue="0" is a CLR sentinel so an unset field is omitted on add.
        /// </summary>
        [TikProperty("auth-id", DefaultValue = "0")]
        public int AuthId { get; set; }

        /// <summary>
        /// auth-key — authentication key/password string for OSPF packet authentication.
        /// </summary>
        [TikProperty("auth-key")]
        public string AuthKey { get; set; }

        /// <summary>
        /// passive — when true the interface is passive: OSPF adjacencies are not formed, but the
        /// network is still advertised into OSPF (if matched by a network statement or interfaces).
        /// Default: false (no)
        /// </summary>
        [TikProperty("passive", DefaultValue = "no")]
        public bool Passive { get; set; }

        /// <summary>
        /// use-bfd — enable Bidirectional Forwarding Detection (BFD) for faster neighbour failure detection.
        /// Default: false (no)
        /// </summary>
        [TikProperty("use-bfd", DefaultValue = "no")]
        public bool UseBfd { get; set; }

        /// <summary>
        /// prefix-list — name of an IP prefix list used to filter networks redistributed into OSPF via this template.
        /// </summary>
        [TikProperty("prefix-list")]
        public string PrefixList { get; set; }

        /// <summary>
        /// vlink-neighbor-id — router-id of the virtual link neighbour. Used when type=virtual-link.
        /// </summary>
        [TikProperty("vlink-neighbor-id")]
        public string/*IP*/ VlinkNeighborId { get; set; }

        /// <summary>
        /// vlink-transit-area — name of the transit area through which the virtual link passes.
        /// Used when type=virtual-link.
        /// </summary>
        [TikProperty("vlink-transit-area")]
        public string VlinkTransitArea { get; set; }

        /// <summary>
        /// disabled — when true this template entry is administratively disabled.
        /// Default: false (no)
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// inactive — true when this template is not active (e.g. the area or instance is disabled
        /// or the routing package is not running).
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("area={0} interfaces={1}", Area, Interfaces);
    }
}
