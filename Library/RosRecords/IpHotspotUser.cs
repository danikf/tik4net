namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// ip/hotspot/user
    /// 
    /// This is the menu, where client's user/password information is actually added, additional configuration options for HotSpot users are configured here as well.
    /// </summary>
    [RosRecord("/ip/hotspot/user")]
    public class IpHotspotUser  : IHasId {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// address: IP address, when specified client will get the address from the HotSpot one-to-one NAT translations. Address does not restrict HotSpot login only from this address
        /// </summary>
        [RosProperty("address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ Address { get; set; }

        /// <summary>
        /// comment: descriptive information for HotSpot user, it might be used for scripts to change parameters for specific clients
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// email: HotSpot client's e-mail, informational value for the HotSpot user
        /// </summary>
        [RosProperty("email")]
        public string Email { get; set; }

        /// <summary>
        /// limit-bytes-in: Maximal amount of bytes that can be received from the user. User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [RosProperty("limit-bytes-in")]
        public long LimitBytesIn { get; set; }

        /// <summary>
        /// limit-bytes-out: Maximal amount of bytes that can be transmitted from the user. User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [RosProperty("limit-bytes-out")]
        public long LimitBytesOut { get; set; }

        /// <summary>
        /// limit-bytes-total: (limit-bytes-in+limit-bytes-out). User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [RosProperty("limit-bytes-total")]
        public long LimitBytesTotal { get; set; }

        /// <summary>
        /// limit-uptime: Uptime limit for the HotSpot client, user is disconnected from HotSpot as soon as uptime is reached.
        /// </summary>
        [RosProperty("limit-uptime", DefaultValue = "0")]
        public string/*time*/ LimitUptime { get; set; }

        /// <summary>
        /// mac-address: Client is allowed to login only from the specified MAC-address. If value is  00:00:00:00:00:00, any mac address is allowed.
        /// </summary>
        [RosProperty("mac-address", DefaultValue = "00:00:00:00:00:00")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// name: HotSpot login page username, when MAC-address authentication is used name is configured as client's MAC-address
        /// </summary>
        [RosProperty("name",IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// password: User password
        /// </summary>
        [RosProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// profile: User profile configured in  /ip hotspot user profile
        /// </summary>
        [RosProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>
        /// routes: Routes added to HotSpot gateway when client is connected. The route format dst-address gateway metric (for example, 192.168.1.0/24 192.168.0.1 1)
        /// </summary>
        [RosProperty("routes")]
        public string Routes { get; set; }

        /// <summary>
        /// server: HotSpot server's name to which user is allowed login
        /// </summary>
        [RosProperty("server", DefaultValue = "all")]
        public string/*string | all*/ Server { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// bytes-in: 
        /// </summary>
        [RosProperty("bytes-in",IsReadOnly = true)]
        public long BytesIn { get; private set; }

        /// <summary>
        /// bytes-out: 
        /// </summary>
        [RosProperty("bytes-out",IsReadOnly = true)]
        public long BytesOut { get; private set; }

        /// <summary>
        /// packets-in: 
        /// </summary>
        [RosProperty("packets-in",IsReadOnly = true)]
        public long PacketsIn { get; private set; }

        /// <summary>
        /// packets-out: 
        /// </summary>
        [RosProperty("packets-out",IsReadOnly = true)]
        public long PacketsOut { get; private set; }

        /// <summary>
        /// uptime: 
        /// </summary>
        [RosProperty("uptime",IsReadOnly = true)]
        public string Uptime { get; private set; }

        /// <summary>
        /// ctor
        /// </summary>
        public IpHotspotUser() {
            Server = "all";
        }
    }
}
