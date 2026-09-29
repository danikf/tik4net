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
        public string? Id { get; private set; }

        /// <summary>
        /// action: Action to take if the packet is matched by the rule.
        /// <seealso cref="ActionType"/>
        /// </summary>
        [TikProperty("action", DefaultValue = "accept", WinboxLabel = "Action")]
        public TikValue<ActionType?> Action { get; set; }

        /// <summary>
        /// address-list: Name of the address list used when action is add-dst-to-address-list or add-src-to-address-list.
        /// </summary>
        [TikProperty("address-list", WinboxLabel = "Address List")]
        public TikValue<string?> AddressList { get; set; }

        /// <summary>
        /// address-list-timeout: Time interval after which the address will be removed from the address list.
        /// Value 00:00:00 leaves the address in the list forever.
        /// </summary>
        [TikProperty("address-list-timeout", DefaultValue = "00:00:00")]
        public TikValue<TikDuration?> AddressListTimeout { get; set; }

        /// <summary>
        /// chain: Specifies to which chain the rule is added. Use built-in prerouting/output or a custom name.
        /// <seealso cref="ChainType"/>
        /// </summary>
        [TikProperty("chain", WinboxLabel = "Chain")]
        public TikValue<string?> Chain { get; set; }

        /// <summary>
        /// comment: Descriptive comment for the rule.
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }

        /// <summary>
        /// content: Match packets that contain the specified text.
        /// </summary>
        [TikProperty("content", WinboxLabel = "Content", Negatable = true)]
        public TikValue<string?> Content { get; set; }

        /// <summary>
        /// dscp: Matches DSCP IP header field.
        /// </summary>
        [TikProperty("dscp", WinboxLabel = "DSCP")]
        public TikValue<int?> Dscp { get; set; }

        /// <summary>
        /// dst-address: Matches packets whose destination equals the specified IP or falls into the specified IP range.
        /// </summary>
        [TikProperty("dst-address", WinboxLabel = "Dst. Address", Negatable = true)]
        public TikValue<string?> DstAddress { get; set; }

        /// <summary>
        /// dst-address-list: Matches destination address of a packet against a user-defined address list.
        /// </summary>
        [TikProperty("dst-address-list", WinboxLabel = "Dst. Address List", Negatable = true)]
        public TikValue<string?> DstAddressList { get; set; }

        /// <summary>
        /// dst-address-type: Matches destination address type (unicast, local, broadcast, multicast).
        /// </summary>
        [TikProperty("dst-address-type", WinboxLabel = "Dst. Address Type")]
        public TikValue<string?> DstAddressType { get; set; }

        /// <summary>
        /// dst-limit: Matches packets until a given rate (per-flow) is exceeded.
        /// </summary>
        [TikProperty("dst-limit", WinboxLabel = "Dst. Limit")]
        public TikValue<string?> DstLimit { get; set; }

        /// <summary>
        /// dst-port: List of destination port numbers or port number ranges. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("dst-port", WinboxLabel = "Dst. Port", Negatable = true)]
        public TikValue<string?> DstPort { get; set; }

        /// <summary>
        /// fragment: Matches fragmented packets (not the first fragment).
        /// </summary>
        [TikProperty("fragment")]
        public TikValue<bool?> Fragment { get; set; }

        /// <summary>
        /// hotspot: Matches packets in a HotSpot scenario by the specified attribute.
        /// </summary>
        [TikProperty("hotspot", WinboxLabel = "Hotspot")]
        public TikValue<string?> Hotspot { get; set; }

        /// <summary>
        /// icmp-options: Matches ICMP type:code fields.
        /// </summary>
        [TikProperty("icmp-options", WinboxLabel = "ICMP Options", Negatable = true)]
        public TikValue<string?> IcmpOptions { get; set; }

        /// <summary>
        /// in-bridge-port: Actual interface the packet has entered the router when the incoming interface is a bridge.
        /// </summary>
        [TikProperty("in-bridge-port")]
        public TikValue<string?> InBridgePort { get; set; }

        /// <summary>
        /// in-bridge-port-list: Matches in-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("in-bridge-port-list")]
        public TikValue<string?> InBridgePortList { get; set; }

        /// <summary>
        /// in-interface: Interface the packet has entered the router.
        /// </summary>
        [TikProperty("in-interface", WinboxLabel = "In. Interface", Negatable = true)]
        public TikValue<string?> InInterface { get; set; }

        /// <summary>
        /// in-interface-list: Matches in-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("in-interface-list", WinboxLabel = "In. Interface List", Negatable = true)]
        public TikValue<string?> InInterfaceList { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet (VLAN, WMM, MPLS EXP).
        /// </summary>
        [TikProperty("ingress-priority", WinboxLabel = "Ingress Priority", Negatable = true)]
        public TikValue<int?> IngressPriority { get; set; }

        /// <summary>
        /// ipsec-policy: Matches the policy used by IPsec. Format: direction,policy.
        /// </summary>
        [TikProperty("ipsec-policy", WinboxLabel = "IPsec Policy")]
        public TikValue<string?> IpsecPolicy { get; set; }

        /// <summary>
        /// ipv4-options: Matches IPv4 header options (any, loose-source-routing, record-route, router-alert, etc.).
        /// </summary>
        [TikProperty("ipv4-options", WinboxLabel = "IPv4 Options")]
        public TikValue<string?> Ipv4Options { get; set; }

        /// <summary>
        /// jump-target: Name of the target chain to jump to. Applicable only if action=jump.
        /// </summary>
        [TikProperty("jump-target", WinboxLabel = "Jump Target")]
        public TikValue<string?> JumpTarget { get; set; }

        /// <summary>
        /// limit: Matches packets at a limited rate. Parameters: count[/time],burst.
        /// </summary>
        [TikProperty("limit", WinboxLabel = "Limit")]
        public TikValue<string?> Limit { get; set; }

        /// <summary>
        /// log: Whether to log matched packets (shorthand flag; use action=log for full log action).
        /// </summary>
        [TikProperty("log", DefaultValue = "no", WinboxLabel = "Log")]
        public TikValue<bool?> Log { get; set; }

        /// <summary>
        /// log-prefix: Adds specified text at the beginning of every log message. Applicable if action=log or log=yes.
        /// </summary>
        [TikProperty("log-prefix", WinboxLabel = "Log Prefix")]
        public TikValue<string?> LogPrefix { get; set; }

        /// <summary>
        /// nth: Matches every nth packet.
        /// </summary>
        [TikProperty("nth", WinboxLabel = "Nth", Negatable = true)]
        public TikValue<string?> Nth { get; set; }

        /// <summary>
        /// out-bridge-port: Actual interface the packet is leaving through when it is a bridge.
        /// </summary>
        [TikProperty("out-bridge-port")]
        public TikValue<string?> OutBridgePort { get; set; }

        /// <summary>
        /// out-bridge-port-list: Matches out-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("out-bridge-port-list")]
        public TikValue<string?> OutBridgePortList { get; set; }

        /// <summary>
        /// out-interface: Interface the packet is leaving the router through.
        /// </summary>
        [TikProperty("out-interface", WinboxLabel = "Out. Interface", Negatable = true)]
        public TikValue<string?> OutInterface { get; set; }

        /// <summary>
        /// out-interface-list: Matches out-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("out-interface-list", WinboxLabel = "Out. Interface List", Negatable = true)]
        public TikValue<string?> OutInterfaceList { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with a particular packet mark.
        /// </summary>
        [TikProperty("packet-mark")]
        public TikValue<string?> PacketMark { get; set; }

        /// <summary>
        /// packet-size: Matches packets of specified size or size range in bytes.
        /// </summary>
        [TikProperty("packet-size", WinboxLabel = "Packet Size", Negatable = true)]
        public TikValue<string?> PacketSize { get; set; }

        /// <summary>
        /// per-connection-classifier: PCC matcher divides traffic into equal streams.
        /// </summary>
        [TikProperty("per-connection-classifier", WinboxLabel = "Per Connection Classifier")]
        public TikValue<string?> PerConnectionClassifier { get; set; }

        /// <summary>
        /// port: Matches if any (source or destination) port matches the specified list. Applicable only for TCP/UDP.
        /// </summary>
        [TikProperty("port")]
        public TikValue<string?> Port { get; set; }

        /// <summary>
        /// priority: Matches packet priority (VLAN or WMM priority tag).
        /// </summary>
        [TikProperty("priority", WinboxLabel = "Priority", Negatable = true)]
        public TikValue<string?> Priority { get; set; }

        /// <summary>
        /// protocol: Matches particular IP protocol specified by protocol name or number.
        /// </summary>
        [TikProperty("protocol", WinboxLabel = "Protocol", Negatable = true)]
        public TikValue<string?> Protocol { get; set; }

        /// <summary>
        /// psd: Attempts to detect TCP and UDP port scans.
        /// Format: WeightThreshold, DelayThreshold, LowPortWeight, HighPortWeight.
        /// </summary>
        [TikProperty("psd", WinboxLabel = "PSD")]
        public TikValue<string?> Psd { get; set; }

        /// <summary>
        /// random: Matches packets randomly with given probability.
        /// </summary>
        [TikProperty("random", WinboxLabel = "Random")]
        public TikValue<string?> Random { get; set; }

        /// <summary>
        /// src-address: Matches packets whose source equals the specified IP or falls into the specified IP range.
        /// </summary>
        [TikProperty("src-address", WinboxLabel = "Src. Address", Negatable = true)]
        public TikValue<string?> SrcAddress { get; set; }

        /// <summary>
        /// src-address-list: Matches source address of a packet against a user-defined address list.
        /// </summary>
        [TikProperty("src-address-list", WinboxLabel = "Src. Address List", Negatable = true)]
        public TikValue<string?> SrcAddressList { get; set; }

        /// <summary>
        /// src-address-type: Matches source address type (unicast, local, broadcast, multicast).
        /// </summary>
        [TikProperty("src-address-type", WinboxLabel = "Src. Address Type")]
        public TikValue<string?> SrcAddressType { get; set; }

        /// <summary>
        /// src-mac-address: Matches source MAC address of the packet.
        /// </summary>
        [TikProperty("src-mac-address", WinboxLabel = "Src. MAC Address", Negatable = true)]
        public TikValue<string?> SrcMacAddress { get; set; }

        /// <summary>
        /// src-port: List of source ports and ranges. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("src-port", WinboxLabel = "Src. Port", Negatable = true)]
        public TikValue<string?> SrcPort { get; set; }

        /// <summary>
        /// tcp-flags: Matches specified TCP flags (ack, cwr, ece, fin, psh, rst, syn, urg).
        /// </summary>
        [TikProperty("tcp-flags", WinboxLabel = "TCP Flags")]
        public TikValue<string?> TcpFlags { get; set; }

        /// <summary>
        /// tcp-mss: Matches TCP MSS value of an IP packet.
        /// </summary>
        [TikProperty("tcp-mss", WinboxLabel = "TCP MSS", Negatable = true)]
        public TikValue<string?> TcpMss { get; set; }

        /// <summary>
        /// time: Allows creating filter based on packet arrival time and date.
        /// </summary>
        [TikProperty("time", WinboxLabel = "Time")]
        public TikValue<string?> Time { get; set; }

        /// <summary>
        /// tls-host: Matches TLS SNI hostname (RouterOS 7+).
        /// </summary>
        [TikProperty("tls-host", WinboxLabel = "TLS Host", Negatable = true)]
        public TikValue<string?> TlsHost { get; set; }

        /// <summary>
        /// tos: Matches the ToS (Type of Service) field of IP header.
        /// </summary>
        [TikProperty("tos", WinboxLabel = "TOS")]
        public TikValue<string?> Tos { get; set; }

        /// <summary>
        /// ttl: Matches packets TTL value.
        /// </summary>
        [TikProperty("ttl", WinboxLabel = "TTL")]
        public TikValue<string?> Ttl { get; set; }

        /// <summary>
        /// disabled: Whether the rule is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>
        /// dynamic: Whether the rule was added dynamically (read-only).
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikValue<bool?> Dynamic { get; private set; }

        /// <summary>
        /// invalid: Whether the rule is invalid (read-only).
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikValue<bool?> Invalid { get; private set; }

        /// <summary>
        /// bytes: Statistics — total bytes matched by this rule (read-only).
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true, WinboxLabel = "Bytes")]
        public TikValue<long?> Bytes { get; private set; }

        /// <summary>
        /// packets: Statistics — total packets matched by this rule (read-only).
        /// </summary>
        [TikProperty("packets", IsReadOnly = true, WinboxLabel = "Packets")]
        public TikValue<long?> Packets { get; private set; }

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
