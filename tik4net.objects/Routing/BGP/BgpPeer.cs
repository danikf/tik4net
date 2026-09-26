using System;

namespace tik4net.Objects.Routing.Bgp
{

    /// <summary>
    /// Access to the data provided by /routing/bgp/peer (RouterOS 6).
    /// Replaced by <see cref="BgpConnection"/> in RouterOS 7 (/routing/bgp/connection).
    /// </summary>
    [TikEntity("/routing/bgp/peer")]
    [Obsolete("RouterOS 7 removed /routing/bgp/peer. Use BgpConnection (/routing/bgp/connection) instead.")]
    public class BgpPeer
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// Gets or sets the name of the peer.
        /// </summary>
        [TikProperty("name")]
        public TikValue<string?> Name { get; set; }

        /// <summary>
        /// Gets or sets the BGP instance that this peer belongs to.
        /// </summary>
        [TikProperty("instance")]
        public TikValue<string?> Instance { get; set; }

        /// <summary>
        /// Gets or sets the remote IP address of the peer.
        /// </summary>
        [TikProperty("remote-address")]
        public TikValue<string?> RemoteAddress { get; set; }

        /// <summary>
        /// Gets or sets the the remote peer's autonomuous system number.
        /// </summary>
        [TikProperty("remote-as")]
        public TikValue<long?> RemoteAs { get; set; }

        /// <summary>
        /// Gets or sets the next-hop choice (default, force-self, propagate).
        /// </summary>
        [TikProperty("nexthop-choice")]
        public TikValue<string?> NexthopChoice { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is a multi-hop peer.
        /// </summary>
        [TikProperty("multihop")]
        public TikValue<bool?> Multihop { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to reflect the route.
        /// </summary>
        [TikProperty("route-reflect")]
        public TikValue<bool?> RouteReflect { get; set; }

        /// <summary>
        /// Gets or sets the hold-time of this peer.
        /// </summary>
        [TikProperty("hold-time")]
        public TikValue<TikDuration?> HoldTime { get; set; }

        /// <summary>
        /// Gets or sets the time-to-live setting of this peer.
        /// </summary>
        [TikProperty("ttl")]
        public TikValue<string?> Ttl { get; set; }

        /// <summary>
        /// Gets or sets a comma-separated list of address families (ip, ipv6, l2vpn, vpn4, l2vpn-cisco) that are routed to/by this peer.
        /// </summary>
        [TikProperty("address-families")]
        public TikValue<string?> AddressFamilies { get; set; }

        /// <summary>
        /// Gets or sets the value whether default originate (never, if-installed, always).
        /// </summary>
        [TikProperty("default-originate")]
        public TikValue<string?> DefaultOriginate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove autonomuous system having private AS numbers.
        /// </summary>
        [TikProperty("remove-private-as")]
        public TikValue<bool?> RemovePrivateAs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to override the autonomuous system numbers.
        /// </summary>
        [TikProperty("as-override")]
        public TikValue<bool?> AsOverride { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to this peer as passive.
        /// </summary>
        [TikProperty("passive")]
        public TikValue<bool?> Passive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use Bidirectional Forwarding Detection with this peer.
        /// </summary>
        [TikProperty("use-bfd")]
        public TikValue<bool?> UseBfd { get; set; }

        /// <summary>
        /// Gets or sets the peer's remote ID (usually some IP address).
        /// </summary>
        [TikProperty("remote-id")]
        public TikValue<string?> RemoteId { get; set; }

        /// <summary>
        /// Gets or sets the local IP address that is used to communicate to this peer.
        /// </summary>
        [TikProperty("local-address", IsReadOnly = true)]
        public TikValue<string?> LocalAddress { get; private set; }

        /// <summary>
        /// Gets the uptime of the link to this peer.
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public TikValue<TikDuration?> Uptime { get; private set; }

        /// <summary>
        /// Gets the number of prefixes advertised by this peer.
        /// </summary>
        [TikProperty("prefix-count", IsReadOnly = true)]
        public TikValue<long?> PrefixCount { get; private set; }

        /// <summary>
        /// Gets the number of updates that have been sent to this peer. 
        /// </summary>
        [TikProperty("updates-sent", IsReadOnly = true)]
        public TikValue<long?> UpdatesSent { get; private set; }

        /// <summary>
        /// Gets the number of updates that have been received from this peer. 
        /// </summary>
        [TikProperty("updates-received", IsReadOnly = true)]
        public TikValue<long?> UpdatesReceived { get; private set; }

        /// <summary>
        /// Gets the number of withdrawals that have been sent to this peer. 
        /// </summary>
        [TikProperty("withdrawn-sent", IsReadOnly = true)]
        public TikValue<long?> WithdrawnSent { get; private set; }

        /// <summary>
        /// Gets the number of withdrawals that have been received form this peer. 
        /// </summary>
        [TikProperty("withdrawn-received", IsReadOnly = true)]
        public TikValue<long?> WithdrawnReceived { get; private set; }

        /// <summary>
        /// remote-hold-time: 
        /// </summary>
        [TikProperty("remote-hold-time", IsReadOnly = true)]
        public TikValue<TikDuration?> RemoteHoldTime { get; private set; }

        /// <summary>
        /// Gets the actually used hold-time of the link to this peer.
        /// </summary>
        [TikProperty("used-hold-time", IsReadOnly = true)]
        public TikValue<TikDuration?> UsedHoldTime { get; private set; }

        /// <summary>
        /// Gets the actually used keepalive-time of the link to this peer.
        /// </summary>
        [TikProperty("used-keepalive-time", IsReadOnly = true)]
        public TikValue<TikDuration?> UsedKeepaliveTime { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this peer has the refresh capability.
        /// </summary>
        [TikProperty("refresh-capability", IsReadOnly = true)]
        public TikValue<bool?> RefreshCapability { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this peer has the AS4 capability.
        /// </summary>
        [TikProperty("as4-capability", IsReadOnly = true)]
        public TikValue<bool?> As4Capability { get; private set; }

        /// <summary>
        /// Gets the state of the link to this peer.
        /// </summary>
        [TikProperty("state", IsReadOnly = true)]
        public TikValue<string?> State { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the link to this peer is currently established.
        /// </summary>
        [TikProperty("established", IsReadOnly = true)]
        public TikValue<bool?> Established { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this peer is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public TikValue<bool?> Disabled { get; set; }
    }
}
