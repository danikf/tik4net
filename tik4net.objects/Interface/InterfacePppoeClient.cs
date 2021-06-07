namespace tik4net.Objects.Interface
{
    [TikEntity("interface/pppoe-client", IncludeDetails = true)]
    public class InterfacePppoeClient
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("ac-name")] public string AcName { get; set; }

        [TikProperty("add-default-route", DefaultValue = "false")]
        public YesNoOptions AddDefaultRoute { get; set; }

        [TikProperty("allow", DefaultValue = "mschap2,mschap1,chap,pap")]
        public string Allow { get; set; }

        [TikProperty("default-route-distance", DefaultValue = "1")]
        public byte DefaultRouteDistance { get; set; }

        [TikProperty("dial-on-demand", DefaultValue = "false")]
        public YesNoOptions DialOnDemand { get; set; }

        [TikProperty("interface")] 
        public string Interface { get; set; }

        [TikProperty("keepalive-timeout", DefaultValue = "60")]
        public int KeepaliveTimeout { get; set; }

        [TikProperty("max-mru", DefaultValue = "1460")]
        public string MaxMru { get; set; }

        [TikProperty("max-mtu", DefaultValue = "1460")]
        public string MaxMtu { get; set; }

        [TikProperty("mrru", DefaultValue = "disabled")]
        public string Mrru { get; set; }

        [TikProperty("name")] 
        public string Name { get; set; }

        [TikProperty("password")] 
        public string Password { get; set; }

        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        [TikProperty("service-name")] 
        public string ServiceName { get; set; }

        [TikProperty("use-peer-dns", DefaultValue = "false")]
        public YesNoOptions UsePeerDns { get; set; }

        [TikProperty("user")] 
        public string User { get; set; }

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