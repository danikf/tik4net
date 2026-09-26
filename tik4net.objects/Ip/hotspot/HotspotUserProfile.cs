namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// ip/hotspot/user: User profile menu is used for common HotSpot client settings. Profiles are like User groups with the same set of settings, rate-limit, filter chain name, etc. 
    /// </summary>
    [TikEntity("/ip/hotspot/user/profile")]
    public class HotspotUserProfile
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// add-mac-cookie: Allows to add mac cookie for users. Read more&gt;&gt;
        /// </summary>
        [TikProperty("add-mac-cookie", DefaultValue = "yes", WinboxLabel = "Add MAC Cookie")]
        public TikValue<bool?> AddMacCookie { get; set; }

        /// <summary>
        /// address-list: Name of the address list in which users IP address will be added. Useful to mark traffic per user groups for queue tree configurations.
        /// </summary>
        [TikProperty("address-list", WinboxLabel = "Address List")]
        public TikValue<string?> AddressList { get; set; }

        /// <summary>
        /// address-pool: IP pool name from which the user will get IP. When user has improper network settings configuration on the computer, HotSpot server makes translation and assigns correct IP address from the pool instead of incorrect one
        /// </summary>
        [TikProperty("address-pool", DefaultValue = "none", WinboxLabel = "Address Pool")]
        public TikValue<string?> AddressPool { get; set; }

        /// <summary>
        /// advertise: Enable forced advertisement popups. After certain interval specific web-page is being displayed for HotSpot users. Advertisement page might be blocked by browsers popup blockers.
        /// </summary>
        [TikProperty("advertise", DefaultValue = "no", WinboxLabel = "Advertise")]
        public TikValue<bool?> Advertise { get; set; }

        /// <summary>
        /// advertise-interval: Set of intervals between advertisement popups. After the list is done, the last value is used for all further advertisements, 10 minutes
        /// </summary>
        [TikProperty("advertise-interval", DefaultValue = "30m,10m", WinboxLabel = "Advertise Interval")]
        public TikValue<string?> AdvertiseInterval { get; set; }

        /// <summary>
        /// advertise-timeout: How long advertisement is shown, before blocking network access for HotSpot client. Connection to Internet is not allowed, when advertisement is not shown.
        /// </summary>
        [TikProperty("advertise-timeout", DefaultValue = "1m", WinboxLabel = "Advertise Timeout")]
        public TikValue<string?> AdvertiseTimeout { get; set; }

        /// <summary>
        /// advertise-url: List of URLs that is show for advertisement popups. After the last URL is used, list starts from the begining.
        /// </summary>
        [TikProperty("advertise-url", WinboxLabel = "Advertise URL")]
        public TikValue<string?> AdvertiseUrl { get; set; }

        /// <summary>
        /// idle-timeout: Maximal period of inactivity for authorized HotSpot clients. Timer is counting, when there is no traffic coming from that client and going through the router, for example computer is switched off. User is logged out, dropped of the host list, the address used by the user is freed, when timeout is reached.
        /// </summary>
        [TikProperty("idle-timeout", DefaultValue = "none", WinboxLabel = "Idle Timeout")]
        public TikValue<TikDuration?> IdleTimeout { get; set; }

        /// <summary>
        /// incoming-filter: Name of the firewall chain applied to incoming packets from the users of this profile, jump rule is required from built-in chain (input, forward, output) to chain=hotspot
        /// </summary>
        [TikProperty("incoming-filter", WinboxLabel = "Incoming Filter")]
        public TikValue<string?> IncomingFilter { get; set; }

        /// <summary>
        /// incoming-packet-mark: Packet mark put on incoming packets from every user of this profile
        /// </summary>
        [TikProperty("incoming-packet-mark", WinboxLabel = "Incoming Packet Mark")]
        public TikValue<string?> IncomingPacketMark { get; set; }

        /// <summary>
        /// keepalive-timeout: Keepalive timeout for authorized HotSpot clients. Used to detect, that the computer of the client is alive and reachable. User is logged out, when timeout value is reached
        /// </summary>
        [TikProperty("keepalive-timeout", WinboxLabel = "Keepalive Timeout")]
        public TikValue<TikDuration?> KeepaliveTimeout { get; set; }

        /// <summary>
        /// mac-cookie-timeout: Selects mac-cookie timeout from last login or logout. Read more&gt;&gt;
        /// </summary>
        [TikProperty("mac-cookie-timeout", DefaultValue = "3d", WinboxLabel = "MAC Cookie Timeout")]
        public TikValue<TikDuration?> MacCookieTimeout { get; set; }

        /// <summary>
        /// name: Descriptive name of the profile
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikValue<string?> Name { get; set; }

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
        [TikProperty("on-login", DefaultValue = "", IsFreeText = true, WinboxLabel = "On Login")]
        public TikValue<string?> OnLogin { get; set; }

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
        [TikProperty("on-logout", DefaultValue = "", IsFreeText = true, WinboxLabel = "On Logout")]
        public TikValue<string?> OnLogout { get; set; }

        /// <summary>
        /// open-status-page
        /// Option to show status page for user authenticated with mac login method. For example to show advertisement on status page (alogin.html)
        ///  http-login - open status page only for HTTP login (includes cookie and HTTPS) 	
        ///  always - open HTTP status page in case of mac login as well
        /// </summary>
        [TikProperty("open-status-page", DefaultValue = "always", WinboxLabel = "Open Status Page")]
        public TikValue<string?> OpenStatusPage { get; set; }

        /// <summary>
        /// outgoing-filter: Name of the firewall chain applied to outgoing packets from the users of this profile, jump rule is required from built-in chain (input, forward, output) to chain=hotspot
        /// </summary>
        [TikProperty("outgoing-filter", WinboxLabel = "Outgoing Filter")]
        public TikValue<string?> OutgoingFilter { get; set; }

        /// <summary>
        /// outgoing-packet-mark: Packet mark put on outgoing packets from every user of this profile
        /// </summary>
        [TikProperty("outgoing-packet-mark", WinboxLabel = "Outgoing Packet Mark")]
        public TikValue<string?> OutgoingPacketMark { get; set; }

        /// <summary>
        /// rate-limit: Simple dynamic queue is created for user, once it logs in to the HotSpot. Rate-limitation is configured in the following form [rx-rate[/tx-rate] [rx-burst-rate[/tx-burst-rate] [rx-burst-threshold[/tx-burst-threshold] [rx-burst-time[/tx-burst-time] [priority] [rx-rate-min[/tx-rate-min]]]]. For example, to set 1M download, 512k upload for the client, rate-limit=512k/1M
        /// </summary>
        [TikProperty("rate-limit", DefaultValue = "")]
        public TikValue<string?> RateLimit { get; set; }

        /// <summary>
        /// session-timeout: Allowed session time for client.  After this time, the user is logged out unconditionally
        /// </summary>
        [TikProperty("session-timeout", DefaultValue = "0s", WinboxLabel = "Session Timeout")]
        public TikValue<TikDuration?> SessionTimeout { get; set; }

        /// <summary>
        /// shared-users: Allowed number of simultaneously logged in users with the same HotSpot username
        /// </summary>
        [TikProperty("shared-users", DefaultValue = "1", WinboxLabel = "Shared Users")]
        public TikValue<string?> SharedUsers { get; set; }

        /// <summary>
        /// status-autorefresh: HotSpot status page autorefresh interval
        /// </summary>
        [TikProperty("status-autorefresh", DefaultValue = "1m", WinboxLabel = "Status Autorefresh")]
        public TikValue<string?> StatusAutorefresh { get; set; }

        /// <summary>
        /// transparent-proxy: Use transparent HTTP proxy for the authorized users of this profile
        /// </summary>
        [TikProperty("transparent-proxy", DefaultValue = "yes", WinboxLabel = "Transparent Proxy")]
        public TikValue<bool?> TransparentProxy { get; set; }
    }
}
