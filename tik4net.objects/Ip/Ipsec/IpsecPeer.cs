namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/peer
    ///
    /// IKE peer configuration. Each peer entry defines a remote peer (matched by address prefix)
    /// and the IKE exchange parameters (profile, exchange mode, local address, port) used when
    /// establishing an IPsec Security Association with that peer.
    /// </summary>
    [TikEntity("/ip/ipsec/peer", IncludeDetails = true)]
    public class IpsecPeer
    {
        /// <summary>Possible IKE exchange modes for Phase 1 negotiation (RFC 2408).</summary>
        public enum ExchangeModeType
        {
            /// <summary>main — Main Mode (identity protection), the standard IKEv1 Phase 1 mode.</summary>
            [TikEnum("main")] Main,
            /// <summary>aggressive — Aggressive Mode, faster but exposes identities.</summary>
            [TikEnum("aggressive")] Aggressive,
            /// <summary>base — Base Mode (rarely used IKEv1 variant).</summary>
            [TikEnum("base")] Base,
            /// <summary>ike2 — IKEv2 (RFC 7296), the modern IKE version.</summary>
            [TikEnum("ike2")] Ike2,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — peer identifier; used to reference this entry from policies and scripts.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// address — IP/IPv6 prefix of the remote peer. When the remote peer's address matches
        /// this prefix the peer configuration is applied.
        /// Default: 0.0.0.0/0 (match any remote address).
        /// </summary>
        [TikProperty("address", DefaultValue = "0.0.0.0/0")]
        public string/*IP Prefix*/ Address { get; set; }

        /// <summary>
        /// local-address — router's local IP/IPv6 address to which IKE Phase 1 is bound.
        /// Leave empty to use the address selected by the routing table.
        /// </summary>
        [TikProperty("local-address")]
        public string/*IP*/ LocalAddress { get; set; }

        /// <summary>
        /// port — UDP port used by the initiator when connecting to the remote peer.
        /// Default on the router is 500 (standard IKE port); 0 here means "not explicitly set"
        /// so the mapper omits the field on add and the router applies its own default.
        /// </summary>
        [TikProperty("port")]
        public int Port { get; set; }

        /// <summary>
        /// profile — name of the IKE profile template (/ip/ipsec/profile) applied during
        /// Phase 1 negotiation.
        /// Default: "default"
        /// </summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>
        /// exchange-mode — IKEv1/IKEv2 Phase 1 exchange mode (RFC 2408).
        /// Default: main.
        /// <seealso cref="ExchangeModeType"/>
        /// </summary>
        [TikProperty("exchange-mode", DefaultValue = "main")]
        public ExchangeModeType ExchangeMode { get; set; }

        /// <summary>
        /// send-initial-contact — when true, the router sends an Initial-Contact IKE
        /// notification at the start of a new Phase 1 to tear down any stale SAs on the
        /// remote side. Disable when the remote peer does not handle this correctly.
        /// Default: yes
        /// </summary>
        [TikProperty("send-initial-contact", DefaultValue = "yes")]
        public bool SendInitialContact { get; set; }

        /// <summary>
        /// passive — when true the router acts only as a responder and waits for the remote
        /// peer to initiate IKE; it will not start Phase 1 on its own.
        /// Default: no
        /// </summary>
        [TikProperty("passive", DefaultValue = "no")]
        public bool Passive { get; set; }

        /// <summary>
        /// ppk-secret — Post-quantum Preshared Key secret (IKEv2 RFC 8784). Leave empty
        /// when PPK is not used.
        /// </summary>
        [TikProperty("ppk-secret")]
        public string PpkSecret { get; set; }

        /// <summary>
        /// disabled — when true this peer entry is not used to match remote peers.
        /// Default: no
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short description of the peer entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// dynamic — true when this entry was created automatically by another service
        /// (e.g. L2TP server); false for manually configured peers.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// responder — true when this peer is configured (or determined) to act as a
        /// responder only (listens for incoming IKE; never initiates).
        /// </summary>
        [TikProperty("responder", IsReadOnly = true)]
        public bool Responder { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
