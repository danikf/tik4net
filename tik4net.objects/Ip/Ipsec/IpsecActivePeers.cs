namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>
    /// /ip/ipsec/active-peers
    ///
    /// Read-only status table of currently established IKE Phase 1 peers.
    /// Each row represents one active IPsec peer session, showing addressing,
    /// traffic counters, negotiation side, NAT-T status, and uptime.
    /// Use <c>kill-connections</c> to manually disconnect all remote peers.
    /// <para>
    /// Declared <see cref="TikEntityOperations.Remove"/> only, although the menu also lists <c>add</c> and
    /// <c>set</c>. Those two are RouterOS's generic list machinery showing through and do not work: on 7.24
    /// an <c>add</c> carrying the only parameter the menu accepts (<c>comment</c>) answers
    /// <i>"error - contact MikroTik support and send a supout file (3)"</i> and creates nothing, and
    /// <c>set</c> answers the same rather than <i>no such item</i>. <c>remove</c> behaves normally.
    /// </para>
    /// </summary>
    [TikEntity("/ip/ipsec/active-peers", SupportedOperations = TikEntityOperations.Remove, IncludeDetails = true)]
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
        public string? Id { get; private set; }

        /// <summary>
        /// id — IKE remote identity of this peer (e.g. an FQDN, IP address, or distinguished
        /// name), as presented during Phase 1 negotiation. Distinct from the row key (.id).
        /// </summary>
        [TikProperty("id", IsReadOnly = true, WinboxLabel = "ID")]
        public TikField<string?> RemoteId { get; private set; }

        /// <summary>
        /// remote-address — the remote peer's IP or IPv6 address.
        /// </summary>
        [TikProperty("remote-address", IsReadOnly = true, WinboxLabel = "Remote Address")]
        public TikField<string?> RemoteAddress { get; private set; }

        /// <summary>
        /// local-address — local address on the router used by this peer session.
        /// </summary>
        [TikProperty("local-address", IsReadOnly = true, WinboxLabel = "Local Address")]
        public TikField<string?> LocalAddress { get; private set; }

        /// <summary>
        /// dynamic-address — IP or IPv6 address dynamically assigned to the peer via Mode Config.
        /// Empty when Mode Config is not used.
        /// </summary>
        [TikProperty("dynamic-address", IsReadOnly = true, WinboxLabel = "Dynamic Address")]
        public TikField<string?> DynamicAddress { get; private set; }

        /// <summary>
        /// state — current Phase 1 negotiation status (e.g. "established", "connecting").
        /// </summary>
        [TikProperty("state", IsReadOnly = true, WinboxLabel = "State")]
        public TikField<string?> State { get; private set; }

        /// <summary>
        /// side — shows which side initiated the Phase 1 negotiation.
        /// <seealso cref="SideType"/>
        /// </summary>
        [TikProperty("side", IsReadOnly = true, WinboxLabel = "Side")]
        public TikField<SideType?> Side { get; private set; }

        /// <summary>
        /// uptime — how long this peer has been in an established state.
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true, WinboxLabel = "Uptime")]
        public TikField<string?> Uptime { get; private set; }

        /// <summary>
        /// last-seen — duration since the last message was received from this peer.
        /// </summary>
        [TikProperty("last-seen", IsReadOnly = true, WinboxLabel = "Last Seen")]
        public TikField<string?> LastSeen { get; private set; }

        /// <summary>
        /// responder — true when the connection was initiated by the remote peer.
        /// </summary>
        [TikProperty("responder", IsReadOnly = true)]
        public TikField<bool?> Responder { get; private set; }

        /// <summary>
        /// natt-peer — true when NAT Traversal (NAT-T) is active for this peer connection.
        /// </summary>
        [TikProperty("natt-peer", IsReadOnly = true, WinboxLabel = "NATT Peer")]
        public TikField<bool?> NattPeer { get; private set; }

        /// <summary>
        /// ph2-total — total number of active IPsec Phase 2 security associations for this peer.
        /// </summary>
        [TikProperty("ph2-total", IsReadOnly = true, WinboxLabel = "PH2 Total")]
        public TikField<string?> Ph2Total { get; private set; }

        /// <summary>
        /// rx-bytes — total bytes received from this peer.
        /// </summary>
        [TikProperty("rx-bytes", IsReadOnly = true, WinboxLabel = "Rx Bytes")]
        public TikField<string?> RxBytes { get; private set; }

        /// <summary>
        /// rx-packets — total packets received from this peer.
        /// </summary>
        [TikProperty("rx-packets", IsReadOnly = true, WinboxLabel = "Rx Packets")]
        public TikField<string?> RxPackets { get; private set; }

        /// <summary>
        /// tx-bytes — total bytes transmitted to this peer.
        /// </summary>
        [TikProperty("tx-bytes", IsReadOnly = true, WinboxLabel = "Tx Bytes")]
        public TikField<string?> TxBytes { get; private set; }

        /// <summary>
        /// tx-packets — total packets transmitted to this peer.
        /// </summary>
        [TikProperty("tx-packets", IsReadOnly = true, WinboxLabel = "Tx Packets")]
        public TikField<string?> TxPackets { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("{0} ({1})", RemoteAddress, State);
    }
}
