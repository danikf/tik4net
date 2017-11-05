namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// ip/hotspot/user: User profile menu is used for common HotSpot client settings. Profiles are like User groups with the same set of settings, rate-limit, filter chain name, etc. 
    /// </summary>
    [RosRecord("/ip/hotspot/user/profile")]
    public class HotspotUserProfile  : IHasId {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// add-mac-cookie: Allows to add mac cookie for users. Read more&gt;&gt;
        /// </summary>
        [RosProperty("add-mac-cookie", DefaultValue = "yes")]
        public bool AddMacCookie { get; set; }

        /// <summary>
        /// address-list: Name of the address list in which users IP address will be added. Useful to mark traffic per user groups for queue tree configurations.
        /// </summary>
        [RosProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// address-pool: IP pool name from which the user will get IP. When user has improper network settings configuration on the computer, HotSpot server makes translation and assigns correct IP address from the pool instead of incorrect one
        /// </summary>
        [RosProperty("address-pool", DefaultValue = "none")]
        public string/*string |none*/ AddressPool { get; set; }

        /// <summary>
        /// advertise: Enable forced advertisement popups. After certain interval specific web-page is being displayed for HotSpot users. Advertisement page might be blocked by browsers popup blockers.
        /// </summary>
        [RosProperty("advertise", DefaultValue = "no")]
        public bool Advertise { get; set; }

        /// <summary>
        /// advertise-interval: Set of intervals between advertisement popups. After the list is done, the last value is used for all further advertisements, 10 minutes
        /// </summary>
        [RosProperty("advertise-interval", DefaultValue = "30m,10m")]
        public string/*time[,time[,..]]*/ AdvertiseInterval { get; set; }

        /// <summary>
        /// advertise-timeout: How long advertisement is shown, before blocking network access for HotSpot client. Connection to Internet is not allowed, when advertisement is not shown.
        /// </summary>
        [RosProperty("advertise-timeout", DefaultValue = "1m")]
        public string/*time | immediately | never*/ AdvertiseTimeout { get; set; }

        /// <summary>
        /// advertise-url: List of URLs that is show for advertisement popups. After the last URL is used, list starts from the begining.
        /// </summary>
        [RosProperty("advertise-url")]
        public string/*string[,string[,..]]*/ AdvertiseUrl { get; set; }

        /// <summary>
        /// idle-timeout: Maximal period of inactivity for authorized HotSpot clients. Timer is counting, when there is no traffic coming from that client and going through the router, for example computer is switched off. User is logged out, dropped of the host list, the address used by the user is freed, when timeout is reached.
        /// </summary>
        [RosProperty("idle-timeout", DefaultValue = "none", UnsetOnDefault = true)]
        public string/*time | none*/ IdleTimeout { get; set; }

        /// <summary>
        /// incoming-filter: Name of the firewall chain applied to incoming packets from the users of this profile, jump rule is required from built-in chain (input, forward, output) to chain=hotspot
        /// </summary>
        [RosProperty("incoming-filter")]
        public string IncomingFilter { get; set; }

        /// <summary>
        /// incoming-packet-mark: Packet mark put on incoming packets from every user of this profile
        /// </summary>
        [RosProperty("incoming-packet-mark")]
        public string IncomingPacketMark { get; set; }

        /// <summary>
        /// keepalive-timeout: Keepalive timeout for authorized HotSpot clients. Used to detect, that the computer of the client is alive and reachable. User is logged out, when timeout value is reached
        /// </summary>
        [RosProperty("keepalive-timeout", UnsetOnDefault = true)]
        public string/*time | none*/ KeepaliveTimeout { get; set; }

        /// <summary>
        /// mac-cookie-timeout: Selects mac-cookie timeout from last login or logout. Read more&gt;&gt;
        /// </summary>
        [RosProperty("mac-cookie-timeout", DefaultValue = "3d", UnsetOnDefault = true)]
        public string/*time*/ MacCookieTimeout { get; set; }

        /// <summary>
        /// name: Descriptive name of the profile
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// on-login
        /// Script name to be executed, when user logs in to the HotSpot from the particular profile. It is possible to get username from internal user and interface variable. For example, :log info "User $user logged in!" . If hotspot is set on bridge interface, then interface variable will show bridge as actual interface unless use-ip-firewall' is set in bridge settings.
        /// List of available variables:
        ///  $user
        ///  $username (alternative var name for $user)
        ///  $address
        ///  $mac-address
        ///  $interface
        /// </summary>
        [RosProperty("on-login", DefaultValue = "")]
        public string OnLogin { get; set; }

        /// <summary>
        /// on-logout
        /// Script name to be executed, when user logs out from the HotSpot.It is possible to get username from internal user and interface variable. For example, :log info "User $user logged in!" . If hotspot is set on bridge interface, then interface variable will show bridge as actual interface unless use-ip-firewall is set in bridge settings.
        /// List of available variables:
        ///  $user
        ///  $username (alternative var name for $user)
        ///  $address
        ///  $mac-address
        ///  $interface
        ///  $cause
        /// </summary>
        [RosProperty("on-logout", DefaultValue = "")]
        public string OnLogout { get; set; }

        /// <summary>
        /// open-status-page
        /// Option to show status page for user authenticated with mac login method. For example to show advertisement on status page (alogin.html)
        ///  http-login - open status page only for HTTP login (includes cookie and HTTPS) 	
        ///  always - open HTTP status page in case of mac login as well
        /// </summary>
        [RosProperty("open-status-page", DefaultValue = "always")]
        public string/*always | http-login*/ OpenStatusPage { get; set; }

        /// <summary>
        /// outgoing-filter: Name of the firewall chain applied to outgoing packets from the users of this profile, jump rule is required from built-in chain (input, forward, output) to chain=hotspot
        /// </summary>
        [RosProperty("outgoing-filter")]
        public string OutgoingFilter { get; set; }

        /// <summary>
        /// outgoing-packet-mark: Packet mark put on outgoing packets from every user of this profile
        /// </summary>
        [RosProperty("outgoing-packet-mark")]
        public string OutgoingPacketMark { get; set; }

        /// <summary>
        /// rate-limit: Simple dynamic queue is created for user, once it logs in to the HotSpot. Rate-limitation is configured in the following form [rx-rate[/tx-rate] [rx-burst-rate[/tx-burst-rate] [rx-burst-threshold[/tx-burst-threshold] [rx-burst-time[/tx-burst-time] [priority] [rx-rate-min[/tx-rate-min]]]]. For example, to set 1M download, 512k upload for the client, rate-limit=512k/1M
        /// </summary>
        [RosProperty("rate-limit", DefaultValue = "")]
        public string RateLimit { get; set; }

        /// <summary>
        /// session-timeout: Allowed session time for client.  After this time, the user is logged out unconditionally
        /// </summary>
        [RosProperty("session-timeout", DefaultValue = "0s")]
        public string/*time*/ SessionTimeout { get; set; }

        /// <summary>
        /// shared-users: Allowed number of simultaneously logged in users with the same HotSpot username
        /// </summary>
        [RosProperty("shared-users", DefaultValue = "unlimited")]
        public string SharedUsers { get; set; }

        /// <summary>
        /// status-autorefresh: HotSpot status page autorefresh interval
        /// </summary>
        [RosProperty("status-autorefresh", DefaultValue = "none")]
        public string/*time | none*/ StatusAutorefresh { get; set; }

        /// <summary>
        /// transparent-proxy: Use transparent HTTP proxy for the authorized users of this profile
        /// </summary>
        [RosProperty("transparent-proxy", DefaultValue = "yes")]
        public bool TransparentProxy { get; set; }
    }
}
