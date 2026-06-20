using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Tunnel
{
    /// <summary>
    /// /interface/eoip
    /// Ethernet over IP (EoIP) tunneling is a MikroTik RouterOS protocol based on GRE (RFC 1701)
    /// that creates an Ethernet tunnel between two routers on top of an IP connection.
    /// See https://help.mikrotik.com/docs/display/ROS/EoIP
    /// </summary>
    [TikEntity("/interface/eoip", IncludeDetails = true)]
    public class InterfaceEoip
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — Tunnel interface name.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>mtu — Layer3 MTU. Can be "auto" or a specific integer. Default: auto.</summary>
        [TikProperty("mtu", DefaultValue = "auto")]
        public string Mtu { get; set; }

        /// <summary>actual-mtu — Effective MTU after overhead (read-only).</summary>
        [TikProperty("actual-mtu", IsReadOnly = true)]
        public string ActualMtu { get; private set; }

        /// <summary>l2mtu — Layer2 MTU (read-only, not configurable on EoIP).</summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string L2Mtu { get; private set; }

        /// <summary>mac-address — Virtual MAC address for the EoIP interface. Use range 00:00:5E:80:00:00–00:00:5E:FF:FF:FF.</summary>
        [TikProperty("mac-address", DefaultValue = "")]
        public string/*MAC*/ MacAddress { get; set; }

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

        public enum LoopProtectMode
        {
            /// <summary>default — Use the interface default loop protection setting.</summary>
            [TikEnum("default")] Default,
            /// <summary>off — Disable loop protection.</summary>
            [TikEnum("off")] Off,
            /// <summary>on — Enable loop protection.</summary>
            [TikEnum("on")] On,
        }

        /// <summary>loop-protect — Loop protection mode. Default: default.</summary>
        /// <seealso cref="LoopProtectMode"/>
        [TikProperty("loop-protect", DefaultValue = "default")]
        public LoopProtectMode LoopProtect { get; set; }

        /// <summary>loop-protect-status — Current loop protection status (read-only).</summary>
        [TikProperty("loop-protect-status", IsReadOnly = true)]
        public string LoopProtectStatus { get; private set; }

        /// <summary>loop-protect-send-interval — How often loop protection packets are sent. Default: 5s.</summary>
        [TikProperty("loop-protect-send-interval", DefaultValue = "5s")]
        public string/*time*/ LoopProtectSendInterval { get; set; }

        /// <summary>loop-protect-disable-time — How long to disable interface when loop is detected. Default: 5m.</summary>
        [TikProperty("loop-protect-disable-time", DefaultValue = "5m")]
        public string/*time*/ LoopProtectDisableTime { get; set; }

        /// <summary>local-address — Local tunnel endpoint IP address. 0.0.0.0 means use the outgoing interface address.</summary>
        [TikProperty("local-address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ LocalAddress { get; set; }

        /// <summary>remote-address — Remote tunnel endpoint IP address. Required.</summary>
        [TikProperty("remote-address", IsMandatory = true)]
        public string/*IP*/ RemoteAddress { get; set; }

        /// <summary>tunnel-id — Unique EoIP tunnel identifier (0–65535). Must match on both endpoints. Required.</summary>
        [TikProperty("tunnel-id", IsMandatory = true)]
        public int TunnelId { get; set; }

        /// <summary>keepalive — Tunnel keepalive interval and retry count (e.g. "10s,10"). Default: 10s,10.</summary>
        [TikProperty("keepalive", DefaultValue = "10s,10")]
        public string Keepalive { get; set; }

        /// <summary>dscp — DSCP value for tunnel packets. "inherit" copies from encapsulated traffic, or 0–63.</summary>
        [TikProperty("dscp", DefaultValue = "inherit")]
        public string Dscp { get; set; }

        /// <summary>dont-fragment — DF bit handling: "no" to fragment if needed; "inherit" copies from original packet.</summary>
        [TikProperty("dont-fragment", DefaultValue = "no")]
        public string DontFragment { get; set; }

        /// <summary>clamp-tcp-mss — Adjust MSS for TCP SYN packets when they would exceed tunnel MTU. Default: yes.</summary>
        [TikProperty("clamp-tcp-mss", DefaultValue = "yes")]
        public bool ClampTcpMss { get; set; }

        /// <summary>allow-fast-path — Allow FastPath processing. Must be disabled when using IPsec. Default: yes.</summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes")]
        public bool AllowFastPath { get; set; }

        /// <summary>ipsec-secret — Pre-shared key for dynamic IPsec peer at the remote address.</summary>
        [TikProperty("ipsec-secret", DefaultValue = "")]
        public string IpsecSecret { get; set; }

        /// <summary>running — Whether the tunnel is running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — Short description of the tunnel.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
