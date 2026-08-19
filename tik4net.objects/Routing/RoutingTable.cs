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
        public string? Id { get; private set; }

        /// <summary>
        /// name — unique identifier for the routing table.
        /// Referenced by routing rules (/routing/rule) and firewall mangle rules.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string? Name { get; set; }

        /// <summary>
        /// fib — when true, the table is a FIB (Forwarding Information Base) table whose
        /// entries are installed into the kernel forwarding plane. The built-in "main" table
        /// always has fib set. Default: false (not a FIB table).
        ///
        /// RouterOS stores this as a valueless presence-flag: over the binary API and REST a read
        /// returns <c>fib=</c> (the word present, the value empty) when the flag is set, and omits
        /// the word when it is not. The property is declared <see cref="TikPropertyAttribute.IsPresenceFlag"/>,
        /// so the empty value reads as <c>true</c> — the CLI transports and <c>WinboxNative</c> report
        /// the same row as <c>true</c> outright, and all three now agree. A row that does not carry the
        /// word at all leaves the property <c>null</c>: the router reported nothing.
        ///
        /// Writing is unaffected: Save() sends <c>=fib=yes</c>, which the router accepts.
        /// </summary>
        [TikProperty("fib", DefaultValue = "no", IsPresenceFlag = true)]
        public bool? Fib { get; set; }

        /// <summary>
        /// disabled — when true the routing table is administratively disabled.
        /// Default: false
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool? Disabled { get; set; }

        /// <summary>comment — optional free-text annotation.</summary>
        [TikProperty("comment")]
        public string? Comment { get; set; }

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
        public override string? ToString() => Name;
    }
}
