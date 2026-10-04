namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// ip/hotspot/user
    /// 
    /// This is the menu, where client's user/password information is actually added, additional configuration options for HotSpot users are configured here as well.
    /// </summary>
    [TikEntity("/ip/hotspot/user", IncludeDetails = true)]
    public class HotspotUser
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// address: IP address, when specified client will get the address from the HotSpot one-to-one NAT translations. Address does not restrict HotSpot login only from this address
        /// </summary>
        [TikProperty("address", DefaultValue = "0.0.0.0", WinboxLabel = "Address")]
        public TikField<string?> Address { get; set; }

        /// <summary>
        /// comment: descriptive information for HotSpot user, it might be used for scripts to change parameters for specific clients
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// email: HotSpot client's e-mail, informational value for the HotSpot user
        /// </summary>
        [TikProperty("email", WinboxLabel = "Email")]
        public TikField<string?> Email { get; set; }

        /// <summary>
        /// limit-bytes-in: Maximal amount of bytes that can be received from the user. User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [TikProperty("limit-bytes-in", WinboxLabel = "Limit Bytes In")]
        public TikField<long?> LimitBytesIn { get; set; }

        /// <summary>
        /// limit-bytes-out: Maximal amount of bytes that can be transmitted from the user. User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [TikProperty("limit-bytes-out", WinboxLabel = "Limit Bytes Out")]
        public TikField<long?> LimitBytesOut { get; set; }

        /// <summary>
        /// limit-bytes-total: (limit-bytes-in+limit-bytes-out). User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [TikProperty("limit-bytes-total", WinboxLabel = "Limit Bytes Total")]
        public TikField<long?> LimitBytesTotal { get; set; }

        /// <summary>
        /// limit-uptime: Uptime limit for the HotSpot client, user is disconnected from HotSpot as soon as uptime is reached.
        /// </summary>
        [TikProperty("limit-uptime", DefaultValue = "0", WinboxLabel = "Limit Uptime")]
        public TikField<TikDuration?> LimitUptime { get; set; }

        /// <summary>
        /// mac-address: Client is allowed to login only from the specified MAC-address. If value is  00:00:00:00:00:00, any mac address is allowed.
        /// </summary>
        [TikProperty("mac-address", DefaultValue = "00:00:00:00:00:00", WinboxLabel = "MAC Address")]
        public TikField<string?> MacAddress { get; set; }

        /// <summary>
        /// name: HotSpot login page username, when MAC-address authentication is used name is configured as client's MAC-address
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// password: User password
        /// </summary>
        [TikProperty("password", IsSensitive = true, WinboxLabel = "Password")]
        public TikField<string?> Password { get; set; }

        /// <summary>
        /// profile: User profile configured in  /ip hotspot user profile
        /// </summary>
        [TikProperty("profile", DefaultValue = "default", WinboxLabel = "Profile")]
        public TikField<string?> Profile { get; set; }

        /// <summary>
        /// routes: Routes added to HotSpot gateway when client is connected. The route format dst-address gateway metric (for example, 192.168.1.0/24 192.168.0.1 1)
        /// </summary>
        [TikProperty("routes", WinboxLabel = "Routes")]
        public TikField<string?> Routes { get; set; }

        /// <summary>
        /// server: HotSpot server's name to which user is allowed login
        /// </summary>
        [TikProperty("server", DefaultValue = "all", WinboxLabel = "Server")]
        public TikField<string?> Server { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// bytes-in: 
        /// </summary>
        [TikProperty("bytes-in", IsReadOnly = true, WinboxLabel = "Bytes In")]
        public TikField<long?> BytesIn { get; private set; }

        /// <summary>
        /// bytes-out: 
        /// </summary>
        [TikProperty("bytes-out", IsReadOnly = true, WinboxLabel = "Bytes Out")]
        public TikField<long?> BytesOut { get; private set; }

        /// <summary>
        /// packets-in: 
        /// </summary>
        [TikProperty("packets-in", IsReadOnly = true, WinboxLabel = "Packets In")]
        public TikField<long?> PacketsIn { get; private set; }

        /// <summary>
        /// packets-out: 
        /// </summary>
        [TikProperty("packets-out", IsReadOnly = true, WinboxLabel = "Packets Out")]
        public TikField<long?> PacketsOut { get; private set; }

        /// <summary>
        /// uptime: 
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true, WinboxLabel = "Uptime")]
        public TikField<TikDuration?> Uptime { get; private set; }

    }
}
