using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Tunnel
{
    /// <summary>
    /// /interface/vxlan
    /// Virtual eXtensible Local Area Network (VXLAN) is a tunneling protocol that extends VLAN
    /// identifiers to 24 bits, enabling Layer 2 overlays across Layer 3 networks via UDP.
    /// Requires RouterOS 7.1+.
    /// See https://help.mikrotik.com/docs/display/ROS/VXLAN
    /// </summary>
    [TikEntity("/interface/vxlan", IncludeDetails = true)]
    public class InterfaceVxlan
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — VXLAN interface name.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>mtu — Layer3 MTU in bytes. Default: 1500. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("mtu", DefaultValue = "0")]
        public int Mtu { get; set; }

        /// <summary>l2mtu — Layer2 MTU (read-only).</summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string L2Mtu { get; private set; }

        /// <summary>mac-address — MAC address of the VXLAN interface (auto-assigned or manually set).</summary>
        [TikProperty("mac-address", DefaultValue = "")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>vni — VXLAN Network Identifier (1–16777216). Required.</summary>
        [TikProperty("vni", IsMandatory = true)]
        public int Vni { get; set; }

        /// <summary>port — UDP destination port for VXLAN packets. Default: 4789. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("port", DefaultValue = "0")]
        public int Port { get; set; }

        /// <summary>local-address — Local source IP address for VXLAN packets.</summary>
        [TikProperty("local-address", DefaultValue = "")]
        public string/*IP*/ LocalAddress { get; set; }

        /// <summary>group — Multicast group address for BUM (Broadcast, Unknown-unicast, Multicast) traffic between VTEPs.</summary>
        [TikProperty("group", DefaultValue = "")]
        public string/*IP*/ Group { get; set; }

        /// <summary>interface — Interface to use for multicast forwarding (used together with group).</summary>
        [TikProperty("interface", DefaultValue = "")]
        public string Interface { get; set; }

        public enum ArpMode
        {
            /// <summary>enabled — Interface uses ARP.</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — Interface will not use ARP.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>local-proxy-arp — ARP proxy only for local subnet entries.</summary>
            [TikEnum("local-proxy-arp")] LocalProxyArp,
            /// <summary>proxy-arp — Full ARP proxy.</summary>
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

        /// <summary>vtep-vrf — VRF table used for VTEP listening and connections. Default: main.</summary>
        [TikProperty("vtep-vrf", DefaultValue = "main")]
        public string VtepVrf { get; set; }

        public enum VtepsIpVersionType
        {
            /// <summary>ipv4 — Use IPv4 for static VTEP connections.</summary>
            [TikEnum("ipv4")] Ipv4,
            /// <summary>ipv6 — Use IPv6 for static VTEP connections.</summary>
            [TikEnum("ipv6")] Ipv6,
        }

        /// <summary>vteps-ip-version — IP version for static VTEP connections. Default: ipv4.</summary>
        /// <seealso cref="VtepsIpVersionType"/>
        [TikProperty("vteps-ip-version", DefaultValue = "ipv4")]
        public VtepsIpVersionType VtepsIpVersion { get; set; }

        /// <summary>dont-fragment — DF flag in outer IPv4 header. Default: auto.</summary>
        [TikProperty("dont-fragment", DefaultValue = "auto")]
        public string DontFragment { get; set; }

        /// <summary>ttl — TTL value in outgoing VXLAN packets. "auto" uses the routing table value. Default: auto.</summary>
        [TikProperty("ttl", DefaultValue = "auto")]
        public string Ttl { get; set; }

        /// <summary>max-fdb-size — Maximum number of MAC entries in the forwarding database. Default: 4096. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("max-fdb-size", DefaultValue = "0")]
        public int MaxFdbSize { get; set; }

        /// <summary>learning — Dynamically learn MAC addresses and remote VTEP IPs. Default: yes.</summary>
        [TikProperty("learning", DefaultValue = "yes")]
        public bool Learning { get; set; }

        /// <summary>checksum — Calculate UDP checksum in outer packets. Default: no.</summary>
        [TikProperty("checksum", DefaultValue = "no")]
        public bool Checksum { get; set; }

        public enum RemCsumType
        {
            /// <summary>none — No remote checksum offload.</summary>
            [TikEnum("none")] None,
            /// <summary>rx — Remote checksum offload on receive.</summary>
            [TikEnum("rx")] Rx,
            /// <summary>tx — Remote checksum offload on transmit.</summary>
            [TikEnum("tx")] Tx,
            /// <summary>both — Remote checksum offload on both directions.</summary>
            [TikEnum("both")] Both,
        }

        /// <summary>rem-csum — Remote Checksum Offload setting. Default: none.</summary>
        /// <seealso cref="RemCsumType"/>
        [TikProperty("rem-csum", DefaultValue = "none")]
        public RemCsumType RemCsum { get; set; }

        /// <summary>hw — Enable hardware offloading on compatible devices. Default: yes.</summary>
        [TikProperty("hw", DefaultValue = "yes")]
        public bool Hw { get; set; }

        /// <summary>allow-fast-path — Allow FastPath processing. Default: yes.</summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes")]
        public bool AllowFastPath { get; set; }

        /// <summary>bridge — Bridge interface to add this VXLAN interface as a slave port.</summary>
        [TikProperty("bridge", DefaultValue = "")]
        public string Bridge { get; set; }

        /// <summary>bridge-pvid — Port VLAN ID when used with a bridge with VLAN filtering. Default: 1. DefaultValue="0" prevents sending 0 on add.</summary>
        [TikProperty("bridge-pvid", DefaultValue = "0")]
        public int BridgePvid { get; set; }

        /// <summary>hw-offloaded — Whether hardware offloading is active (read-only).</summary>
        [TikProperty("hw-offloaded", IsReadOnly = true)]
        public bool HwOffloaded { get; private set; }

        /// <summary>running — Whether the VXLAN interface is running (read-only).</summary>
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
