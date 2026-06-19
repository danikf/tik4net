namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/active-peers
    ///
    /// Read-only status table of currently established IKE Phase 1 peers.
    /// Each row represents one active IPsec peer session, showing addressing,
    /// traffic counters, negotiation side, NAT-T status, and uptime.
    /// Use <c>kill-connections</c> to manually disconnect all remote peers.
    /// </summary>
    [TikEntity("/ip/ipsec/active-peers", IsReadOnly = true, IncludeDetails = true)]
    public class IpsecActivePeers
    {
        /// <summary>Possible sides for IKE Phase 1 negotiation.</summary>
        public enum SideType
        {
            /// <summary>initiator — this router initiated the Phase 1 exchange.</summary>
            [TikEnum("initiator")] Initiator,
            /// <summary>responder — the remote peer initiated Phase 1.</summary>
            [TikEnum("responder")] Responder,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// id — IKE remote identity of this peer (e.g. an FQDN, IP address, or distinguished
        /// name), as presented during Phase 1 negotiation. Distinct from the row key (.id).
        /// </summary>
        [TikProperty("id", IsReadOnly = true)]
        public string RemoteId { get; private set; }

        /// <summary>
        /// remote-address — the remote peer's IP or IPv6 address.
        /// </summary>
        [TikProperty("remote-address", IsReadOnly = true)]
        public string/*IP*/ RemoteAddress { get; private set; }

        /// <summary>
        /// local-address — local address on the router used by this peer session.
        /// </summary>
        [TikProperty("local-address", IsReadOnly = true)]
        public string/*IP*/ LocalAddress { get; private set; }

        /// <summary>
        /// dynamic-address — IP or IPv6 address dynamically assigned to the peer via Mode Config.
        /// Empty when Mode Config is not used.
        /// </summary>
        [TikProperty("dynamic-address", IsReadOnly = true)]
        public string/*IP*/ DynamicAddress { get; private set; }

        /// <summary>
        /// state — current Phase 1 negotiation status (e.g. "established", "connecting").
        /// </summary>
        [TikProperty("state", IsReadOnly = true)]
        public string State { get; private set; }

        /// <summary>
        /// side — shows which side initiated the Phase 1 negotiation.
        /// <seealso cref="SideType"/>
        /// </summary>
        [TikProperty("side", IsReadOnly = true)]
        public SideType Side { get; private set; }

        /// <summary>
        /// uptime — how long this peer has been in an established state.
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public string/*time*/ Uptime { get; private set; }

        /// <summary>
        /// last-seen — duration since the last message was received from this peer.
        /// </summary>
        [TikProperty("last-seen", IsReadOnly = true)]
        public string/*time*/ LastSeen { get; private set; }

        /// <summary>
        /// responder — true when the connection was initiated by the remote peer.
        /// </summary>
        [TikProperty("responder", IsReadOnly = true)]
        public bool Responder { get; private set; }

        /// <summary>
        /// natt-peer — true when NAT Traversal (NAT-T) is active for this peer connection.
        /// </summary>
        [TikProperty("natt-peer", IsReadOnly = true)]
        public bool NattPeer { get; private set; }

        /// <summary>
        /// ph2-total — total number of active IPsec Phase 2 security associations for this peer.
        /// </summary>
        [TikProperty("ph2-total", IsReadOnly = true)]
        public string Ph2Total { get; private set; }

        /// <summary>
        /// rx-bytes — total bytes received from this peer.
        /// </summary>
        [TikProperty("rx-bytes", IsReadOnly = true)]
        public string RxBytes { get; private set; }

        /// <summary>
        /// rx-packets — total packets received from this peer.
        /// </summary>
        [TikProperty("rx-packets", IsReadOnly = true)]
        public string RxPackets { get; private set; }

        /// <summary>
        /// tx-bytes — total bytes transmitted to this peer.
        /// </summary>
        [TikProperty("tx-bytes", IsReadOnly = true)]
        public string TxBytes { get; private set; }

        /// <summary>
        /// tx-packets — total packets transmitted to this peer.
        /// </summary>
        [TikProperty("tx-packets", IsReadOnly = true)]
        public string TxPackets { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("{0} ({1})", RemoteAddress, State);
    }
}
