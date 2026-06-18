using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/raw
    /// Raw firewall rules operate on the lowest layer — before connection tracking — and allow
    /// high-performance packet filtering or bypass of the conntrack engine (notrack action).
    /// Chains: prerouting (all incoming), output (locally originated).
    /// </summary>
    [TikEntity("/ip/firewall/raw", IncludeDetails = true, IsOrdered = true, IncludeCliStats = true)]
    public class FirewallRaw
    {
        /// <summary>
        /// Action type for raw firewall rules — <see cref="FirewallRaw.Action"/>
        /// </summary>
        public enum ActionType
        {
            /// <summary>
            /// accept - accept the packet. Packet is not passed to the next firewall rule.
            /// </summary>
            [TikEnum("accept")]
            Accept,

            /// <summary>
            /// add-dst-to-address-list - add destination address to address list specified by address-list parameter.
            /// </summary>
            [TikEnum("add-dst-to-address-list")]
            AddDstToAddressList,

            /// <summary>
            /// add-src-to-address-list - add source address to address list specified by address-list parameter.
            /// </summary>
            [TikEnum("add-src-to-address-list")]
            AddSrcToAddressList,

            /// <summary>
            /// drop - silently drop the packet.
            /// </summary>
            [TikEnum("drop")]
            Drop,

            /// <summary>
            /// jump - jump to the user defined chain specified by the value of jump-target parameter.
            /// </summary>
            [TikEnum("jump")]
            Jump,

            /// <summary>
            /// log - add a message to the system log. After packet is matched it is passed to the next rule.
            /// </summary>
            [TikEnum("log")]
            Log,

            /// <summary>
            /// notrack - disable connection tracking for this packet (bypass conntrack). Useful for high-throughput flows.
            /// </summary>
            [TikEnum("notrack")]
            Notrack,

            /// <summary>
            /// passthrough - ignore this rule and go to next one (useful for statistics).
            /// </summary>
            [TikEnum("passthrough")]
            Passthrough,

            /// <summary>
            /// return - pass control back to the chain from where the jump took place.
            /// </summary>
            [TikEnum("return")]
            Return,
        }

        /// <summary>
        /// Built-in raw chains — <see cref="FirewallRaw.Chain"/>
        /// </summary>
        public static class ChainType
        {
            /// <summary>
            /// prerouting - processes all packets entering the router, before routing decision.
            /// </summary>
            public const string Prerouting = "prerouting";

            /// <summary>
            /// output - processes packets originated from the router itself.
            /// </summary>
            public const string Output = "output";
        }

        /// <summary>
        /// .id: primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// action: Action to take if the packet is matched by the rule.
        /// <seealso cref="ActionType"/>
        /// </summary>
        [TikProperty("action", DefaultValue = "accept")]
        public ActionType Action { get; set; }

        /// <summary>
        /// address-list: Name of the address list used when action is add-dst-to-address-list or add-src-to-address-list.
        /// </summary>
        [TikProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// address-list-timeout: Time interval after which the address will be removed from the address list.
        /// Value 00:00:00 leaves the address in the list forever.
        /// </summary>
        [TikProperty("address-list-timeout", DefaultValue = "00:00:00")]
        public string/*time*/ AddressListTimeout { get; set; }

        /// <summary>
        /// chain: Specifies to which chain the rule is added. Use built-in prerouting/output or a custom name.
        /// <seealso cref="ChainType"/>
        /// </summary>
        [TikProperty("chain")]
        public string/*name*/ Chain { get; set; }

        /// <summary>
        /// comment: Descriptive comment for the rule.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// content: Match packets that contain the specified text.
        /// </summary>
        [TikProperty("content", UnsetOnDefault = true)]
        public string Content { get; set; }

        /// <summary>
        /// dscp: Matches DSCP IP header field.
        /// </summary>
        [TikProperty("dscp", UnsetOnDefault = true)]
        public int Dscp { get; set; }

        /// <summary>
        /// dst-address: Matches packets whose destination equals the specified IP or falls into the specified IP range.
        /// </summary>
        [TikProperty("dst-address", UnsetOnDefault = true)]
        public string DstAddress { get; set; }

        /// <summary>
        /// dst-address-list: Matches destination address of a packet against a user-defined address list.
        /// </summary>
        [TikProperty("dst-address-list", UnsetOnDefault = true)]
        public string/*name*/ DstAddressList { get; set; }

        /// <summary>
        /// dst-address-type: Matches destination address type (unicast, local, broadcast, multicast).
        /// </summary>
        [TikProperty("dst-address-type", UnsetOnDefault = true)]
        public string DstAddressType { get; set; }

        /// <summary>
        /// dst-limit: Matches packets until a given rate (per-flow) is exceeded.
        /// </summary>
        [TikProperty("dst-limit", UnsetOnDefault = true)]
        public string DstLimit { get; set; }

        /// <summary>
        /// dst-port: List of destination port numbers or port number ranges. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("dst-port", UnsetOnDefault = true)]
        public string DstPort { get; set; }

        /// <summary>
        /// fragment: Matches fragmented packets (not the first fragment).
        /// </summary>
        [TikProperty("fragment", UnsetOnDefault = true)]
        public bool Fragment { get; set; }

        /// <summary>
        /// hotspot: Matches packets in a HotSpot scenario by the specified attribute.
        /// </summary>
        [TikProperty("hotspot", UnsetOnDefault = true)]
        public string Hotspot { get; set; }

        /// <summary>
        /// icmp-options: Matches ICMP type:code fields.
        /// </summary>
        [TikProperty("icmp-options", UnsetOnDefault = true)]
        public string IcmpOptions { get; set; }

        /// <summary>
        /// in-bridge-port: Actual interface the packet has entered the router when the incoming interface is a bridge.
        /// </summary>
        [TikProperty("in-bridge-port", UnsetOnDefault = true)]
        public string/*name*/ InBridgePort { get; set; }

        /// <summary>
        /// in-bridge-port-list: Matches in-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("in-bridge-port-list", UnsetOnDefault = true)]
        public string/*name*/ InBridgePortList { get; set; }

        /// <summary>
        /// in-interface: Interface the packet has entered the router.
        /// </summary>
        [TikProperty("in-interface", UnsetOnDefault = true)]
        public string/*name*/ InInterface { get; set; }

        /// <summary>
        /// in-interface-list: Matches in-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("in-interface-list", UnsetOnDefault = true)]
        public string/*name*/ InInterfaceList { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet (VLAN, WMM, MPLS EXP).
        /// </summary>
        [TikProperty("ingress-priority", UnsetOnDefault = true)]
        public int IngressPriority { get; set; }

        /// <summary>
        /// ipsec-policy: Matches the policy used by IPsec. Format: direction,policy.
        /// </summary>
        [TikProperty("ipsec-policy", UnsetOnDefault = true)]
        public string IpsecPolicy { get; set; }

        /// <summary>
        /// ipv4-options: Matches IPv4 header options (any, loose-source-routing, record-route, router-alert, etc.).
        /// </summary>
        [TikProperty("ipv4-options", UnsetOnDefault = true)]
        public string Ipv4Options { get; set; }

        /// <summary>
        /// jump-target: Name of the target chain to jump to. Applicable only if action=jump.
        /// </summary>
        [TikProperty("jump-target")]
        public string/*name*/ JumpTarget { get; set; }

        /// <summary>
        /// limit: Matches packets at a limited rate. Parameters: count[/time],burst.
        /// </summary>
        [TikProperty("limit", UnsetOnDefault = true)]
        public string Limit { get; set; }

        /// <summary>
        /// log: Whether to log matched packets (shorthand flag; use action=log for full log action).
        /// </summary>
        [TikProperty("log", DefaultValue = "no")]
        public bool Log { get; set; }

        /// <summary>
        /// log-prefix: Adds specified text at the beginning of every log message. Applicable if action=log or log=yes.
        /// </summary>
        [TikProperty("log-prefix")]
        public string LogPrefix { get; set; }

        /// <summary>
        /// nth: Matches every nth packet.
        /// </summary>
        [TikProperty("nth", UnsetOnDefault = true)]
        public string Nth { get; set; }

        /// <summary>
        /// out-bridge-port: Actual interface the packet is leaving through when it is a bridge.
        /// </summary>
        [TikProperty("out-bridge-port", UnsetOnDefault = true)]
        public string/*name*/ OutBridgePort { get; set; }

        /// <summary>
        /// out-bridge-port-list: Matches out-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("out-bridge-port-list", UnsetOnDefault = true)]
        public string/*name*/ OutBridgePortList { get; set; }

        /// <summary>
        /// out-interface: Interface the packet is leaving the router through.
        /// </summary>
        [TikProperty("out-interface", UnsetOnDefault = true)]
        public string/*name*/ OutInterface { get; set; }

        /// <summary>
        /// out-interface-list: Matches out-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("out-interface-list", UnsetOnDefault = true)]
        public string/*name*/ OutInterfaceList { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with a particular packet mark.
        /// </summary>
        [TikProperty("packet-mark", UnsetOnDefault = true)]
        public string PacketMark { get; set; }

        /// <summary>
        /// packet-size: Matches packets of specified size or size range in bytes.
        /// </summary>
        [TikProperty("packet-size", UnsetOnDefault = true)]
        public string PacketSize { get; set; }

        /// <summary>
        /// per-connection-classifier: PCC matcher divides traffic into equal streams.
        /// </summary>
        [TikProperty("per-connection-classifier", UnsetOnDefault = true)]
        public string PerConnectionClassifier { get; set; }

        /// <summary>
        /// port: Matches if any (source or destination) port matches the specified list. Applicable only for TCP/UDP.
        /// </summary>
        [TikProperty("port", UnsetOnDefault = true)]
        public string Port { get; set; }

        /// <summary>
        /// priority: Matches packet priority (VLAN or WMM priority tag).
        /// </summary>
        [TikProperty("priority", UnsetOnDefault = true)]
        public string Priority { get; set; }

        /// <summary>
        /// protocol: Matches particular IP protocol specified by protocol name or number.
        /// </summary>
        [TikProperty("protocol", UnsetOnDefault = true)]
        public string Protocol { get; set; }

        /// <summary>
        /// psd: Attempts to detect TCP and UDP port scans.
        /// Format: WeightThreshold, DelayThreshold, LowPortWeight, HighPortWeight.
        /// </summary>
        [TikProperty("psd", UnsetOnDefault = true)]
        public string Psd { get; set; }

        /// <summary>
        /// random: Matches packets randomly with given probability.
        /// </summary>
        [TikProperty("random", UnsetOnDefault = true)]
        public string Random { get; set; }

        /// <summary>
        /// src-address: Matches packets whose source equals the specified IP or falls into the specified IP range.
        /// </summary>
        [TikProperty("src-address", UnsetOnDefault = true)]
        public string SrcAddress { get; set; }

        /// <summary>
        /// src-address-list: Matches source address of a packet against a user-defined address list.
        /// </summary>
        [TikProperty("src-address-list", UnsetOnDefault = true)]
        public string/*name*/ SrcAddressList { get; set; }

        /// <summary>
        /// src-address-type: Matches source address type (unicast, local, broadcast, multicast).
        /// </summary>
        [TikProperty("src-address-type", UnsetOnDefault = true)]
        public string SrcAddressType { get; set; }

        /// <summary>
        /// src-mac-address: Matches source MAC address of the packet.
        /// </summary>
        [TikProperty("src-mac-address", UnsetOnDefault = true)]
        public string/*MAC*/ SrcMacAddress { get; set; }

        /// <summary>
        /// src-port: List of source ports and ranges. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("src-port", UnsetOnDefault = true)]
        public string SrcPort { get; set; }

        /// <summary>
        /// tcp-flags: Matches specified TCP flags (ack, cwr, ece, fin, psh, rst, syn, urg).
        /// </summary>
        [TikProperty("tcp-flags", UnsetOnDefault = true)]
        public string TcpFlags { get; set; }

        /// <summary>
        /// tcp-mss: Matches TCP MSS value of an IP packet.
        /// </summary>
        [TikProperty("tcp-mss", UnsetOnDefault = true)]
        public string TcpMss { get; set; }

        /// <summary>
        /// time: Allows creating filter based on packet arrival time and date.
        /// </summary>
        [TikProperty("time", UnsetOnDefault = true)]
        public string/*time*/ Time { get; set; }

        /// <summary>
        /// tls-host: Matches TLS SNI hostname (RouterOS 7+).
        /// </summary>
        [TikProperty("tls-host", UnsetOnDefault = true)]
        public string TlsHost { get; set; }

        /// <summary>
        /// tos: Matches the ToS (Type of Service) field of IP header.
        /// </summary>
        [TikProperty("tos", UnsetOnDefault = true)]
        public string Tos { get; set; }

        /// <summary>
        /// ttl: Matches packets TTL value.
        /// </summary>
        [TikProperty("ttl", UnsetOnDefault = true)]
        public string Ttl { get; set; }

        /// <summary>
        /// disabled: Whether the rule is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// dynamic: Whether the rule was added dynamically (read-only).
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// invalid: Whether the rule is invalid (read-only).
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// bytes: Statistics — total bytes matched by this rule (read-only).
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public long Bytes { get; private set; }

        /// <summary>
        /// packets: Statistics — total packets matched by this rule (read-only).
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public long Packets { get; private set; }

        /// <summary>
        /// Human-readable identity.
        /// </summary>
        public override string ToString()
        {
            return base.ToString() + string.Format(" (Chain:{0}, Action:{1}, SrcAddress:{2}, DstAddress:{3}, Comment:{4})",
                Chain, Action, SrcAddress, DstAddress, Comment);
        }
    }
}
