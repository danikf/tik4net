namespace tik4net.Objects.Routing.Bgp
{

    /// <summary>
    /// Access to the data provided by
    /// /routing/bgp/peer
    /// </summary>
    [TikEntity("/routing/bgp/peer")]
    public class BgpPeer
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Gets or sets the name of the peer.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the BGP instance that this peer belongs to.
        /// </summary>
        [TikProperty("instance")]
        public string Instance { get; set; }

        /// <summary>
        /// Gets or sets the remote IP address of the peer.
        /// </summary>
        [TikProperty("remote-address")]
        public string RemoteAddress { get; set; }

        /// <summary>
        /// Gets or sets the the remote peer's autonomuous system number.
        /// </summary>
        [TikProperty("remote-as")]
        public long RemoteAs { get; set; }

        /// <summary>
        /// Gets or sets the next-hop choice (default, force-self, propagate).
        /// </summary>
        [TikProperty("nexthop-choice")]
        public string NexthopChoice { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is a multi-hop peer.
        /// </summary>
        [TikProperty("multihop")]
        public bool Multihop { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to reflect the route.
        /// </summary>
        [TikProperty("route-reflect")]
        public bool RouteReflect { get; set; }

        /// <summary>
        /// Gets or sets the hold-time of this peer.
        /// </summary>
        [TikProperty("hold-time")]
        public string HoldTime { get; set; }

        /// <summary>
        /// Gets or sets the time-to-live setting of this peer.
        /// </summary>
        [TikProperty("ttl")]
        public string Ttl { get; set; }

        /// <summary>
        /// Gets or sets a comma-separated list of address families (ip, ipv6, l2vpn, vpn4, l2vpn-cisco) that are routed to/by this peer.
        /// </summary>
        [TikProperty("address-families")]
        public string AddressFamilies { get; set; }

        /// <summary>
        /// Gets or sets the value whether default originate (never, if-installed, always).
        /// </summary>
        [TikProperty("default-originate")]
        public string DefaultOriginate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove autonomuous system having private AS numbers.
        /// </summary>
        [TikProperty("remove-private-as")]
        public bool RemovePrivateAs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to override the autonomuous system numbers.
        /// </summary>
        [TikProperty("as-override")]
        public bool AsOverride { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to this peer as passive.
        /// </summary>
        [TikProperty("passive")]
        public bool Passive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use Bidirectional Forwarding Detection with this peer.
        /// </summary>
        [TikProperty("use-bfd")]
        public bool UseBfd { get; set; }

        /// <summary>
        /// Gets or sets the peer's remote ID (usually some IP address).
        /// </summary>
        [TikProperty("remote-id")]
        public string RemoteId { get; set; }

        /// <summary>
        /// Gets or sets the local IP address that is used to communicate to this peer.
        /// </summary>
        [TikProperty("local-address", IsReadOnly = true)]
        public string LocalAddress { get; private set; }

        /// <summary>
        /// Gets the uptime of the link to this peer.
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public string Uptime { get; private set; }

        /// <summary>
        /// Gets the number of prefixes advertised by this peer.
        /// </summary>
        [TikProperty("prefix-count", IsReadOnly = true)]
        public long PrefixCount { get; private set; }

        /// <summary>
        /// Gets the number of updates that have been sent to this peer. 
        /// </summary>
        [TikProperty("updates-sent", IsReadOnly = true)]
        public long UpdatesSent { get; private set; }

        /// <summary>
        /// Gets the number of updates that have been received from this peer. 
        /// </summary>
        [TikProperty("updates-received", IsReadOnly = true)]
        public long UpdatesReceived { get; private set; }

        /// <summary>
        /// Gets the number of withdrawals that have been sent to this peer. 
        /// </summary>
        [TikProperty("withdrawn-sent", IsReadOnly = true)]
        public long WithdrawnSent { get; private set; }

        /// <summary>
        /// Gets the number of withdrawals that have been received form this peer. 
        /// </summary>
        [TikProperty("withdrawn-received", IsReadOnly = true)]
        public long WithdrawnReceived { get; private set; }

        /// <summary>
        /// remote-hold-time: 
        /// </summary>
        [TikProperty("remote-hold-time", IsReadOnly = true)]
        public string RemoteHoldTime { get; private set; }

        /// <summary>
        /// Gets the actually used hold-time of the link to this peer.
        /// </summary>
        [TikProperty("used-hold-time", IsReadOnly = true)]
        public string UsedHoldTime { get; private set; }

        /// <summary>
        /// Gets the actually used keepalive-time of the link to this peer.
        /// </summary>
        [TikProperty("used-keepalive-time", IsReadOnly = true)]
        public string UsedKeepaliveTime { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this peer has the refresh capability.
        /// </summary>
        [TikProperty("refresh-capability", IsReadOnly = true)]
        public bool RefreshCapability { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this peer has the AS4 capability.
        /// </summary>
        [TikProperty("as4-capability", IsReadOnly = true)]
        public bool As4Capability { get; private set; }

        /// <summary>
        /// Gets the state of the link to this peer.
        /// </summary>
        [TikProperty("state", IsReadOnly = true)]
        public string State { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the link to this peer is currently established.
        /// </summary>
        [TikProperty("established", IsReadOnly = true)]
        public bool Established { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this peer is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
