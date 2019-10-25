namespace tik4net.Objects.Routing.Bgp
{
    /// <summary>
    /// The BGP instance as provided by
    /// /routing/bgp/instance
    /// </summary>
    [TikEntity("/routing/bgp/instance")]
    public class BgpInstance
    {
        /// <summary>
        /// .id: 
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Gets or sets the name of this BGP instance.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the autonomuous system that this instance belongs to.
        /// </summary>
        [TikProperty("as")]
        public long As { get; set; }

        /// <summary>
        /// Gets or sets the ID of the router.
        /// </summary>
        [TikProperty("router-id")]
        public string RouterId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute connected routes.
        /// </summary>
        [TikProperty("redistribute-connected")]
        public bool RedistributeConnected { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute static routes.
        /// </summary>
        [TikProperty("redistribute-static")]
        public bool RedistributeStatic { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute routes received via RIP. 
        /// </summary>
        [TikProperty("redistribute-rip")]
        public bool RedistributeRip { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute routes received via OSPF.
        /// </summary>
        [TikProperty("redistribute-ospf")]
        public bool RedistributeOspf { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute routes received via other BGP instances.
        /// </summary>
        [TikProperty("redistribute-other-bgp")]
        public bool RedistributeOtherBgp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to do client-to-client reflection.
        /// </summary>
        [TikProperty("client-to-client-reflection")]
        public bool ClientToClientReflection { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore the autonomuous system path length.
        /// </summary>
        [TikProperty("ignore-as-path-len")]
        public bool IgnoreAsPathLen { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this is the default instance.
        /// </summary>
        [TikProperty("default")]
        public bool Default { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
