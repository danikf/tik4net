namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/mode-config
    ///
    /// IKEv2 Mode Config (RFC 7296 §3.15) allows the responder (server) to assign an IP address,
    /// DNS servers, and split-tunnel routes to the initiator (client) during IKE_AUTH exchange.
    /// Each entry defines either a responder-side pool configuration or an initiator-side request
    /// profile. Entries are referenced from /ip/ipsec/peer via the mode-config parameter.
    /// </summary>
    [TikEntity("/ip/ipsec/mode-config", IncludeDetails = true)]
    public class IpsecModeConfig
    {
        /// <summary>Possible values for the use-responder-dns property.</summary>
        public enum UseResponderDnsType
        {
            /// <summary>exclusively — use only the DNS servers received from the responder; ignore locally configured DNS.</summary>
            [TikEnum("exclusively")] Exclusively,
            /// <summary>no — do not request or use DNS servers from the responder.</summary>
            [TikEnum("no")] No,
            /// <summary>yes — accept DNS servers from the responder and add them alongside locally configured ones.</summary>
            [TikEnum("yes")] Yes,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — identifier for this mode-config entry; referenced from /ip/ipsec/peer.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// responder — when true this entry acts as a responder (server) and assigns addresses/DNS
        /// to connecting clients; when false it is an initiator (client) profile that requests
        /// configuration from the remote server.
        /// Default: no
        /// </summary>
        [TikProperty("responder", DefaultValue = "no")]
        public bool Responder { get; set; }

        // --- Responder-side (server) fields ---

        /// <summary>
        /// address-pool — name of the IP pool (/ip/pool) from which addresses are assigned to
        /// initiators. Applicable when responder=yes.
        /// </summary>
        [TikProperty("address-pool")]
        public string AddressPool { get; set; }

        /// <summary>
        /// address-prefix-length — prefix length (subnet mask) of the address assigned from the
        /// pool to the initiator. Valid range: 1–32. When not set the router uses its own default.
        /// Document intent: a value of 0 means "not set" and the mapper omits the field on add.
        /// Applicable when responder=yes.
        /// </summary>
        [TikProperty("address-prefix-length")]
        public int AddressPrefixLength { get; set; }

        /// <summary>
        /// split-include — comma-separated list of subnets in CIDR notation to tunnel to the
        /// client (split-tunnelling include list). Sent to the initiator as traffic-selectors.
        /// Applicable when responder=yes.
        /// </summary>
        [TikProperty("split-include")]
        public string SplitInclude { get; set; }

        /// <summary>
        /// split-dns — list of DNS domain suffixes that the initiator should resolve using the
        /// VPN-assigned DNS servers rather than its local resolver. Applicable when responder=yes.
        /// </summary>
        [TikProperty("split-dns")]
        public string SplitDns { get; set; }

        /// <summary>
        /// system-dns — when true, the router sends its own /ip/dns server addresses to the
        /// initiator as part of Mode Config. Cannot be used together with static-dns.
        /// Applicable when responder=yes.
        /// </summary>
        [TikProperty("system-dns")]
        public bool SystemDns { get; set; }

        /// <summary>
        /// static-dns — manually specified DNS server IP address(es) (comma-separated) sent to the
        /// initiator via Mode Config. Cannot be used together with system-dns.
        /// Applicable when responder=yes.
        /// </summary>
        [TikProperty("static-dns")]
        public string/*IP list*/ StaticDns { get; set; }

        // --- Initiator-side (client) fields ---

        /// <summary>
        /// address — single IP address to request from the responder instead of relying on an
        /// address pool. When set the initiator proposes this specific address during Mode Config
        /// exchange. Applicable when responder=no.
        /// </summary>
        [TikProperty("address")]
        public string/*IP*/ Address { get; set; }

        /// <summary>
        /// src-address-list — name of an address list (/ip/firewall/address-list) for which
        /// dynamic source-NAT rules are generated so that traffic from those addresses is
        /// routed over the VPN tunnel. Applicable when responder=no.
        /// </summary>
        [TikProperty("src-address-list")]
        public string SrcAddressList { get; set; }

        /// <summary>
        /// use-responder-dns — controls whether DNS servers received from the responder during
        /// Mode Config are used by the initiator.
        ///   exclusively — use only the responder-provided DNS; ignore local DNS.
        ///   yes          — add responder DNS alongside local DNS.
        ///   no           — ignore responder DNS entirely.
        /// Default: exclusively
        /// <seealso cref="UseResponderDnsType"/>
        /// Applicable when responder=no.
        /// </summary>
        [TikProperty("use-responder-dns", DefaultValue = "exclusively")]
        public UseResponderDnsType UseResponderDns { get; set; }

        // --- Shared fields ---

        /// <summary>
        /// connection-mark — firewall connection mark to match. When set only connections with
        /// the specified mark are processed by this mode-config entry.
        /// </summary>
        [TikProperty("connection-mark")]
        public string ConnectionMark { get; set; }

        // NOTE: /ip/ipsec/mode-config has no "comment" field on RouterOS (confirmed via
        // add-completion and print), so no Comment property is exposed here.

        // --- Read-only properties ---

        /// <summary>
        /// default — true when this entry is a system-generated default that cannot be deleted.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
