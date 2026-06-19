namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/proposal
    ///
    /// IPsec proposal defines the set of cryptographic algorithms and parameters offered (or accepted)
    /// during IKE Phase 2 (Quick Mode / CREATE_CHILD_SA) negotiation. Each proposal specifies which
    /// authentication and encryption algorithms are allowed for the resulting IPsec SAs, the SA lifetime,
    /// and the Diffie-Hellman group used for Perfect Forward Secrecy (PFS).
    /// </summary>
    [TikEntity("/ip/ipsec/proposal", IncludeDetails = true)]
    public class IpsecProposal
    {
        /// <summary>Allowed PFS Diffie-Hellman groups.</summary>
        public enum PfsGroupType
        {
            /// <summary>none — PFS is disabled; no new DH exchange for Phase 2.</summary>
            [TikEnum("none")] None,
            /// <summary>modp768 — 768-bit MODP group (Group 1).</summary>
            [TikEnum("modp768")] Modp768,
            /// <summary>modp1024 — 1024-bit MODP group (Group 2). Router default.</summary>
            [TikEnum("modp1024")] Modp1024,
            /// <summary>modp1536 — 1536-bit MODP group (Group 5).</summary>
            [TikEnum("modp1536")] Modp1536,
            /// <summary>modp2048 — 2048-bit MODP group (Group 14).</summary>
            [TikEnum("modp2048")] Modp2048,
            /// <summary>modp3072 — 3072-bit MODP group (Group 15).</summary>
            [TikEnum("modp3072")] Modp3072,
            /// <summary>modp4096 — 4096-bit MODP group (Group 16).</summary>
            [TikEnum("modp4096")] Modp4096,
            /// <summary>modp6144 — 6144-bit MODP group (Group 17).</summary>
            [TikEnum("modp6144")] Modp6144,
            /// <summary>modp8192 — 8192-bit MODP group (Group 18).</summary>
            [TikEnum("modp8192")] Modp8192,
            /// <summary>ecp256 — 256-bit Elliptic Curve group (RFC 5903).</summary>
            [TikEnum("ecp256")] Ecp256,
            /// <summary>ecp384 — 384-bit Elliptic Curve group (RFC 5903).</summary>
            [TikEnum("ecp384")] Ecp384,
            /// <summary>ecp521 — 521-bit Elliptic Curve group (RFC 5903).</summary>
            [TikEnum("ecp521")] Ecp521,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — proposal identifier; used to reference this entry from policies.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// auth-algorithms — comma-separated list of allowed authentication (integrity) algorithms
        /// for Phase 2 SAs. SHA (Secure Hash Algorithm) is stronger but slower than MD5.
        /// Valid values: md5, sha1, sha256, sha512, null (no authentication, e.g. for GCM).
        /// Default: sha1
        /// </summary>
        [TikProperty("auth-algorithms", DefaultValue = "sha1")]
        public string AuthAlgorithms { get; set; }

        /// <summary>
        /// enc-algorithms — comma-separated list of allowed encryption algorithms and key lengths
        /// for Phase 2 SAs. Multiple values are tried in order during negotiation.
        /// Valid values: null, des, 3des, aes-128-cbc, aes-192-cbc, aes-256-cbc,
        ///   aes-128-ctr, aes-192-ctr, aes-256-ctr, aes-128-gcm, aes-192-gcm, aes-256-gcm,
        ///   blowfish, camellia-128, camellia-192, camellia-256, twofish.
        /// Default: aes-256-cbc,aes-192-cbc,aes-128-cbc
        /// </summary>
        [TikProperty("enc-algorithms", DefaultValue = "aes-256-cbc,aes-192-cbc,aes-128-cbc")]
        public string EncAlgorithms { get; set; }

        /// <summary>
        /// lifetime — how long (time string, e.g. "30m") to use the SA before it must be
        /// renegotiated and replaced.
        /// Default: 30m
        /// </summary>
        [TikProperty("lifetime", DefaultValue = "30m")]
        public string/*time*/ Lifetime { get; set; }

        /// <summary>
        /// pfs-group — Diffie-Hellman group used for Perfect Forward Secrecy in Phase 2.
        /// Set to none to disable PFS.
        /// Default: modp1024.
        /// <seealso cref="PfsGroupType"/>
        /// </summary>
        [TikProperty("pfs-group", DefaultValue = "modp1024")]
        public PfsGroupType PfsGroup { get; set; }

        /// <summary>
        /// disabled — when true this proposal entry is not offered during IKE negotiation.
        /// Default: no
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short description of the proposal entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// default — true when this is the built-in default proposal entry created by RouterOS.
        /// Default entries cannot be deleted.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
