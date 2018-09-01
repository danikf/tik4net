using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ppp
{
    /// <summary>
    /// ppp/profile: PPP profiles are used to define default values for user access records stored under /ppp secret submenu. Settings in /ppp secret User Database override corresponding /ppp profile settings except that single IP addresses always take precedence over IP pools when specified as local-address or remote-address parameters. 
    /// https://wiki.mikrotik.com/wiki/Manual:PPP_AAA
    /// </summary>
    [TikEntity("ppp/profile")]
    public class PppProfile
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// address-list:  Address list name to which ppp assigned address will be added.
        /// </summary>
        [TikProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// bridge: Name of the  bridge interface to which ppp interface will be added as slave port. Both tunnel end point (server and client) must be in bridge in order to make this work.
        /// </summary>
        [TikProperty("bridge")]
        public string Bridge { get; set; }

        /// <summary>
        /// change-tcp-mss
        /// Modifies connection MSS settings:
        ///  yes - adjust connection MSS value 
        ///  no - do not adjust connection MSS value 
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile
        /// </summary>
        [TikProperty("change-tcp-mss", DefaultValue = "default")]
        public string/*yes | no | default*/ ChangeTcpMss { get; set; }

        /// <summary>
        /// comment: 
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// dhcpv6-pd-pool: Name of the  IPv6 pool which will be used by dynamically created  DHCPv6-PD server when client connects.  Read more &gt;&gt;
        /// </summary>
        [TikProperty("dhcpv6-pd-pool")]
        public string Dhcpv6PdPool { get; set; }

        /// <summary>
        /// dns-server: IP address of the DNS server that is supplied to ppp clients
        /// </summary>
        [TikProperty("dns-server")]
        public string/*IP*/ DnsServer { get; set; }

        /// <summary>
        /// idle-timeout: Specifies the amount of time after which the link will be terminated if there are no activity present. Timeout is not set by default
        /// </summary>
        [TikProperty("idle-timeout")]
        public string/*time*/ IdleTimeout { get; set; }

        /// <summary>
        /// incoming-filter: Firewall chain name for incoming packets. Specified chain gets control for each packet coming from the client. The ppp chain should be manually added and rules with action=jump jump-target=ppp should be added to other relevant chains in order for this feature to work. For more information look at the  examples section
        /// </summary>
        [TikProperty("incoming-filter")]
        public string IncomingFilter { get; set; }

        /// <summary>
        /// local-address: Tunnel address or name of the  pool from which address is assigned to ppp interface locally.
        /// </summary>
        [TikProperty("local-address")]
        public string/*IP address | pool*/ LocalAddress { get; set; }

        /// <summary>
        /// name: PPP profile name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// only-one
        /// Defines whether a user is allowed to have more than one connection at a time
        ///  yes - a user is not allowed to have more than one connection at a time 
        ///  no - the user is allowed to have more than one connection at a time 
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile
        /// </summary>
        [TikProperty("only-one", DefaultValue = "default")]
        public string/*yes | no | default*/ OnlyOne { get; set; }

        /// <summary>
        /// outgoing-filter: Firewall chain name for outgoing packets. Specified chain gets control for each packet going to the client. The ppp chain should be manually added and rules with action=jump jump-target=ppp should be added to other relevant chains in order for this feature to work. For more information look at the Examples section.
        /// </summary>
        [TikProperty("outgoing-filter")]
        public string OutgoingFilter { get; set; }

        /// <summary>
        /// rate-limit: Rate limitation in form of rx-rate[/tx-rate] [rx-burst-rate[/tx-burst-rate] [rx-burst-threshold[/tx-burst-threshold] [rx-burst-time[/tx-burst-time] [priority] [rx-rate-min[/tx-rate-min]]]] from the point of view of the router (so "rx" is client upload, and "tx" is client download). All rates are measured in bits per second, unless followed by optional 'k' suffix (kilobits per second) or 'M' suffix (megabits per second). If tx-rate is not specified, rx-rate serves as tx-rate too. The same applies for tx-burst-rate, tx-burst-threshold and tx-burst-time. If both rx-burst-threshold and tx-burst-threshold are not specified (but burst-rate is specified), rx-rate and tx-rate are used as burst thresholds. If both rx-burst-time and tx-burst-time are not specified, 1s is used as default. Priority takes values 1..8, where 1 implies the highest priority, but 8 - the lowest. If rx-rate-min and tx-rate-min are not specified rx-rate and tx-rate values are used. The rx-rate-min and tx-rate-min values can not exceed rx-rate and tx-rate values.
        /// </summary>
        [TikProperty("rate-limit")]
        public string RateLimit { get; set; }

        /// <summary>
        /// remote-address: Tunnel address or name of the  pool from which address is assigned to remote ppp interface.
        /// </summary>
        [TikProperty("remote-address")]
        public string/*IP*/ RemoteAddress { get; set; }

        /// <summary>
        /// remote-ipv6-prefix-pool: Assign prefix from IPv6 pool to the client and install corresponding IPv6 route.
        /// </summary>
        [TikProperty("remote-ipv6-prefix-pool", DefaultValue = "none")]
        public string/*string | none*/ RemoteIpv6PrefixPool { get; set; }

        /// <summary>
        /// session-timeout: Maximum time the connection can stay up. By default no time limit is set.
        /// </summary>
        [TikProperty("session-timeout")]
        public string/*time*/ SessionTimeout { get; set; }

        /// <summary>
        /// use-compression
        /// Specifies whether to use data compression or not.
        ///  yes - enable data compression 
        ///  no - disable data compression
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile 
        /// This setting does not affect OVPN tunnels.
        /// </summary>
        [TikProperty("use-compression", DefaultValue = "default")]
        public string/*yes | no | default*/ UseCompression { get; set; }

        /// <summary>
        /// use-encryption
        /// Specifies whether to use data encryption or not.
        ///  yes - enable data encryption 
        ///  no - disable data encryption
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile 
        ///  require - explicitly requires encryption
        /// This setting does not work on OVPN and SSTP tunnels.
        /// </summary>
        [TikProperty("use-encryption", DefaultValue = "default")]
        public string/*yes | no | default | require*/ UseEncryption { get; set; }

        /// <summary>
        /// use-ipv6
        /// Specifies whether to allow IPv6. By default is enabled if IPv6 package is installed.
        ///  yes - enable IPv6 support
        ///  no - disable IPv6 support
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile 
        ///  require - explicitly requires IPv6 support
        /// </summary>
        [TikProperty("use-ipv6", DefaultValue = "default")]
        public string/*yes | no | default | require*/ UseIpv6 { get; set; }

        /// <summary>
        /// use-mpls
        /// Specifies whether to allow MPLS over PPP.
        ///  yes - enable MPLS support
        ///  no - disable MPLS support
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile 
        ///  require - explicitly requires MPLS support
        /// </summary>
        [TikProperty("use-mpls", DefaultValue = "default")]
        public string/*yes | no | default | require*/ UseMpls { get; set; }

        /// <summary>
        /// use-vj-compression
        /// Specifies whether to use Van Jacobson header compression algorithm.
        ///  yes - enable Van Jacobson header compression
        ///  no - disable Van Jacobson header compression 
        ///  default - derive this value from the interface default profile; same as no if this is the interface default profile
        /// </summary>
        [TikProperty("use-vj-compression", DefaultValue = "default")]
        public string/*yes | no | default*/ UseVjCompression { get; set; }

        /// <summary>
        /// on-up
        /// Execute script on user login-event. These are available variables that are accessible for the event script:
        ///  user
        ///  local-address
        ///  remote-address
        ///  caller-id
        ///  called-id
        ///  interface
        /// </summary>
        [TikProperty("on-up")]
        public string/*script*/ OnUp { get; set; }

        /// <summary>
        /// on-down: Execute script on user logging off. See on-up for more details
        /// </summary>
        [TikProperty("on-down")]
        public string/*script*/ OnDown { get; set; }

        /// <summary>
        /// wins-server: IP address of the WINS server to supply to Windows clients
        /// </summary>
        [TikProperty("wins-server")]
        public string/*IP address*/ WinsServer { get; set; }
    }

}
