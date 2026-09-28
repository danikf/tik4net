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
    // IncludeDetails: a bare CLI `print as-value` returns only the summary columns — scope,
    // target-scope, immediate-gw, local-address and vrf-interface are detail-only, so without this the
    // five CLI transports read them as the CLR default while the API reports them. `/ip/route print`
    // accepts `detail` (asked, not assumed).
    [TikEntity("/ip/route", IncludeDetails = true)]
    public class IpRoute
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// Gets or sets the destination address of the route.
        /// </summary>
        [TikProperty("dst-address", WinboxLabel = "Dst. Address")]
        public TikValue<string?> DstAddress { get; set; }

        /// <summary>
        /// Gets or sets the gateway IP address of the route.
        /// </summary>
        [TikProperty("gateway", WinboxLabel = "Gateway")]
        public TikValue<string?> Gateway { get; set; }

        /// <summary>
        /// Gets or sets the routing table this route belongs to (<c>main</c> unless set).
        /// </summary>
        /// <remarks>
        /// RouterOS 7 names the field <c>routing-table</c> and RouterOS 6 <c>routing-mark</c>, and each refuses the
        /// other name. It is read under whichever the router prints, and a route read with a mark is saved under that
        /// name again. Otherwise it is written as <c>routing-table</c>: RouterOS 6 prints no <c>routing-mark</c> for a
        /// route in the main table, so on RouterOS 6 giving a new route, or a main-table route, a mark is refused.
        /// </remarks>
        [TikProperty("routing-table", AlternateNames = new[] { "routing-mark" })]
        public TikValue<string?> RoutingTable { get; set; }

        /// <summary>
        /// Gets the gateway status of this route.
        /// </summary>
        [TikProperty("gateway-status", IsReadOnly = true)]
        public TikValue<string?> GatewayStatus { get; private set; }

        /// <summary>
        /// Gets or sets the distance of this route in hops. 
        /// </summary>
        [TikProperty("distance", WinboxLabel = "Distance")]
        public TikValue<long?> Distance { get; set; }

        /// <summary>
        /// Gets or sets the scope of this route.
        /// </summary>
        [TikProperty("scope", WinboxLabel = "Scope")]
        public TikValue<long?> Scope { get; set; }

        /// <summary>
        /// Gets or sets the target scope of this route.
        /// </summary>
        [TikProperty("target-scope", WinboxLabel = "Target Scope")]
        public TikValue<long?> TargetScope { get; set; }

        /// <summary>
        /// Gets a value indicating whether this route is currently active.
        /// </summary>
        [TikProperty("active", IsReadOnly = true)]
        public TikValue<bool?> Active { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this is a static route.
        /// </summary>
        [TikProperty("static", IsReadOnly = true)]
        public TikValue<bool?> Static { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this route is currently disabled.
        /// </summary>
        [TikProperty("disabled")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>
        /// Gets or sets the route's comment.
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }

        /// <summary>
        /// Gets the BGP autonomuous system path as comma-separated list.
        /// </summary>
        [TikProperty("bgp-as-path", IsReadOnly = true)]
        public TikValue<string?> BgpAsPath { get; private set; }

        /// <summary>
        /// Gets the BGP origin that provided this route.
        /// </summary>
        [TikProperty("bgp-origin", IsReadOnly = true)]
        public TikValue<string?> BgpOrigin { get; private set; }

        /// <summary>
        /// Gets the BGP communities of this route.
        /// </summary>
        [TikProperty("bgp-communities", IsReadOnly = true)]
        public TikValue<string?> BgpCommunities { get; private set; }

        /// <summary>
        /// Gets the info from which peer (peer name as defined for the routing protocol) this route has been received.
        /// </summary>
        [TikProperty("received-from", IsReadOnly = true)]
        public TikValue<string?> ReceivedFrom { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this route is a dynamic route.
        /// </summary>
        /// <remarks>
        /// For dynamic routes most of the writeable properties cannot be set as they're set dynamically.<br/>
        /// This is, however, currently not reflected by the C# properties.
        /// </remarks>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikValue<bool?> Dynamic { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this route is a BGP route.
        /// </summary>
        [TikProperty("bgp", IsReadOnly = true)]
        public TikValue<bool?> Bgp { get; private set; }

        /// <summary>
        /// Gets the preferred source address of this route.
        /// </summary>
        [TikProperty("pref-src", IsReadOnly = true, WinboxLabel = "Pref. Source")]
        public TikValue<string?> PrefSrc { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this route is currently connected.
        /// </summary>
        [TikProperty("connect", IsReadOnly = true)]
        public TikValue<bool?> Connect { get; private set; }
    }
}
