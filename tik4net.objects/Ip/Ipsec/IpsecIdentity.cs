namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/identity
    ///
    /// IPsec identity configuration. Each identity entry defines how a remote peer is authenticated
    /// and which policy rules apply when the peer's traffic is matched. The identity is linked to an
    /// IKE peer entry and carries the authentication credentials (pre-shared key, certificate, EAP
    /// credentials) as well as optional policy generation settings and mode-config parameters.
    /// </summary>
    [TikEntity("/ip/ipsec/identity", IncludeDetails = true)]
    public class IpsecIdentity
    {
        /// <summary>Authentication methods for IPsec peer identity verification.</summary>
        public enum AuthMethodType
        {
            /// <summary>pre-shared-key — authenticate using a shared secret string.</summary>
            [TikEnum("pre-shared-key")] PreSharedKey,
            /// <summary>pre-shared-key-xauth — pre-shared key with XAuth user/password exchange (IKEv1 only).</summary>
            [TikEnum("pre-shared-key-xauth")] PreSharedKeyXauth,
            /// <summary>rsa-signature — authenticate using RSA certificates (legacy name).</summary>
            [TikEnum("rsa-signature")] RsaSignature,
            /// <summary>rsa-signature-hybrid — server authenticates with a certificate, client with XAuth.</summary>
            [TikEnum("rsa-signature-hybrid")] RsaSignatureHybrid,
            /// <summary>rsa-key — authenticate using a raw RSA public/private key pair.</summary>
            [TikEnum("rsa-key")] RsaKey,
            /// <summary>digital-signature — authenticate using a digital certificate (IKEv2).</summary>
            [TikEnum("digital-signature")] DigitalSignature,
            /// <summary>eap — Extensible Authentication Protocol (IKEv2 only).</summary>
            [TikEnum("eap")] Eap,
            /// <summary>eap-radius — EAP backed by a RADIUS server (IKEv2 only).</summary>
            [TikEnum("eap-radius")] EapRadius,
        }

        /// <summary>Controls whether dynamic security policies (SAs) are generated for unmatched traffic.</summary>
        public enum GeneratePolicyType
        {
            /// <summary>no — do not generate policies automatically; only use explicitly configured ones.</summary>
            [TikEnum("no")] No,
            /// <summary>port-override — generate policies and override port selectors with 0 (any port).</summary>
            [TikEnum("port-override")] PortOverride,
            /// <summary>port-strict — generate policies and keep the original port selectors from the IKE proposal.</summary>
            [TikEnum("port-strict")] PortStrict,
        }

        /// <summary>Logic used to match an incoming IKE identity against this entry.</summary>
        public enum MatchByType
        {
            /// <summary>remote-id — match by the remote ID value sent in the IKE exchange.</summary>
            [TikEnum("remote-id")] RemoteId,
            /// <summary>certificate — match by the subject of the peer's certificate.</summary>
            [TikEnum("certificate")] Certificate,
        }

        /// <summary>The local identity type sent to the remote peer during IKE negotiation.</summary>
        public enum MyIdType
        {
            /// <summary>auto — RouterOS selects the local identity automatically.</summary>
            [TikEnum("auto")] Auto,
            /// <summary>address — send the local IP address as the identity.</summary>
            [TikEnum("address")] Address,
            /// <summary>fqdn — send a Fully Qualified Domain Name as the identity.</summary>
            [TikEnum("fqdn")] Fqdn,
            /// <summary>user-fqdn — send a user@domain string as the identity.</summary>
            [TikEnum("user-fqdn")] UserFqdn,
            /// <summary>key-id — send an opaque key identifier as the identity.</summary>
            [TikEnum("key-id")] KeyId,
        }

        /// <summary>The expected identity type received from the remote peer during IKE negotiation.</summary>
        public enum RemoteIdType
        {
            /// <summary>auto — RouterOS matches the remote ID automatically.</summary>
            [TikEnum("auto")] Auto,
            /// <summary>address — expect an IP address as the remote identity.</summary>
            [TikEnum("address")] Address,
            /// <summary>dn — expect a Distinguished Name (X.509 subject) as the remote identity.</summary>
            [TikEnum("dn")] Dn,
            /// <summary>fqdn — expect a Fully Qualified Domain Name as the remote identity.</summary>
            [TikEnum("fqdn")] Fqdn,
            /// <summary>user-fqdn — expect a user@domain string as the remote identity.</summary>
            [TikEnum("user-fqdn")] UserFqdn,
            /// <summary>key-id — expect an opaque key identifier as the remote identity.</summary>
            [TikEnum("key-id")] KeyId,
            /// <summary>ignore — do not validate the remote identity.</summary>
            [TikEnum("ignore")] Ignore,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// peer — name of the IKE peer entry (/ip/ipsec/peer) this identity is linked to.
        /// The router requires a peer reference on add; it is treated as a mandatory field.
        /// </summary>
        [TikProperty("peer", IsMandatory = true)]
        public string Peer { get; set; }

        /// <summary>
        /// auth-method — authentication method used to verify the remote peer's identity.
        /// Default: pre-shared-key.
        /// <seealso cref="AuthMethodType"/>
        /// </summary>
        [TikProperty("auth-method", DefaultValue = "pre-shared-key")]
        public AuthMethodType AuthMethod { get; set; }

        /// <summary>
        /// secret — pre-shared key string used when auth-method is pre-shared-key or
        /// pre-shared-key-xauth. Leave empty for certificate-based methods.
        /// </summary>
        [TikProperty("secret")]
        public string Secret { get; set; }

        /// <summary>
        /// generate-policy — controls whether RouterOS automatically creates IPsec policies
        /// (SAs) for traffic that matches this identity but has no explicit policy entry.
        /// Default: no.
        /// <seealso cref="GeneratePolicyType"/>
        /// </summary>
        [TikProperty("generate-policy", DefaultValue = "no")]
        public GeneratePolicyType GeneratePolicy { get; set; }

        /// <summary>
        /// match-by — logic used to match incoming IKE identities against this entry.
        /// Default: remote-id.
        /// <seealso cref="MatchByType"/>
        /// </summary>
        [TikProperty("match-by", DefaultValue = "remote-id")]
        public MatchByType MatchBy { get; set; }

        /// <summary>
        /// mode-config — name of the mode-config entry (/ip/ipsec/mode-config) to use for
        /// IP address assignment during IKEv1 Mode-Config or IKEv2 CP payload exchange.
        /// Leave empty to disable mode-config for this identity.
        /// </summary>
        [TikProperty("mode-config")]
        public string ModeConfig { get; set; }

        /// <summary>
        /// policy-template-group — name of the policy template group used to validate traffic
        /// selectors when generate-policy is active.
        /// Default: "default"
        /// </summary>
        [TikProperty("policy-template-group", DefaultValue = "default")]
        public string PolicyTemplateGroup { get; set; }

        /// <summary>
        /// my-id — type and value of the local identity sent to the remote peer in IKE.
        /// Default: auto.
        /// <seealso cref="MyIdType"/>
        /// </summary>
        [TikProperty("my-id", DefaultValue = "auto")]
        public MyIdType MyId { get; set; }

        /// <summary>
        /// remote-id — expected identity type and value received from the remote peer.
        /// Default: auto.
        /// <seealso cref="RemoteIdType"/>
        /// </summary>
        [TikProperty("remote-id", DefaultValue = "auto")]
        public RemoteIdType RemoteId { get; set; }

        /// <summary>
        /// certificate — name of a local certificate (from /certificate) used to authenticate
        /// this router to the remote peer when auth-method is rsa-signature, rsa-signature-hybrid,
        /// or digital-signature.
        /// </summary>
        [TikProperty("certificate")]
        public string Certificate { get; set; }

        /// <summary>
        /// remote-certificate — name of the certificate (from /certificate) used to authenticate
        /// the remote peer. When specified, the remote peer's certificate must match this entry.
        /// </summary>
        [TikProperty("remote-certificate")]
        public string RemoteCertificate { get; set; }

        /// <summary>
        /// key — name of a local RSA private key (from /ip/ipsec/key) used when auth-method
        /// is rsa-key.
        /// </summary>
        [TikProperty("key")]
        public string Key { get; set; }

        /// <summary>
        /// remote-key — name of the remote peer's RSA public key (from /ip/ipsec/key) used
        /// to verify the remote peer when auth-method is rsa-key.
        /// </summary>
        [TikProperty("remote-key")]
        public string RemoteKey { get; set; }

        /// <summary>
        /// eap-methods — comma-separated list of EAP methods accepted/offered when auth-method
        /// is eap or eap-radius (IKEv2 only).
        /// Default: eap-tls
        /// </summary>
        [TikProperty("eap-methods")]
        public string EapMethods { get; set; }

        /// <summary>
        /// username — XAuth or EAP account name sent to the remote peer when auth-method is
        /// pre-shared-key-xauth, rsa-signature-hybrid, eap, or eap-radius.
        /// </summary>
        [TikProperty("username")]
        public string Username { get; set; }

        /// <summary>
        /// password — XAuth or EAP credential sent to the remote peer when auth-method is
        /// pre-shared-key-xauth, rsa-signature-hybrid, eap, or eap-radius.
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// notrack-chain — when set, RouterOS adds /ip/firewall/raw rules to the named chain
        /// to bypass connection tracking for IPsec traffic matched by this identity.
        /// Leave empty to disable.
        /// </summary>
        [TikProperty("notrack-chain")]
        public string NotrackChain { get; set; }

        /// <summary>
        /// disabled — when true this identity entry is not used to match remote peers.
        /// Default: no
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short description of the identity entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// dynamic — true when this entry was created automatically by another service
        /// (e.g. L2TP server); false for manually configured identities.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("peer={0} auth={1}", Peer, AuthMethod);
    }
}
