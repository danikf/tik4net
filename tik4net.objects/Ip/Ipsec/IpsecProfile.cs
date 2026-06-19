namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/profile
    ///
    /// IKE Phase 1 profile. Defines the IKE negotiation parameters (encryption, hashing,
    /// Diffie-Hellman group, lifetime, NAT traversal, DPD, proposal-check) applied during
    /// Phase 1 SA establishment. Referenced by /ip/ipsec/peer entries via the "profile" field.
    /// </summary>
    [TikEntity("/ip/ipsec/profile", IncludeDetails = true)]
    public class IpsecProfile
    {
        /// <summary>Hashing algorithms for IKE Phase 1 authentication.</summary>
        public enum HashAlgorithmType
        {
            /// <summary>sha1 — SHA-1 (default). Listed first so a fresh entity equals the router default and is omitted on add.</summary>
            [TikEnum("sha1")] Sha1,
            /// <summary>md5 — MD5 (faster but weaker; not recommended for new deployments).</summary>
            [TikEnum("md5")] Md5,
            /// <summary>sha256 — SHA-256 (recommended for IKEv2).</summary>
            [TikEnum("sha256")] Sha256,
            /// <summary>sha512 — SHA-512 (strongest, highest CPU cost).</summary>
            [TikEnum("sha512")] Sha512,
        }

        /// <summary>Proposal-check modes controlling how Phase 2 lifetime proposals are validated.</summary>
        public enum ProposalCheckType
        {
            /// <summary>obey — accept the initiator's proposal unconditionally (default). Listed first so a fresh entity equals the router default and is omitted on add.</summary>
            [TikEnum("obey")] Obey,
            /// <summary>claim — use the smaller of the proposed and configured lifetimes.</summary>
            [TikEnum("claim")] Claim,
            /// <summary>exact — require an exact match; reject proposals with different lifetimes.</summary>
            [TikEnum("exact")] Exact,
            /// <summary>strict — reject proposals with lifetimes longer than the configured value.</summary>
            [TikEnum("strict")] Strict,
        }

        /// <summary>PRF (Pseudo-Random Function) algorithms for IKEv2 key derivation.</summary>
        public enum PrfAlgorithmType
        {
            /// <summary>auto — derive the PRF from the negotiated hash-algorithm (default). Listed first so a fresh entity equals the router default and is omitted on add.</summary>
            [TikEnum("auto")] Auto,
            /// <summary>sha1 — HMAC-SHA-1.</summary>
            [TikEnum("sha1")] Sha1,
            /// <summary>sha256 — HMAC-SHA-256.</summary>
            [TikEnum("sha256")] Sha256,
            /// <summary>sha384 — HMAC-SHA-384.</summary>
            [TikEnum("sha384")] Sha384,
            /// <summary>sha512 — HMAC-SHA-512.</summary>
            [TikEnum("sha512")] Sha512,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — profile identifier; referenced by /ip/ipsec/peer entries.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// hash-algorithm — hashing algorithm used for IKE Phase 1 authentication.
        /// SHA is stronger but slower. Default: sha1.
        /// <seealso cref="HashAlgorithmType"/>
        /// </summary>
        [TikProperty("hash-algorithm", DefaultValue = "sha1")]
        public HashAlgorithmType HashAlgorithm { get; set; }

        /// <summary>
        /// enc-algorithm — comma-separated list of encryption algorithms offered during
        /// Phase 1 negotiation (multi-value, kept as string).
        /// Allowed values: 3des, aes-128, aes-192, aes-256, blowfish, camellia-128,
        /// camellia-192, camellia-256, des.
        /// Default: "aes-128,3des"
        /// </summary>
        [TikProperty("enc-algorithm", DefaultValue = "aes-128,3des")]
        public string EncAlgorithm { get; set; }

        /// <summary>
        /// dh-group — comma-separated list of Diffie-Hellman groups offered during Phase 1
        /// (multi-value, kept as string).
        /// Allowed values: modp768, modp1024, modp1536, modp2048, modp3072, modp4096,
        /// modp6144, modp8192, ecp256, ecp384, ecp521.
        /// Default: "modp1024,modp2048"
        /// </summary>
        [TikProperty("dh-group", DefaultValue = "modp1024,modp2048")]
        public string DhGroup { get; set; }

        /// <summary>
        /// lifetime — how long the Phase 1 SA is considered valid before re-keying.
        /// Accepts RouterOS time format (e.g. "1d", "8h", "30m"). Default: "1d"
        /// </summary>
        [TikProperty("lifetime", DefaultValue = "1d")]
        public string/*time*/ Lifetime { get; set; }

        /// <summary>
        /// lifebytes — maximum number of bytes transferred before the Phase 1 SA is re-keyed.
        /// 0 means disabled (not set). Valid range: 0–4294967295.
        /// When 0 the mapper omits the field on add and the router uses its own default.
        /// </summary>
        [TikProperty("lifebytes")]
        public long Lifebytes { get; set; }

        /// <summary>
        /// nat-traversal — enable Linux NAT-T (RFC 3947) to allow IPsec through NAT devices.
        /// Default: yes
        /// </summary>
        [TikProperty("nat-traversal", DefaultValue = "yes")]
        public bool NatTraversal { get; set; }

        /// <summary>
        /// dpd-interval — Dead Peer Detection (DPD) probe interval.
        /// Accepts a RouterOS time value (e.g. "8s") or the special string "disable-dpd"
        /// to turn DPD off entirely. Kept as string because of the special token.
        /// Default: "8s"
        /// When not set the mapper omits the field on add and the router uses its own default.
        /// </summary>
        [TikProperty("dpd-interval")]
        public string/*time|disable-dpd*/ DpdInterval { get; set; }

        /// <summary>
        /// dpd-maximum-failures — number of consecutive DPD probe failures before the peer
        /// is declared dead and the SA is torn down. Valid range: 1–100.
        /// When 0 the mapper omits the field on add and the router uses its own default (4).
        /// </summary>
        [TikProperty("dpd-maximum-failures")]
        public int DpdMaximumFailures { get; set; }

        /// <summary>
        /// proposal-check — how the router validates the Phase 2 lifetime proposed by the
        /// initiator. Default: obey.
        /// <seealso cref="ProposalCheckType"/>
        /// </summary>
        [TikProperty("proposal-check", DefaultValue = "obey")]
        public ProposalCheckType ProposalCheck { get; set; }

        /// <summary>
        /// prf-algorithm — Pseudo-Random Function algorithm for IKEv2 key derivation.
        /// "auto" (default) derives the PRF from the negotiated hash-algorithm.
        /// Valid values: auto, sha1, sha256, sha384, sha512.
        /// <seealso cref="PrfAlgorithmType"/>
        /// </summary>
        [TikProperty("prf-algorithm", DefaultValue = "auto")]
        public PrfAlgorithmType PrfAlgorithm { get; set; }

        /// <summary>
        /// ppk — enable Post-quantum Preshared Key (PPK) support (IKEv2, RFC 8784).
        /// Default: no
        /// </summary>
        [TikProperty("ppk", DefaultValue = "no")]
        public bool Ppk { get; set; }

        // NOTE: /ip/ipsec/profile has no "comment" field on RouterOS (confirmed via
        // add-completion), so no Comment property is exposed here.

        // --- Read-only properties ---

        /// <summary>
        /// default — true when this is a built-in (system) profile that cannot be deleted.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
