using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// ip/hotspot/user
    /// 
    /// This is the menu, where client's user/password information is actually added, additional configuration options for HotSpot users are configured here as well.
    /// </summary>
    [TikEntity("ip/hotspot/user")]
    public class HotspotUser
    {
        #region Submenu classes
        /// <summary>
        /// ip/hotspot/user: User profile menu is used for common HotSpot client settings. Profiles are like User groups with the same set of settings, rate-limit, filter chain name, etc. 
        /// </summary>
        [TikEntity("ip/hotspot/user/profile")]
        public class UserProfile
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// add-mac-cookie: Allows to add mac cookie for users. Read more&gt;&gt;
            /// </summary>
            [TikProperty("add-mac-cookie", DefaultValue = "yes")]
            public bool AddMacCookie { get; set; }

            /// <summary>
            /// address-list: Name of the address list in which users IP address will be added. Useful to mark traffic per user groups for queue tree configurations.
            /// </summary>
            [TikProperty("address-list")]
            public string AddressList { get; set; }

            /// <summary>
            /// address-pool: IP pool name from which the user will get IP. When user has improper network settings configuration on the computer, HotSpot server makes translation and assigns correct IP address from the pool instead of incorrect one
            /// </summary>
            [TikProperty("address-pool", DefaultValue = "none")]
            public string/*string |none*/ AddressPool { get; set; }

            /// <summary>
            /// advertise: Enable forced advertisement popups. After certain interval specific web-page is being displayed for HotSpot users. Advertisement page might be blocked by browsers popup blockers.
            /// </summary>
            [TikProperty("advertise", DefaultValue = "no")]
            public bool Advertise { get; set; }

            /// <summary>
            /// advertise-interval: Set of intervals between advertisement popups. After the list is done, the last value is used for all further advertisements, 10 minutes
            /// </summary>
            [TikProperty("advertise-interval", DefaultValue = "30m,10m")]
            public string/*time[,time[,..]]*/ AdvertiseInterval { get; set; }

            /// <summary>
            /// advertise-timeout: How long advertisement is shown, before blocking network access for HotSpot client. Connection to Internet is not allowed, when advertisement is not shown.
            /// </summary>
            [TikProperty("advertise-timeout", DefaultValue = "1m")]
            public string/*time | immediately | never*/ AdvertiseTimeout { get; set; }

            /// <summary>
            /// advertise-url: List of URLs that is show for advertisement popups. After the last URL is used, list starts from the begining.
            /// </summary>
            [TikProperty("advertise-url")]
            public string/*string[,string[,..]]*/ AdvertiseUrl { get; set; }

            /// <summary>
            /// idle-timeout: Maximal period of inactivity for authorized HotSpot clients. Timer is counting, when there is no traffic coming from that client and going through the router, for example computer is switched off. User is logged out, dropped of the host list, the address used by the user is freed, when timeout is reached.
            /// </summary>
            [TikProperty("idle-timeout", DefaultValue = "none", UnsetOnDefault = true)]
            public string/*time | none*/ IdleTimeout { get; set; }

            /// <summary>
            /// incoming-filter: Name of the firewall chain applied to incoming packets from the users of this profile, jump rule is required from built-in chain (input, forward, output) to chain=hotspot
            /// </summary>
            [TikProperty("incoming-filter")]
            public string IncomingFilter { get; set; }

            /// <summary>
            /// incoming-packet-mark: Packet mark put on incoming packets from every user of this profile
            /// </summary>
            [TikProperty("incoming-packet-mark")]
            public string IncomingPacketMark { get; set; }

            /// <summary>
            /// keepalive-timeout: Keepalive timeout for authorized HotSpot clients. Used to detect, that the computer of the client is alive and reachable. User is logged out, when timeout value is reached
            /// </summary>
            [TikProperty("keepalive-timeout", UnsetOnDefault = true)]
            public string/*time | none*/ KeepaliveTimeout { get; set; }

            /// <summary>
            /// mac-cookie-timeout: Selects mac-cookie timeout from last login or logout. Read more&gt;&gt;
            /// </summary>
            [TikProperty("mac-cookie-timeout", DefaultValue = "3d", UnsetOnDefault = true)]
            public string/*time*/ MacCookieTimeout { get; set; }

            /// <summary>
            /// name: Descriptive name of the profile
            /// </summary>
            [TikProperty("name", IsMandatory = true)]
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
            [TikProperty("on-login", DefaultValue = "")]
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
            [TikProperty("on-logout", DefaultValue = "")]
            public string OnLogout { get; set; }

            /// <summary>
            /// open-status-page
            /// Option to show status page for user authenticated with mac login method. For example to show advertisement on status page (alogin.html)
            ///  http-login - open status page only for HTTP login (includes cookie and HTTPS) 	
            ///  always - open HTTP status page in case of mac login as well
            /// </summary>
            [TikProperty("open-status-page", DefaultValue = "always")]
            public string/*always | http-login*/ OpenStatusPage { get; set; }

            /// <summary>
            /// outgoing-filter: Name of the firewall chain applied to outgoing packets from the users of this profile, jump rule is required from built-in chain (input, forward, output) to chain=hotspot
            /// </summary>
            [TikProperty("outgoing-filter")]
            public string OutgoingFilter { get; set; }

            /// <summary>
            /// outgoing-packet-mark: Packet mark put on outgoing packets from every user of this profile
            /// </summary>
            [TikProperty("outgoing-packet-mark")]
            public string OutgoingPacketMark { get; set; }

            /// <summary>
            /// rate-limit: Simple dynamic queue is created for user, once it logs in to the HotSpot. Rate-limitation is configured in the following form [rx-rate[/tx-rate] [rx-burst-rate[/tx-burst-rate] [rx-burst-threshold[/tx-burst-threshold] [rx-burst-time[/tx-burst-time] [priority] [rx-rate-min[/tx-rate-min]]]]. For example, to set 1M download, 512k upload for the client, rate-limit=512k/1M
            /// </summary>
            [TikProperty("rate-limit", DefaultValue = "")]
            public string RateLimit { get; set; }

            /// <summary>
            /// session-timeout: Allowed session time for client.  After this time, the user is logged out unconditionally
            /// </summary>
            [TikProperty("session-timeout", DefaultValue = "0s")]
            public string/*time*/ SessionTimeout { get; set; }

            /// <summary>
            /// shared-users: Allowed number of simultaneously logged in users with the same HotSpot username
            /// </summary>
            [TikProperty("shared-users", DefaultValue = "unlimited")]
            public string SharedUsers { get; set; }

            /// <summary>
            /// status-autorefresh: HotSpot status page autorefresh interval
            /// </summary>
            [TikProperty("status-autorefresh", DefaultValue = "none")]
            public string/*time | none*/ StatusAutorefresh { get; set; }

            /// <summary>
            /// transparent-proxy: Use transparent HTTP proxy for the authorized users of this profile
            /// </summary>
            [TikProperty("transparent-proxy", DefaultValue = "yes")]
            public bool TransparentProxy { get; set; }

            /// <summary>
            /// ctor
            /// </summary>
            public UserProfile()
            {
                SharedUsers = "1";
            }
        }

        #endregion

        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// address: IP address, when specified client will get the address from the HotSpot one-to-one NAT translations. Address does not restrict HotSpot login only from this address
        /// </summary>
        [TikProperty("address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ Address { get; set; }

        /// <summary>
        /// comment: descriptive information for HotSpot user, it might be used for scripts to change parameters for specific clients
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// email: HotSpot client's e-mail, informational value for the HotSpot user
        /// </summary>
        [TikProperty("email")]
        public string Email { get; set; }

        /// <summary>
        /// limit-bytes-in: Maximal amount of bytes that can be received from the user. User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [TikProperty("limit-bytes-in", DefaultValue = "0")]
        public long LimitBytesIn { get; set; }

        /// <summary>
        /// limit-bytes-out: Maximal amount of bytes that can be transmitted from the user. User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [TikProperty("limit-bytes-out", DefaultValue = "0")]
        public long LimitBytesOut { get; set; }

        /// <summary>
        /// limit-bytes-total: (limit-bytes-in+limit-bytes-out). User is disconnected from HotSpot after the limit is reached.
        /// </summary>
        [TikProperty("limit-bytes-total", DefaultValue = "0")]
        public long LimitBytesTotal { get; set; }

        /// <summary>
        /// limit-uptime: Uptime limit for the HotSpot client, user is disconnected from HotSpot as soon as uptime is reached.
        /// </summary>
        [TikProperty("limit-uptime", DefaultValue = "0")]
        public string/*time*/ LimitUptime { get; set; }

        /// <summary>
        /// mac-address: Client is allowed to login only from the specified MAC-address. If value is  00:00:00:00:00:00, any mac address is allowed.
        /// </summary>
        [TikProperty("mac-address", DefaultValue = "00:00:00:00:00:00")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// name: HotSpot login page username, when MAC-address authentication is used name is configured as client's MAC-address
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// password: User password
        /// </summary>
        [TikProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// profile: User profile configured in  /ip hotspot user profile
        /// </summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>
        /// routes: Routes added to HotSpot gateway when client is connected. The route format dst-address gateway metric (for example, 192.168.1.0/24 192.168.0.1 1)
        /// </summary>
        [TikProperty("routes")]
        public string Routes { get; set; }

        /// <summary>
        /// server: HotSpot server's name to which user is allowed login
        /// </summary>
        [TikProperty("server", DefaultValue = "all")]
        public string/*string | all*/ Server { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// bytes-in: 
        /// </summary>
        [TikProperty("bytes-in", IsReadOnly = true)]
        public long BytesIn { get; private set; }

        /// <summary>
        /// bytes-out: 
        /// </summary>
        [TikProperty("bytes-out", IsReadOnly = true)]
        public long BytesOut { get; private set; }

        /// <summary>
        /// packets-in: 
        /// </summary>
        [TikProperty("packets-in", IsReadOnly = true)]
        public long PacketsIn { get; private set; }

        /// <summary>
        /// packets-out: 
        /// </summary>
        [TikProperty("packets-out", IsReadOnly = true)]
        public long PacketsOut { get; private set; }

        /// <summary>
        /// uptime: 
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public string Uptime { get; private set; }

        /// <summary>
        /// ctor
        /// </summary>
        public HotspotUser()
        {
            Server = "all";
        }
            
    }

}
