namespace tik4net.Objects.Routing.Bgp
{
    /// <summary>
    /// Access to the data provided by
    /// /routing/bgp/network
    /// </summary>
    [TikEntity("/routing/bgp/network")]
    public class BgpNetwork
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Gets or sets the BGP advirtised network in CIDR format (e.g. 44.224.10.64/29).
        /// </summary>
        [TikProperty("network")]
        public string Network { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to synchronize this network.
        /// </summary>
        [TikProperty("synchronize")]
        public bool Synchronize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this network is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
