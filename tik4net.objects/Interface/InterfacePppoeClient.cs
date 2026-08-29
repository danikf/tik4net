namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/pppoe-client
    /// PPPoE client interface, used to dial a PPPoE server over an ethernet-like interface.
    /// </summary>
    [TikEntity("interface/pppoe-client", IncludeDetails = true)]
    public class InterfacePppoeClient
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>ac-name — Access concentrator name to connect to. Empty connects to any access concentrator.</summary>
        [TikProperty("ac-name")] public string? AcName { get; set; }

        /// <summary>add-default-route — Whether to add a default route using the peer address received during PPP negotiation.</summary>
        [TikProperty("add-default-route", DefaultValue = "false")]
        public YesNoOptions? AddDefaultRoute { get; set; }

        /// <summary>allow — Allowed authentication methods (comma-separated).</summary>
        [TikProperty("allow", DefaultValue = "mschap2,mschap1,chap,pap")]
        public string? Allow { get; set; }

        /// <summary>default-route-distance — Distance metric of the default route created by add-default-route.</summary>
        [TikProperty("default-route-distance", DefaultValue = "1")]
        public byte? DefaultRouteDistance { get; set; }

        /// <summary>dial-on-demand — Whether to bring the connection up only when outbound traffic requires it.</summary>
        [TikProperty("dial-on-demand", DefaultValue = "false")]
        public YesNoOptions? DialOnDemand { get; set; }

        /// <summary>interface — Interface on which the PPPoE client looks for a PPPoE server.</summary>
        [TikProperty("interface")]
        public string? Interface { get; set; }

        /// <summary>keepalive-timeout — Interval (seconds) used to check whether the server is still online.</summary>
        [TikProperty("keepalive-timeout", DefaultValue = "60")]
        public int? KeepaliveTimeout { get; set; }

        /// <summary>max-mru — Maximum Receive Unit negotiated with the server.</summary>
        [TikProperty("max-mru", DefaultValue = "1460")]
        public string? MaxMru { get; set; }

        /// <summary>max-mtu — Maximum Transmit Unit negotiated with the server.</summary>
        [TikProperty("max-mtu", DefaultValue = "1460")]
        public string? MaxMtu { get; set; }

        /// <summary>mrru — Maximum Receive Reconstructed Unit; "disabled" turns off multilink PPP.</summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string? Mrru { get; set; }

        /// <summary>name — Name of the PPPoE client interface.</summary>
        [TikProperty("name")]
        public string? Name { get; set; }

        /// <summary>password — Password used for PPP authentication.</summary>
        [TikProperty("password")]
        public string? Password { get; set; }

        /// <summary>profile — PPP profile applied to this connection.</summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string? Profile { get; set; }

        /// <summary>service-name — Service name advertised by access concentrators to connect to. Empty accepts any.</summary>
        [TikProperty("service-name")]
        public string? ServiceName { get; set; }

        /// <summary>use-peer-dns — Whether to use DNS server addresses supplied by the PPP server.</summary>
        [TikProperty("use-peer-dns", DefaultValue = "false")]
        public YesNoOptions? UsePeerDns { get; set; }

        /// <summary>user — Username used for PPP authentication.</summary>
        [TikProperty("user")]
        public string? User { get; set; }

        /// <summary>Shared true/false option used by the boolean-like properties of this entity (add-default-route, dial-on-demand, use-peer-dns).</summary>
        public enum YesNoOptions
        {
            /// <summary>
            /// yes
            /// </summary>
            [TikEnum("true")] Yes,

            /// <summary>
            /// no
            /// </summary>
            [TikEnum("false")] No,
        }
    }
}
