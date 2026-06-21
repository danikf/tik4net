namespace tik4net.Objects.Routing
{
    /// <summary>
    /// /routing/rule
    ///
    /// Policy-based routing rules (RouterOS 7+). Routing rules are evaluated in order and
    /// determine which routing table is used for a packet matching the criteria.
    /// Rules match on source/destination address, incoming interface, routing-mark, and
    /// minimum prefix length, then perform an action (lookup in a table, drop, or return
    /// unreachable). This menu is ordered — use Move() to reorder entries.
    /// </summary>
    [TikEntity("/routing/rule", IncludeDetails = true, IsOrdered = true)]
    public class RoutingRule
    {
        /// <summary>action values for <see cref="RoutingRule"/></summary>
        public enum ActionType
        {
            /// <summary>lookup — look up the route in the routing table specified by <see cref="Table"/>. This is the router default.</summary>
            [TikEnum("lookup")] Lookup,
            /// <summary>lookup-only-in-table — look up only in the specified table; do not fall through to the main table.</summary>
            [TikEnum("lookup-only-in-table")] LookupOnlyInTable,
            /// <summary>drop — silently discard matching packets.</summary>
            [TikEnum("drop")] Drop,
            /// <summary>unreachable — reply with ICMP unreachable and drop the packet.</summary>
            [TikEnum("unreachable")] Unreachable,
        }

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// action — what to do when a packet matches this rule.
        /// Default: lookup (look up in the table named by <see cref="Table"/>).
        /// </summary>
        /// <seealso cref="ActionType"/>
        [TikProperty("action", DefaultValue = "lookup")]
        public ActionType Action { get; set; }

        /// <summary>
        /// table — name of the routing table to use for lookup when action is lookup or lookup-only-in-table.
        /// References a table defined in /routing/table (or the built-in "main" table).
        /// </summary>
        [TikProperty("table")]
        public string Table { get; set; }

        /// <summary>
        /// src-address — match packets whose source IP address falls within this prefix (e.g. 192.0.2.0/24).
        /// Leave empty to match any source address.
        /// </summary>
        [TikProperty("src-address")]
        public string SrcAddress { get; set; }

        /// <summary>
        /// dst-address — match packets whose destination IP address falls within this prefix (e.g. 198.51.100.0/24).
        /// Leave empty to match any destination address.
        /// </summary>
        [TikProperty("dst-address")]
        public string DstAddress { get; set; }

        /// <summary>
        /// interface — match packets arriving on this interface name.
        /// Leave empty to match all interfaces.
        /// </summary>
        [TikProperty("interface")]
        public string Interface { get; set; }

        /// <summary>
        /// routing-mark — match packets that carry this firewall routing mark (set by mangle rules).
        /// Leave empty to match unmarked packets / all packets.
        /// </summary>
        [TikProperty("routing-mark")]
        public string RoutingMark { get; set; }

        /// <summary>
        /// min-prefix — minimum prefix length of the matched destination route.
        /// Routes with a prefix length shorter than this value are not considered.
        /// Valid range: 0–128. Default (0) means no minimum — all routes qualify.
        /// DefaultValue="0" makes the mapper skip this field on add when it is unset (CLR default 0),
        /// preventing a "value out of range" rejection from the router.
        /// </summary>
        [TikProperty("min-prefix", DefaultValue = "0")]
        public int MinPrefix { get; set; }

        /// <summary>
        /// disabled — when true the rule is administratively disabled and skipped during evaluation.
        /// Default: false
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// inactive — true when the rule is inactive (e.g. the referenced interface or table does not exist).
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>Human-readable summary.</summary>
        public override string ToString()
        {
            string src = string.IsNullOrEmpty(SrcAddress) ? "any" : SrcAddress;
            string dst = string.IsNullOrEmpty(DstAddress) ? "any" : DstAddress;
            return string.Format("src={0} dst={1} action={2}", src, dst, Action);
        }
    }
}
