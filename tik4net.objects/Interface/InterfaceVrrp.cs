using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/vrrp
    /// Virtual Router Redundancy Protocol (VRRP) provides router redundancy by combining a number
    /// of routers into a logical group called a Virtual Router. RouterOS supports VRRPv2 (RFC 3768)
    /// and VRRPv3 (RFC 5798).
    /// See https://help.mikrotik.com/docs/display/ROS/VRRP
    /// </summary>
    [TikEntity("/interface/vrrp", IncludeDetails = true)]
    public class InterfaceVrrp
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — Name of the VRRP interface.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>mtu — Layer3 MTU size (read-only, derived from parent interface).</summary>
        [TikProperty("mtu", IsReadOnly = true)]
        public string Mtu { get; private set; }

        /// <summary>mac-address — Virtual MAC address auto-generated from vrid (read-only).</summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; private set; }

        /// <summary>interface — Physical interface on which VRRP runs. Required.</summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>vrid — Virtual Router Identifier (1–255). Default: 1. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("vrid", DefaultValue = "0")]
        public int Vrid { get; set; }

        /// <summary>priority — Election priority (1–254). 255 is reserved for the IP owner. Default: 100. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("priority", DefaultValue = "0")]
        public int Priority { get; set; }

        /// <summary>interval — How often the VRRP master sends advertisement packets. Default: 1s.</summary>
        [TikProperty("interval", DefaultValue = "1s")]
        public string/*time*/ Interval { get; set; }

        /// <summary>preemption-mode — Whether a higher-priority backup immediately takes over master role.</summary>
        [TikProperty("preemption-mode", DefaultValue = "yes")]
        public bool PreemptionMode { get; set; }

        public enum AuthenticationMode
        {
            /// <summary>none — No authentication.</summary>
            [TikEnum("none")] None,
            /// <summary>simple — Simple plain-text password authentication.</summary>
            [TikEnum("simple")] Simple,
            /// <summary>ah — HMAC-MD5 authentication.</summary>
            [TikEnum("ah")] Ah,
        }

        /// <summary>authentication — Method used to authenticate VRRP packets. Default: none.</summary>
        /// <seealso cref="AuthenticationMode"/>
        [TikProperty("authentication", DefaultValue = "none")]
        public AuthenticationMode Authentication { get; set; }

        /// <summary>password — Password used for VRRP packet authentication.</summary>
        [TikProperty("password", DefaultValue = "")]
        public string Password { get; set; }

        public enum ArpMode
        {
            /// <summary>enabled — Interface uses ARP.</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — Interface will not use ARP.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>proxy-arp — Interface uses the ARP proxy feature.</summary>
            [TikEnum("proxy-arp")] ProxyArp,
            /// <summary>reply-only — Interface only replies to requests matching static ARP entries.</summary>
            [TikEnum("reply-only")] ReplyOnly,
        }

        /// <summary>arp — Address Resolution Protocol setting. Default: enabled.</summary>
        /// <seealso cref="ArpMode"/>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>arp-timeout — How long ARP entries are kept. Default: auto.</summary>
        [TikProperty("arp-timeout", DefaultValue = "auto")]
        public string/*time*/ ArpTimeout { get; set; }

        /// <summary>version — VRRP protocol version (2 or 3). Default: 3. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("version", DefaultValue = "0")]
        public int Version { get; set; }

        public enum V3ProtocolType
        {
            /// <summary>ipv4 — Use IPv4 for VRRPv3.</summary>
            [TikEnum("ipv4")] Ipv4,
            /// <summary>ipv6 — Use IPv6 for VRRPv3.</summary>
            [TikEnum("ipv6")] Ipv6,
        }

        /// <summary>v3-protocol — IP protocol used when version=3. Default: ipv4.</summary>
        /// <seealso cref="V3ProtocolType"/>
        [TikProperty("v3-protocol", DefaultValue = "ipv4")]
        public V3ProtocolType V3Protocol { get; set; }

        /// <summary>on-backup — Script executed when transitioning to backup state.</summary>
        [TikProperty("on-backup", DefaultValue = "")]
        public string OnBackup { get; set; }

        /// <summary>on-master — Script executed when becoming master.</summary>
        [TikProperty("on-master", DefaultValue = "")]
        public string OnMaster { get; set; }

        /// <summary>on-fail — Script executed during failure.</summary>
        [TikProperty("on-fail", DefaultValue = "")]
        public string OnFail { get; set; }

        /// <summary>group-authority — VRRP interface that acts as group authority, controlling this instance's state.</summary>
        [TikProperty("group-authority", DefaultValue = "")]
        public string GroupAuthority { get; set; }

        /// <summary>sync-connection-tracking — Synchronizes connection tracking entries from master to backup.</summary>
        [TikProperty("sync-connection-tracking", DefaultValue = "no")]
        public bool SyncConnectionTracking { get; set; }

        public enum ConnectionTrackingModeType
        {
            /// <summary>passive-active — Passive on backup, active on master.</summary>
            [TikEnum("passive-active")] PassiveActive,
            /// <summary>active-active — Connection tracking active on all nodes.</summary>
            [TikEnum("active-active")] ActiveActive,
        }

        /// <summary>connection-tracking-mode — How connection tracking synchronizes across VRRP nodes. Default: passive-active.</summary>
        /// <seealso cref="ConnectionTrackingModeType"/>
        [TikProperty("connection-tracking-mode", DefaultValue = "passive-active")]
        public ConnectionTrackingModeType ConnectionTrackingMode { get; set; }

        /// <summary>connection-tracking-port — UDP port used for connection tracking synchronization. Default: 8275. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("connection-tracking-port", DefaultValue = "0")]
        public int ConnectionTrackingPort { get; set; }

        /// <summary>remote-address — Peer router IP address for connection tracking synchronization.</summary>
        [TikProperty("remote-address", DefaultValue = "")]
        public string/*IP*/ RemoteAddress { get; set; }

        /// <summary>invalid — Whether the VRRP configuration is invalid (read-only).</summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>running — Whether the VRRP interface is running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — Short description of the interface.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
