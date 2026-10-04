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
        public string? Id { get; private set; }

        /// <summary>name — Tunnel interface name.</summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>mtu — Layer3 MTU. Can be "auto" or a specific integer. Default: auto.</summary>
        [TikProperty("mtu", DefaultValue = "auto", WinboxLabel = "MTU")]
        public TikField<string?> Mtu { get; set; }

        /// <summary>actual-mtu — Effective MTU after overhead (read-only).</summary>
        [TikProperty("actual-mtu", IsReadOnly = true, WinboxLabel = "Actual MTU")]
        public TikField<string?> ActualMtu { get; private set; }

        /// <summary>l2mtu — Layer2 MTU (read-only, not configurable on EoIP).</summary>
        [TikProperty("l2mtu", IsReadOnly = true, WinboxLabel = "L2 MTU")]
        public TikField<string?> L2Mtu { get; private set; }

        /// <summary>mac-address — Virtual MAC address for the EoIP interface. Use range 00:00:5E:80:00:00–00:00:5E:FF:FF:FF.</summary>
        [TikProperty("mac-address", DefaultValue = "", WinboxLabel = "MAC Address")]
        public TikField<string?> MacAddress { get; set; }

        /// <summary>arp — the Address Resolution Protocol setting for the EoIP interface.</summary>
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
            /// <summary>local-proxy-arp — Interface performs proxy ARP and answers back out of
            /// the same interface, so hosts that cannot reach each other directly still resolve.</summary>
            [TikEnum("local-proxy-arp")] LocalProxyArp,
        }

        /// <summary>arp — Address Resolution Protocol setting. Default: enabled.</summary>
        /// <seealso cref="ArpMode"/>
        [TikProperty("arp", DefaultValue = "enabled", WinboxLabel = "ARP")]
        public TikField<ArpMode?> Arp { get; set; }

        /// <summary>arp-timeout — How long ARP entries are kept. Default: auto.</summary>
        [TikProperty("arp-timeout", DefaultValue = "auto", WinboxLabel = "ARP Timeout")]
        public TikField<TikDuration?> ArpTimeout { get; set; }

        /// <summary>loop-protect — the loop protection mode for the EoIP interface.</summary>
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
        [TikProperty("loop-protect", DefaultValue = "default", WinboxLabel = "Loop Protect")]
        public TikField<LoopProtectMode?> LoopProtect { get; set; }

        /// <summary>loop-protect-status — Current loop protection status (read-only).</summary>
        [TikProperty("loop-protect-status", IsReadOnly = true, WinboxLabel = "loop-protect: Status")]
        public TikField<string?> LoopProtectStatus { get; private set; }

        /// <summary>loop-protect-send-interval — How often loop protection packets are sent. Default: 5s.</summary>
        [TikProperty("loop-protect-send-interval", DefaultValue = "5s", WinboxLabel = "loop-protect: Send Interval")]
        public TikField<TikDuration?> LoopProtectSendInterval { get; set; }

        /// <summary>loop-protect-disable-time — How long to disable interface when loop is detected. Default: 5m.</summary>
        [TikProperty("loop-protect-disable-time", DefaultValue = "5m", WinboxLabel = "loop-protect: Disable Time")]
        public TikField<TikDuration?> LoopProtectDisableTime { get; set; }

        /// <summary>local-address — Local tunnel endpoint IP address. 0.0.0.0 means use the outgoing interface address.</summary>
        [TikProperty("local-address", DefaultValue = "0.0.0.0", WinboxLabel = "Local Address")]
        public TikField<string?> LocalAddress { get; set; }

        /// <summary>remote-address — Remote tunnel endpoint IP address. Required.</summary>
        [TikProperty("remote-address", WinboxLabel = "Remote Address")]
        public TikField<string?> RemoteAddress { get; set; }

        /// <summary>tunnel-id — Unique EoIP tunnel identifier (0–65535). Must match on both endpoints. Required.</summary>
        [TikProperty("tunnel-id", WinboxLabel = "Tunnel ID")]
        public TikField<int?> TunnelId { get; set; }

        /// <summary>keepalive — Tunnel keepalive interval and retry count (e.g. "10s,10"). Default: 10s,10.</summary>
        [TikProperty("keepalive", DefaultValue = "10s,10", WinboxLabel = "Keepalive")]
        public TikField<string?> Keepalive { get; set; }

        /// <summary>dscp — DSCP value for tunnel packets. "inherit" copies from encapsulated traffic, or 0–63.</summary>
        [TikProperty("dscp", DefaultValue = "inherit", WinboxLabel = "DSCP")]
        public TikField<string?> Dscp { get; set; }

        /// <summary>dont-fragment — DF bit handling: "no" to fragment if needed; "inherit" copies from original packet.</summary>
        [TikProperty("dont-fragment", DefaultValue = "no", WinboxLabel = "Dont Fragment")]
        public TikField<string?> DontFragment { get; set; }

        /// <summary>clamp-tcp-mss — Adjust MSS for TCP SYN packets when they would exceed tunnel MTU. Default: yes.</summary>
        [TikProperty("clamp-tcp-mss", DefaultValue = "yes", WinboxLabel = "Clamp TCP MSS")]
        public TikField<bool?> ClampTcpMss { get; set; }

        /// <summary>allow-fast-path — Allow FastPath processing. Must be disabled when using IPsec. Default: yes.</summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes", WinboxLabel = "Allow Fast Path")]
        public TikField<bool?> AllowFastPath { get; set; }

        /// <summary>ipsec-secret — Pre-shared key for dynamic IPsec peer at the remote address.</summary>
        [TikProperty("ipsec-secret", DefaultValue = "", IsSensitive = true, WinboxLabel = "IPsec Secret")]
        public TikField<string?> IpsecSecret { get; set; }

        /// <summary>running — Whether the tunnel is running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true, WinboxLabel = "running")]
        public TikField<bool?> Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>comment — Short description of the tunnel.</summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string? ToString() => Name.Value;
    }
}
