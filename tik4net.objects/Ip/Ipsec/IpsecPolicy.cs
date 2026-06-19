namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/policy
    ///
    /// IPsec policy entry. Policies define how IP traffic is matched and what IPsec action
    /// (encrypt, discard, or bypass) is applied to matched packets. Policies are evaluated
    /// in order — use <see cref="IsOrdered"/> move semantics to control priority.
    ///
    /// A policy marked as a template (<see cref="Template"/>) is assigned to a policy group
    /// (<see cref="Group"/>) and instantiated dynamically by IKE when a peer negotiates SAs;
    /// non-template policies are static entries matched directly against traffic.
    /// </summary>
    [TikEntity("/ip/ipsec/policy", IncludeDetails = true, IsOrdered = true)]
    public class IpsecPolicy
    {
        // ── Enums ────────────────────────────────────────────────────────────────

        /// <summary>Possible actions for packets matched by a policy.</summary>
        public enum ActionType
        {
            /// <summary>none — pass the packet without IPsec processing (bypass).</summary>
            [TikEnum("none")] None,
            /// <summary>discard — drop the packet silently.</summary>
            [TikEnum("discard")] Discard,
            /// <summary>encrypt — apply IPsec SA(s) to the packet.</summary>
            [TikEnum("encrypt")] Encrypt,
        }

        /// <summary>Specifies what to do when an SA required by the policy cannot be found.</summary>
        public enum LevelType
        {
            /// <summary>require — drop the packet if no matching SA exists; trigger IKE negotiation.</summary>
            [TikEnum("require")] Require,
            /// <summary>use — send the packet in plaintext if no matching SA exists (best-effort).</summary>
            [TikEnum("use")] Use,
            /// <summary>unique — require a unique SA per policy (forces a new SA for each connection).</summary>
            [TikEnum("unique")] Unique,
        }

        /// <summary>IPsec protocol(s) to apply when encrypting matched packets.</summary>
        public enum IpsecProtocolsType
        {
            /// <summary>esp — Encapsulating Security Payload (confidentiality + integrity).</summary>
            [TikEnum("esp")] Esp,
            /// <summary>ah — Authentication Header (integrity only, no encryption).</summary>
            [TikEnum("ah")] Ah,
        }

        // ── Primary key ──────────────────────────────────────────────────────────

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Writable properties ──────────────────────────────────────────────────

        /// <summary>
        /// src-address — source IP/IPv6 prefix matched against the packet source address.
        /// Supports CIDR notation; use ::/0 or 0.0.0.0/0 for any.
        /// Default: 0.0.0.0/32
        /// </summary>
        [TikProperty("src-address", DefaultValue = "0.0.0.0/32")]
        public string/*IP Prefix*/ SrcAddress { get; set; }

        /// <summary>
        /// src-port — source port (0–65535) or "any" to match all ports.
        /// Only relevant when <see cref="Protocol"/> is TCP or UDP.
        /// Default: any
        /// </summary>
        [TikProperty("src-port", DefaultValue = "any")]
        public string SrcPort { get; set; }

        /// <summary>
        /// dst-address — destination IP/IPv6 prefix matched against the packet destination address.
        /// Supports CIDR notation; use ::/0 or 0.0.0.0/0 for any.
        /// Default: 0.0.0.0/32
        /// </summary>
        [TikProperty("dst-address", DefaultValue = "0.0.0.0/32")]
        public string/*IP Prefix*/ DstAddress { get; set; }

        /// <summary>
        /// dst-port — destination port (0–65535) or "any" to match all ports.
        /// Only relevant when <see cref="Protocol"/> is TCP or UDP.
        /// Default: any
        /// </summary>
        [TikProperty("dst-port", DefaultValue = "any")]
        public string DstPort { get; set; }

        /// <summary>
        /// protocol — IP protocol number or well-known name to match.
        /// Common values: all, tcp, udp, icmp, gre, esp, ah.
        /// Default: all
        /// </summary>
        [TikProperty("protocol", DefaultValue = "all")]
        public string Protocol { get; set; }

        /// <summary>
        /// action — what to do with the packet matched by this policy.
        /// Default: encrypt
        /// <seealso cref="ActionType"/>
        /// </summary>
        [TikProperty("action", DefaultValue = "encrypt")]
        public ActionType Action { get; set; }

        /// <summary>
        /// level — what to do when the required SA(s) cannot be found or established.
        /// Default: require
        /// <seealso cref="LevelType"/>
        /// </summary>
        [TikProperty("level", DefaultValue = "require")]
        public LevelType Level { get; set; }

        /// <summary>
        /// ipsec-protocols — IPsec protocol(s) to apply to matched traffic.
        /// Default: esp
        /// <seealso cref="IpsecProtocolsType"/>
        /// </summary>
        [TikProperty("ipsec-protocols", DefaultValue = "esp")]
        public IpsecProtocolsType IpsecProtocols { get; set; }

        /// <summary>
        /// tunnel — when true, use tunnel mode (encapsulate the whole IP packet);
        /// when false, use transport mode (protect only the payload).
        /// Default: no
        /// </summary>
        [TikProperty("tunnel", DefaultValue = "no")]
        public bool Tunnel { get; set; }

        /// <summary>
        /// peer — name of the /ip/ipsec/peer entry this policy applies to.
        /// Leave empty for template policies matched by group.
        /// </summary>
        [TikProperty("peer")]
        public string Peer { get; set; }

        /// <summary>
        /// proposal — name of the /ip/ipsec/proposal template used when negotiating SAs.
        /// Default: default
        /// </summary>
        [TikProperty("proposal", DefaultValue = "default")]
        public string Proposal { get; set; }

        /// <summary>
        /// template — when true, this entry is a template policy assigned to a policy group;
        /// it is instantiated dynamically by IKE and not matched against traffic directly.
        /// Default: no
        /// </summary>
        [TikProperty("template", DefaultValue = "no")]
        public bool Template { get; set; }

        /// <summary>
        /// group — name of the policy group this template belongs to.
        /// Relevant only when <see cref="Template"/> is true.
        /// Default: default
        /// </summary>
        [TikProperty("group", DefaultValue = "default")]
        public string Group { get; set; }

        /// <summary>
        /// sa-src-address — local IP address used as the IPsec SA source (tunnel local endpoint).
        /// Leave empty to use the address selected by routing. Writable at add/set time.
        /// </summary>
        [TikProperty("sa-src-address")]
        public string/*IP*/ SaSrcAddress { get; set; }

        /// <summary>
        /// sa-dst-address — remote IP address used as the IPsec SA destination (tunnel remote endpoint).
        /// Writable at add/set time; specifies the peer tunnel address for manual SAs.
        /// </summary>
        [TikProperty("sa-dst-address")]
        public string/*IP*/ SaDstAddress { get; set; }

        /// <summary>
        /// disabled — when true, this policy entry is ignored during packet matching.
        /// Default: no
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short description of the policy entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ── Read-only properties ─────────────────────────────────────────────────

        /// <summary>
        /// active — true when this policy is currently in use (has at least one active SA).
        /// </summary>
        [TikProperty("active", IsReadOnly = true)]
        public bool Active { get; private set; }

        /// <summary>
        /// dynamic — true when this policy entry was created dynamically by IKE
        /// (from a template); false for manually configured entries.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// invalid — true when the policy is invalid (e.g. overlaps with another policy).
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// default — true when this is a built-in system default policy entry.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>
        /// ph2-state — indicates progress of Phase 2 (IPsec SA) key establishment for this policy.
        /// </summary>
        [TikProperty("ph2-state", IsReadOnly = true)]
        public string Ph2State { get; private set; }

        /// <summary>
        /// ph2-count — number of active Phase 2 SA sessions associated with this policy.
        /// </summary>
        [TikProperty("ph2-count", IsReadOnly = true)]
        public string Ph2Count { get; private set; }

        // ── Human-readable identity ──────────────────────────────────────────────

        /// <summary>Returns a human-readable summary of the policy.</summary>
        public override string ToString()
            => string.Format("{0} → {1} [{2}]", SrcAddress, DstAddress, Action);
    }
}
