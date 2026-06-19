namespace tik4net.Objects.Routing.Filter
{
    /// <summary>
    /// /routing/filter/rule
    ///
    /// Routing filter rules (RouterOS 7+). Each rule belongs to a named chain and contains a
    /// script-like expression that matches route attributes and applies actions (accept, reject,
    /// or attribute modification). Chains are referenced from routing protocol instances via their
    /// in-filter-chain / out-filter-chain / out-filter-select settings.
    ///
    /// Rule syntax:  if ( [matchers] ) { [actions] } else { [actions] }
    /// Example:      if (dst == 10.0.0.0/8) { accept }
    ///
    /// Rules in each chain are evaluated top-to-bottom (ordered list); the first matching rule wins.
    /// </summary>
    [TikEntity("/routing/filter/rule", IncludeDetails = true, IsOrdered = true)]
    public class RoutingFilterRule
    {
        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// chain — name of the routing filter chain this rule belongs to.
        /// Multiple rules can share the same chain name; they are evaluated in order.
        /// Chain names are arbitrary strings referenced by protocol instance filter settings.
        /// </summary>
        [TikProperty("chain", IsMandatory = true)]
        public string Chain { get; set; }

        /// <summary>
        /// rule — the filter rule expression in RouterOS routing filter scripting language.
        /// Syntax: if ( [matchers] ) { [actions] } else { [actions] }
        /// Matchers test route attributes (dst, bgp-communities, ospf-metric, …).
        /// Actions modify attributes or terminate evaluation (accept, reject, return, …).
        /// Example: "if (dst == 192.168.0.0/16) { set bgp-local-pref 200; accept }"
        /// </summary>
        [TikProperty("rule")]
        public string Rule { get; set; }

        /// <summary>
        /// disabled — when true this rule is administratively disabled and skipped during evaluation.
        /// Default: no
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// inactive — true when this rule is not active (e.g. because it is disabled or
        /// the routing package is not running).
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => string.Format("[{0}] {1}", Chain, Rule);
    }
}
