namespace tik4net.Objects.Ip
{
    /// <summary>
    /// Access to the data provided by
    /// /ip/route
    /// </summary>
    /// <remarks>
    /// Please note that even though many properties are not tagged &quot;readonly&quot; they still might be
    /// read-only for non-static routes (e.g. routes that are inserted by routing protocols).
    /// </remarks>
    [TikEntity("/ip/route")]
    public class IpRoute
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Gets or sets the destination address of the route.
        /// </summary>
        [TikProperty("dst-address")]
        public string DstAddress { get; set; }

        /// <summary>
        /// Gets or sets the gateway IP address of the route.
        /// </summary>
        [TikProperty("gateway")]
        public string Gateway { get; set; }

        /// <summary>
        /// Gets the gateway status of this route.
        /// </summary>
        [TikProperty("gateway-status", IsReadOnly = true)]
        public string GatewayStatus { get; private set; }

        /// <summary>
        /// Gets or sets the distance of this route in hops. 
        /// </summary>
        [TikProperty("distance")]
        public long Distance { get; set; }

        /// <summary>
        /// Gets or sets the scope of this route.
        /// </summary>
        [TikProperty("scope")]
        public long Scope { get; set; }

        /// <summary>
        /// Gets or sets the target scope of this route.
        /// </summary>
        [TikProperty("target-scope")]
        public long TargetScope { get; set; }

        /// <summary>
        /// Gets a value indicating whether this route is currently active.
        /// </summary>
        [TikProperty("active", IsReadOnly = true)]
        public bool Active { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this is a static route.
        /// </summary>
        [TikProperty("static", IsReadOnly = true)]
        public bool Static { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this route is currently disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// Gets the BGP autonomuous system path as comma-separated list.
        /// </summary>
        [TikProperty("bgp-as-path", IsReadOnly = true)]
        public string BgpAsPath { get; private set; }

        /// <summary>
        /// Gets the BGP origin that provided this route.
        /// </summary>
        [TikProperty("bgp-origin", IsReadOnly = true)]
        public string BgpOrigin { get; private set; }

        /// <summary>
        /// Gets the BGP communities of this route.
        /// </summary>
        [TikProperty("bgp-communities", IsReadOnly = true)]
        public string BgpCommunities { get; private set; }

        /// <summary>
        /// Gets the info from which peer (peer name as defined for the routing protocol) this route has been received.
        /// </summary>
        [TikProperty("received-from", IsReadOnly = true)]
        public string ReceivedFrom { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this route is a dynamic route.
        /// </summary>
        /// <remarks>
        /// For dynamic routes most of the writeable properties cannot be set as they're set dynamically.<br/>
        /// This is, however, currently not reflected by the C# properties.
        /// </remarks>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this route is a BGP route.
        /// </summary>
        [TikProperty("bgp", IsReadOnly = true)]
        public bool Bgp { get; private set; }

        /// <summary>
        /// Gets the preferred source address of this route.
        /// </summary>
        [TikProperty("pref-src", IsReadOnly = true)]
        public string PrefSrc { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this route is currently connected.
        /// </summary>
        [TikProperty("connect", IsReadOnly = true)]
        public bool Connect { get; private set; }
    }
}
