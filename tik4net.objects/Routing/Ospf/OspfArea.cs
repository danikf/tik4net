namespace tik4net.Objects.Routing.Ospf
{
    /// <summary>
    /// /routing/ospf/area
    ///
    /// OSPF area configuration (RouterOS 7+). Each area belongs to an OSPF instance and groups
    /// the interfaces/networks participating in that area. The backbone area (area-id 0.0.0.0)
    /// must exist for multi-area OSPF. Stub and NSSA area types reduce LSA flooding by limiting
    /// external route advertisements.
    /// </summary>
    [TikEntity("/routing/ospf/area", IncludeDetails = true)]
    public class OspfArea
    {
        /// <summary>OSPF area type.</summary>
        public enum OspfAreaType
        {
            /// <summary>default — normal OSPF area; all LSA types are flooded. Router default.</summary>
            [TikEnum("default")] Default,
            /// <summary>nssa — Not-So-Stubby Area; allows limited external routes via Type-7 LSAs.</summary>
            [TikEnum("nssa")] Nssa,
            /// <summary>stub — stub area; external LSAs are blocked; a default route is injected by the ABR.</summary>
            [TikEnum("stub")] Stub,
        }

        /// <summary>NSSA translator role for this ABR.</summary>
        public enum NssaTranslatorMode
        {
            /// <summary>candidate — this ABR may become the NSSA translator (elected). Default.</summary>
            [TikEnum("candidate")] Candidate,
            /// <summary>no — this ABR will never translate Type-7 LSAs to Type-5.</summary>
            [TikEnum("no")] No,
            /// <summary>yes — this ABR always acts as the NSSA translator.</summary>
            [TikEnum("yes")] Yes,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — unique name for this OSPF area entry.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// instance — name of the OSPF instance this area belongs to.
        /// Must reference an existing /routing/ospf/instance entry.
        /// This field is mandatory — the router rejects add without it.
        /// </summary>
        [TikProperty("instance", IsMandatory = true)]
        public string Instance { get; set; }

        /// <summary>
        /// area-id — OSPF area identifier in dotted-decimal notation (e.g. "0.0.0.0" for the backbone).
        /// Kept as string to accommodate all valid forms.
        /// Default: 0.0.0.0
        /// </summary>
        [TikProperty("area-id", DefaultValue = "0.0.0.0")]
        public string AreaId { get; set; }

        /// <summary>
        /// type — OSPF area type controlling which LSA types are permitted.
        /// Default: default
        /// </summary>
        /// <seealso cref="OspfAreaType"/>
        [TikProperty("type", DefaultValue = "default")]
        public OspfAreaType Type { get; set; }

        /// <summary>
        /// no-summaries — when true, the ABR does not send summary LSAs (Type-3/4) into this stub/NSSA area,
        /// effectively making it a totally-stub or totally-NSSA area.
        /// Default: false
        /// </summary>
        [TikProperty("no-summaries", DefaultValue = "no")]
        public bool NoSummaries { get; set; }

        /// <summary>
        /// default-cost — cost of the default route injected by the ABR into a stub or NSSA area.
        /// Valid range: 1..16777214. DefaultValue="0" is a CLR-sentinel so the mapper omits it on add
        /// when left unset; set to a real value to override the router's built-in default.
        /// </summary>
        [TikProperty("default-cost", DefaultValue = "0")]
        public int DefaultCost { get; set; }

        /// <summary>
        /// nssa-translator — controls NSSA Type-7 to Type-5 LSA translation role of this ABR.
        /// Applies only when type=nssa.
        /// Default: candidate
        /// </summary>
        /// <seealso cref="NssaTranslatorMode"/>
        [TikProperty("nssa-translator", DefaultValue = "candidate")]
        public NssaTranslatorMode NssaTranslator { get; set; }

        /// <summary>
        /// disabled — when true this area entry is administratively disabled.
        /// Default: false
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// inactive — true when this area is not active (e.g. the parent instance is disabled
        /// or the routing package is not running).
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
