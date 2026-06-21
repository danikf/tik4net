namespace tik4net.Objects.Routing
{
    /// <summary>
    /// /routing/table
    ///
    /// Routing table management (RouterOS 7+). Allows creating and managing named routing tables
    /// beyond the default "main" table. Custom routing tables are used with policy-based routing
    /// (routing rules) to route traffic via alternate paths.
    /// </summary>
    [TikEntity("/routing/table", IncludeDetails = true)]
    public class RoutingTable
    {
        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — unique identifier for the routing table.
        /// Referenced by routing rules (/routing/rule) and firewall mangle rules.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// fib — when true, the table is a FIB (Forwarding Information Base) table whose
        /// entries are installed into the kernel forwarding plane. The built-in "main" table
        /// always has fib set. Default: false (not a FIB table).
        ///
        /// RouterOS quirk: the router stores this as a valueless presence-flag. On read-back
        /// it returns <c>fib=</c> (empty string) rather than <c>fib=yes</c>, so the mapper
        /// always deserializes it as <c>false</c>. Setting Fib=true and calling Save() sends
        /// <c>=fib=yes</c> which the router accepts correctly — the write path works, the
        /// read-back does not reflect the true state via this property.
        /// </summary>
        [TikProperty("fib", DefaultValue = "no")]
        public bool Fib { get; set; }

        /// <summary>
        /// disabled — when true the routing table is administratively disabled.
        /// Default: false
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // --- Read-only properties ---

        /// <summary>
        /// dynamic — true when this routing table was created dynamically by RouterOS
        /// (e.g. the built-in "main" table); false for user-created tables.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// invalid — true when the routing table entry is in an invalid/error state.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
