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
        public string? Id { get; private set; }

        /// <summary>
        /// Gets or sets the name of this BGP instance.
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// Gets or sets the autonomuous system that this instance belongs to.
        /// </summary>
        [TikProperty("as", WinboxLabel = "AS")]
        public TikField<long?> As { get; set; }

        /// <summary>
        /// Gets or sets the ID of the router.
        /// </summary>
        [TikProperty("router-id", WinboxLabel = "IP")]
        public TikField<string?> RouterId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute connected routes.
        /// </summary>
        [TikProperty("redistribute-connected")]
        public TikField<bool?> RedistributeConnected { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute static routes.
        /// </summary>
        [TikProperty("redistribute-static")]
        public TikField<bool?> RedistributeStatic { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute routes received via RIP. 
        /// </summary>
        [TikProperty("redistribute-rip")]
        public TikField<bool?> RedistributeRip { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute routes received via OSPF.
        /// </summary>
        [TikProperty("redistribute-ospf")]
        public TikField<bool?> RedistributeOspf { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to redistribute redistribute routes received via other BGP instances.
        /// </summary>
        [TikProperty("redistribute-other-bgp")]
        public TikField<bool?> RedistributeOtherBgp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to do client-to-client reflection.
        /// </summary>
        [TikProperty("client-to-client-reflection")]
        public TikField<bool?> ClientToClientReflection { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore the autonomuous system path length.
        /// </summary>
        [TikProperty("ignore-as-path-len")]
        public TikField<bool?> IgnoreAsPathLen { get; set; }

        /// <summary>
        /// Gets a value indicating whether this is the default instance. A flag, not a setting.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public TikField<bool?> Default { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }
    }
}
