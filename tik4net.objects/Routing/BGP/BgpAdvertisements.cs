namespace tik4net.Objects.Routing.Bgp
{
    /// <summary>
    /// Mikrotik BGP advertisements as provided by
    /// /routing/bgp/advertisements
    /// </summary>
    [TikEntity("routing/bgp/advertisements", IsReadOnly = true)]
    public class BgpAdvertisements
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Gets or sets the name of the BGP peer from which the advertisement has been received.
        /// </summary>
        [TikProperty("peer")]
        public string Peer { get; set; }

        /// <summary>
        /// Gets or sets the advertised IP prefix.
        /// </summary>
        [TikProperty("prefix")]
        public string Prefix { get; set; }

        /// <summary>
        /// Gets or sets the next-hop host IP.
        /// </summary>
        [TikProperty("nexthop")]
        public string Nexthop { get; set; }

        /// <summary>
        /// Gets are sets the autonomuous system path to the prefix.
        /// </summary>
        [TikProperty("as-path")]
        public string AsPath { get; set; }

        /// <summary>
        /// Gets or sets the origin that provided the info of this advertisement.
        /// </summary>
        [TikProperty("origin")]
        public string Origin { get; set; }

        /// <summary>
        /// communities: 
        /// </summary>
        [TikProperty("communities")]
        public string Communities { get; set; }
    }
}
