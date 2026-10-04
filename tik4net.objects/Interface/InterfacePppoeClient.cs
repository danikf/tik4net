namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/pppoe-client
    /// PPPoE client interface, used to dial a PPPoE server over an ethernet-like interface.
    /// </summary>
    [TikEntity("/interface/pppoe-client", IncludeDetails = true)]
    public class InterfacePppoeClient
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>ac-name — Access concentrator name to connect to. Empty connects to any access concentrator.</summary>
        [TikProperty("ac-name", WinboxLabel = "AC Name")] public TikField<string?> AcName { get; set; }

        /// <summary>add-default-route — Whether to add a default route using the peer address received during PPP negotiation.</summary>
        [TikProperty("add-default-route", DefaultValue = "false", WinboxLabel = "Add Default Route")]
        public TikField<YesNoOptions?> AddDefaultRoute { get; set; }

        /// <summary>allow — Allowed authentication methods (comma-separated).</summary>
        [TikProperty("allow", DefaultValue = "mschap2,mschap1,chap,pap", WinboxLabel = "Allow")]
        public TikField<string?> Allow { get; set; }

        /// <summary>default-route-distance — Distance metric of the default route created by add-default-route.</summary>
        [TikProperty("default-route-distance", DefaultValue = "1", WinboxLabel = "Default Route Distance")]
        public TikField<byte?> DefaultRouteDistance { get; set; }

        /// <summary>dial-on-demand — Whether to bring the connection up only when outbound traffic requires it.</summary>
        [TikProperty("dial-on-demand", DefaultValue = "false", WinboxLabel = "Dial On Demand")]
        public TikField<YesNoOptions?> DialOnDemand { get; set; }

        /// <summary>interface — Interface on which the PPPoE client looks for a PPPoE server.</summary>
        [TikProperty("interface")]
        public TikField<string?> Interface { get; set; }

        /// <summary>keepalive-timeout — Interval (seconds) used to check whether the server is still online.</summary>
        [TikProperty("keepalive-timeout", DefaultValue = "10", WinboxLabel = "Keepalive Timeout")]
        public TikField<int?> KeepaliveTimeout { get; set; }

        /// <summary>max-mru — Maximum Receive Unit negotiated with the server.</summary>
        [TikProperty("max-mru", DefaultValue = "0", WinboxLabel = "Max MRU")]
        public TikField<string?> MaxMru { get; set; }

        /// <summary>max-mtu — Maximum Transmit Unit negotiated with the server.</summary>
        [TikProperty("max-mtu", DefaultValue = "0", WinboxLabel = "Max MTU")]
        public TikField<string?> MaxMtu { get; set; }

        /// <summary>mrru — Maximum Receive Reconstructed Unit; "disabled" turns off multilink PPP.</summary>
        [TikProperty("mrru", DefaultValue = "disabled", WinboxLabel = "MRRU")]
        public TikField<string?> Mrru { get; set; }

        /// <summary>name — Name of the PPPoE client interface.</summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>password — Password used for PPP authentication.</summary>
        [TikProperty("password", IsSensitive = true, WinboxLabel = "Password")]
        public TikField<string?> Password { get; set; }

        /// <summary>profile — PPP profile applied to this connection.</summary>
        [TikProperty("profile", DefaultValue = "default", WinboxLabel = "Profile")]
        public TikField<string?> Profile { get; set; }

        /// <summary>service-name — Service name advertised by access concentrators to connect to. Empty accepts any.</summary>
        [TikProperty("service-name")]
        public TikField<string?> ServiceName { get; set; }

        /// <summary>use-peer-dns — Whether to use DNS server addresses supplied by the PPP server.</summary>
        [TikProperty("use-peer-dns", DefaultValue = "false", WinboxLabel = "Use Peer DNS")]
        public TikField<YesNoOptions?> UsePeerDns { get; set; }

        /// <summary>user — Username used for PPP authentication.</summary>
        [TikProperty("user", WinboxLabel = "User")]
        public TikField<string?> User { get; set; }

        /// <summary>Shared true/false option used by the boolean-like properties of this entity (add-default-route, dial-on-demand, use-peer-dns).</summary>
        public enum YesNoOptions
        {
            /// <summary>
            /// yes - enabled (the router writes <c>true</c>).
            /// </summary>
            [TikEnum("true")] Yes,

            /// <summary>
            /// no - disabled (the router writes <c>false</c>).
            /// </summary>
            [TikEnum("false")] No,
        }
    }
}
