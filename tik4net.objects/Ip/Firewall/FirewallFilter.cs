using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/filter
    /// </summary>
    [TikEntity("/ip/firewall/filter", IncludeDetails = true, IsOrdered = true, IncludeCliStats = true)]
    public class FirewallFilter
    {
        /// <summary>
        /// Firewall filter action type - <see cref="FirewallFilter.Action"/>
        /// </summary>
        public enum ActionType
        {
            /// <summary>
            /// accept the packet. Packet is not passed to next firewall rule.
            /// </summary>
            [TikEnum("accept")]
            Accept,

            /// <summary>
            /// add-dst-to-address-list - add destination address to address list specified by address-list parameter
            /// </summary>
            [TikEnum("add-dst-to-address-list")]
            AddDstToAddressList,

            /// <summary>
            /// add-src-to-address-list - add source address to address list specified by address-list parameter
            /// </summary>
            [TikEnum("add-src-to-address-list")]
            AddSrcToAddressList,

            /// <summary>
            /// drop - silently drop the packet
            /// </summary>
            [TikEnum("drop")]
            Drop,

            /// <summary>
            /// fasttrack-connection - mark the connection for FastPath, so its subsequent packets bypass
            /// the rest of the firewall. Part of the default RouterOS firewall.
            /// </summary>
            [TikEnum("fasttrack-connection")]
            FasttrackConnection,

            /// <summary>
            /// jump - jump to the user defined chain specified by the value of jump-target parameter
            /// </summary>
            [TikEnum("jump")]
            Jump,

            /// <summary>
            /// log - add a message to the system log containing following data: in-interface, out-interface, src-mac, protocol, src-ip:port->dst-ip:port and length of the packet.After packet is matched it is passed to next rule in the list, similar as passthrough
            /// </summary>
            [TikEnum("log")]
            Log,

            /// <summary>
            /// passthrough - ignore this rule and go to next one (useful for statistics).
            /// </summary>
            [TikEnum("passthrough")]
            Passthrough,

            /// <summary>
            /// reject - drop the packet and send an ICMP reject message
            /// </summary>
            [TikEnum("reject")]
            Reject,

            /// <summary>
            /// return - passes control back to the chain from where the jump took place
            /// </summary>
            [TikEnum("return")]
            Return,

            /// <summary>
            /// tarpit - captures and holds TCP connections(replies with SYN/ACK to the inbound TCP SYN packet)
            /// </summary>
            [TikEnum("tarpit")]
            Tarpit,
        }

        /// <summary>
        /// Firewall chain type - <see cref="FirewallFilter.Chain"/>
        /// </summary>
        public static class ChainType
        {
            /// <summary>
            /// input - used to process packets entering the router through one of the interfaces with the destination IP address which is one of the router's addresses. Packets passing through the router are not processed against the rules of the input chain
            /// </summary>
            public const string Input = "input";

            /// <summary>
            /// forward - used to process packets passing through the router
            /// </summary>
            public const string Forward = "forward";

            /// <summary>
            ///  output - used to process packets originated from the router and leaving it through one of the interfaces.Packets passing through the router are not processed against the rules of the output chain
            /// </summary>
            public const string Output = "output";
        }          

        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// action: Action to take if packet is matched by the rule: 
        /// accept - accept the packet.Packet is not passed to next firewall rule.
        /// add-dst-to-address-list - add destination address to  address list specified by address-list parameter
        /// add-src-to-address-list - add source address to  address list specified by address-list parameter
        /// drop - silently drop the packet
        /// jump - jump to the user defined chain specified by the value of jump-target parameter
        /// log - add a message to the system log containing following data: in-interface, out-interface, src-mac, protocol, src-ip:port-&gt;dst-ip:port and length of the packet.After packet is matched it is passed to next rule in the list, similar as passthrough
        /// passthrough - ignore this rule and go to next one (useful for statistics).
        /// reject - drop the packet and send an ICMP reject message
        /// return  - passes control back to the chain from where the jump took place
        /// tarpit - captures and holds TCP connections(replies with SYN/ACK to the inbound TCP SYN packet)
        /// </summary>
        [TikProperty("action", DefaultValue = "accept", WinboxLabel = "Action")]
        public TikField<ActionType?> Action { get; set; }

        /// <summary>
        /// address-list: Name of the address list to be used. Applicable if action is add-dst-to-address-list or add-src-to-address-list 
        /// </summary>
        [TikProperty("address-list", WinboxLabel = "Address List")]
        public TikField<string?> AddressList { get; set; }

        /// <summary>
        /// address-list-timeout: Time interval after which the address will be removed from the address list specified by address-list parameter. Used in conjunction with add-dst-to-address-list or add-src-to-address-list actions
        /// Value of 00:00:00 will leave the address in the address list forever
        /// </summary>
        [TikProperty("address-list-timeout", DefaultValue = "00:00:00")]
        public TikField<TikDuration?> AddressListTimeout { get; set; }

        /// <summary>
        /// chain: Specifies to which chain rule will be added. If the input does not match the name of an already defined chain, a new chain will be created. 
        /// </summary>
        /// <seealso cref="ChainType"/>
        [TikProperty("chain", WinboxLabel = "Chain")]
        public TikField<string?> Chain { get; set; }

        /// <summary>
        /// comment: Descriptive comment for the rule.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// connection-bytes: Matches packets only if a given amount of bytes has been transfered through the particular connection. 0 - means infinity, for example connection-bytes=2000000-0 means that the rule matches if more than 2MB has been transfered through the relevant connection 
        /// </summary>
        /// <remarks>A range, <c>low-high</c>, as the API prints it (<c>2000000-0</c>); a single number is both ends.</remarks>
        [TikProperty("connection-bytes", WinboxLabel = "Connection Bytes")]
        public TikField<string?> ConnectionBytes { get; set; }

        /// <summary>
        /// connection-limit: Restrict connection limit per address or address block up to and including given value 
        /// </summary>
        /// <remarks>The limit and the address-block netmask, <c>limit,netmask</c> (<c>10,32</c>).</remarks>
        [TikProperty("connection-limit", Negatable = true)]
        public TikField<string?> ConnectionLimit { get; set; }

        /// <summary>
        /// connection-mark: Matches packets marked via mangle facility with particular connection mark. If no-mark is set, rule will match any unmarked connection.
        /// </summary>
        [TikProperty("connection-mark", WinboxLabel = "Connection Mark", Negatable = true)]
        public TikField<string?> ConnectionMark { get; set; }

        /// <summary>
        /// connection-nat-state: matches a connection by the NAT applied to it. The whole list takes a <c>!</c>
        /// (<c>!srcnat,dstnat</c>); a member alone does not — the router refuses <c>srcnat,!dstnat</c>.
        /// </summary>
        [TikProperty("connection-nat-state", WinboxLabel = "Connection NAT State", Negatable = true)]
        public TikField<TikValueList<FirewallConnectionNatState>?> ConnectionNatState { get; set; }

        /// <summary>
        /// connection-rate: Connection Rate is a firewall matcher that allow to capture traffic based on present speed of the connection.  Read more &gt;&gt;
        /// </summary>
        /// <remarks>
        /// A range in bits per second, <c>low-high</c>, spelled out as the API prints it (<c>0-100000</c>); the CLI's
        /// <c>0-100k</c> is read the same.
        /// </remarks>
        [TikProperty("connection-rate", WinboxLabel = "Connection Rate", Negatable = true)]
        public TikField<string?> ConnectionRate { get; set; }

        /// <summary>
        /// connection-state: Interprets the connection tracking analysis data for a particular packet:
        /// established - a packet which belongs to an existing connection
        /// invalid - a packet which could not be identified for some reason
        /// new - the packet has started a new connection, or otherwise associated with a connection which has not seen packets in both directions.
        /// related - a packet which is related to, but not part of an existing connection, such as ICMP errors or a packet which begins FTP data connection
        /// </summary>
        [TikProperty("connection-state", WinboxLabel = "Connection State", Negatable = true)]
        public TikField<TikValueList<FirewallConnectionState>?> ConnectionState { get; set; }

        /// <summary>
        /// connection-type: Matches packets from related connections based on information from their connection tracking helpers. A relevant connection helper must be enabled under  /ip firewall service-port
        /// </summary>
        [TikProperty("connection-type", WinboxLabel = "Connection Type", Negatable = true)]
        public TikField<string?> ConnectionType { get; set; }

        /// <summary>
        /// content: Match packets that contain specified text
        /// </summary>
        [TikProperty("content", WinboxLabel = "Content", Negatable = true)]
        public TikField<string?> Content { get; set; }

        /// <summary>
        /// dscp: Matches DSCP IP header field.
        /// </summary>
        [TikProperty("dscp", WinboxLabel = "DSCP", Negatable = true)]
        public TikField<int?> Dscp { get; set; }

        /// <summary>
        /// dst-address: Matches packets which destination is equal to specified IP or falls into specified IP range.
        /// </summary>
        [TikProperty("dst-address", WinboxLabel = "Dst. Address", Negatable = true)]
        public TikField<string?> DstAddress { get; set; }

        /// <summary>
        /// dst-address-list: Matches destination address of a packet against user-defined address list
        /// </summary>
        [TikProperty("dst-address-list", WinboxLabel = "Dst. Address List", Negatable = true)]
        public TikField<string?> DstAddressList { get; set; }

        /// <summary>
        /// dst-address-type: Matches destination address type:
        /// unicast - IP address used for point to point transmission
        /// local - if dst-address is assigned to one of router's interfaces
        /// broadcast - packet is sent to all devices in subnet
        /// multicast - packet is forwarded to defined group of devices
        /// </summary>
        [TikProperty("dst-address-type", WinboxLabel = "Dst. Address Type", Negatable = true)]
        public TikField<TikValueList<FirewallAddressType>?> DstAddressType { get; set; }

        /// <summary>
        /// dst-limit: Matches packets until a given rate is exceeded. Rate is defined as packets per time interval. As opposed to the limit matcher, every flow has it's own limit. Flow is defined by mode parameter. Parameters are written in following format: count[/time],burst,mode[/expire].
        /// count - packet count per time interval per flow to match
        /// time - specifies the time interval in which the packet count per flow cannot be exceeded(optional, 1s will be used if not specified)
        /// burst - initial number of packets per flow to match: this number gets recharged by one every time/count, up to this number
        /// mode - this parameter specifies what unique fields define flow(src-address, dst-address, src-and-dst-address, dst-address-and-port, addresses-and-dst-port)
        /// expire - specifies interval after which flow with no packets will be allowed to be deleted(optional)
        /// </summary>
        [TikProperty("dst-limit", WinboxLabel = "Dst. Limit")]
        public TikField<string?> DstLimit { get; set; }

        /// <summary>
        /// dst-port: List of destination port numbers or port number ranges
        /// </summary>
        [TikProperty("dst-port", WinboxLabel = "Dst. Port", Negatable = true)]
        public TikField<TikValueList<TikPortRange>?> DstPort { get; set; }

        /// <summary>
        /// fragment: Matches fragmented packets. First (starting) fragment does not count. If connection tracking is enabled there will be no fragments as system automatically assembles every packet
        /// </summary>
        [TikProperty("fragment")]
        public TikField<bool?> Fragment { get; set; }

        /// <summary>
        /// hotspot: 
        /// </summary>
        [TikProperty("hotspot", WinboxLabel = "Hotspot", NegatableMembers = true)]
        public TikField<TikValueList<FirewallHotspotMatch>?> Hotspot { get; set; }

        /// <summary>
        /// icmp-options: Matches ICMP type:code fileds
        /// </summary>
        [TikProperty("icmp-options", WinboxLabel = "ICMP Options", Negatable = true)]
        public TikField<string?> IcmpOptions { get; set; }

        /// <summary>
        /// in-bridge-port: Actual interface the packet has entered the router, if incoming interface is bridge. Works only if use-ip-firewall is enabled in bridge settings.
        /// </summary>
        [TikProperty("in-bridge-port", WinboxLabel = "In. Bridge Port", Negatable = true)]
        public TikField<string?> InBridgePort { get; set; }

        /// <summary>
        /// in-bridge-port-list: Matches in-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("in-bridge-port-list", WinboxLabel = "In. Bridge Port List", Negatable = true)]
        public TikField<string?> InBridgePortList { get; set; }

        /// <summary>
        /// in-interface: Interface the packet has entered the router
        /// </summary>
        [TikProperty("in-interface", WinboxLabel = "In. Interface", Negatable = true)]
        public TikField<string?> InInterface { get; set; }

        /// <summary>
        /// in-interface-list: Matches in-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("in-interface-list", WinboxLabel = "In. Interface List", Negatable = true)]
        public TikField<string?> InInterfaceList { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet. Priority may be derived from VLAN, WMM or MPLS EXP bit.  Read more&gt;&gt;
        /// </summary>
        [TikProperty("ingress-priority", WinboxLabel = "Ingress Priority", Negatable = true)]
        public TikField<int?> IngressPriority { get; set; }

        /// <summary>
        /// ipsec-policy: matches the policy used by IPsec, written <c>direction,policy</c>: direction <c>in</c>
        /// (prerouting, input, forward) or <c>out</c> (postrouting, output, forward), policy <c>ipsec</c> (the packet is subject
        /// to IPsec processing) or <c>none</c> (it is not).
        /// </summary>
        [TikProperty("ipsec-policy", WinboxLabel = "IPsec Policy")]
        public TikField<string?> IpsecPolicy { get; set; }

        /// <summary>
        /// ipv4-options: Matches IPv4 header options.
        /// any - match packet with at least one of the ipv4 options
        /// loose-source-routing - match packets with loose source routing option.This option is used to route the internet datagram based on information supplied by the source
        /// no-record-route - match packets with no record route option.This option is used to route the internet datagram based on information supplied by the source
        /// no-router-alert - match packets with no router alter option
        /// no-source-routing - match packets with no source routing option
        /// no-timestamp - match packets with no timestamp option
        /// record-route - match packets with record route option
        /// router-alert - match packets with router alter option
        /// strict-source-routing - match packets with strict source routing option
        /// timestamp - match packets with timestamp
        /// </summary>
        [TikProperty("ipv4-options", WinboxLabel = "IPv4 Options")]
        public TikField<string?> Ipv4Options { get; set; }

        /// <summary>
        /// jump-target: Name of the target chain to jump to. Applicable only if action=jump
        /// </summary>
        [TikProperty("jump-target", WinboxLabel = "Jump Target")]
        public TikField<string?> JumpTarget { get; set; }

        /// <summary>
        /// layer7-protocol: Layer7 filter name defined in  layer7 protocol menu.
        /// </summary>
        [TikProperty("layer7-protocol", WinboxLabel = "Layer7 Protocol", Negatable = true)]
        public TikField<string?> Layer7Protocol { get; set; }

        /// <summary>
        /// limit: Matches packets at a limited rate. Rule using this matcher will match until this limit is reached. Parameters are written in following format: count[/time],burst.
        /// count - packet count per time interval to match
        /// time - specifies the time interval in which the packet count cannot be exceeded(optional, 1s will be used if not specified)
        /// burst - initial number of packets to match: this number gets recharged by one every time/count, up to this number
        /// </summary>
        [TikProperty("limit", WinboxLabel = "Limit", Negatable = true)]
        public TikField<string?> Limit { get; set; }

        /// <summary>
        /// log: Whether to log matched packets (shorthand flag; use action=log for full log action).
        /// </summary>
        [TikProperty("log", DefaultValue = "no", WinboxLabel = "Log")]
        public TikField<bool?> Log { get; set; }

        /// <summary>
        /// log-prefix: Adds specified text at the beginning of every log message. Applicable if action=log
        /// </summary>
        [TikProperty("log-prefix", WinboxLabel = "Log Prefix")]
        public TikField<string?> LogPrefix { get; set; }

        /// <summary>
        /// nth: Matches every nth packet.  Read more &gt;&gt;
        /// </summary>
        [TikProperty("nth", WinboxLabel = "Nth", Negatable = true)]
        public TikField<string?> Nth { get; set; }

        /// <summary>
        /// out-bridge-port: Actual interface the packet is leaving the router, if outgoing interface is bridge. Works only if use-ip-firewall is enabled in bridge settings.
        /// </summary>
        [TikProperty("out-bridge-port", WinboxLabel = "Out. Bridge Port", Negatable = true)]
        public TikField<string?> OutBridgePort { get; set; }

        /// <summary>
        /// out-bridge-port-list: Matches out-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("out-bridge-port-list", WinboxLabel = "Out. Bridge Port List", Negatable = true)]
        public TikField<string?> OutBridgePortList { get; set; }

        /// <summary>
        /// out-interface: Interface the packet is leaving the router
        /// </summary>
        [TikProperty("out-interface", WinboxLabel = "Out. Interface", Negatable = true)]
        public TikField<string?> OutInterface { get; set; }

        /// <summary>
        /// out-interface-list: Matches out-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("out-interface-list", WinboxLabel = "Out. Interface List", Negatable = true)]
        public TikField<string?> OutInterfaceList { get; set; }

        /// <summary>
        /// p2p: Matches packets from various peer-to-peer (P2P) protocols. Does not work on encrypted p2p packets.
        /// </summary>
        [TikProperty("p2p")]
        public TikField<string?> P2p { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with particular packet mark. If no-mark is set, rule will match any unmarked packet.
        /// </summary>
        [TikProperty("packet-mark", WinboxLabel = "Packet Mark", Negatable = true)]
        public TikField<string?> PacketMark { get; set; }

        /// <summary>
        /// packet-size: Matches packets of specified size or size range in bytes.
        /// </summary>
        [TikProperty("packet-size", WinboxLabel = "Packet Size", Negatable = true)]
        public TikField<string?> PacketSize { get; set; }

        /// <summary>
        /// per-connection-classifier: PCC matcher allows to divide traffic into equal streams with ability to keep packets with specific set of options in one particular stream.  Read more &gt;&gt;
        /// </summary>
        [TikProperty("per-connection-classifier", WinboxLabel = "Per Connection Classifier")]
        public TikField<string?> PerConnectionClassifier { get; set; }

        /// <summary>
        /// port: Matches if any (source or destination) port matches the specified list of ports or port ranges. Applicable only if protocol is TCP or UDP
        /// </summary>
        [TikProperty("port", Negatable = true)]
        public TikField<TikValueList<TikPortRange>?> Port { get; set; }

        /// <summary>
        /// priority: Matches packet priority (VLAN or WMM priority tag).
        /// </summary>
        [TikProperty("priority", WinboxLabel = "Priority", Negatable = true)]
        public TikField<string?> Priority { get; set; }

        /// <summary>
        /// protocol: Matches particular IP protocol specified by protocol name or number
        /// </summary>
        [TikProperty("protocol", WinboxLabel = "Protocol", Negatable = true)]
        public TikField<string?> Protocol { get; set; }

        /// <summary>
        /// psd: Attempts to detect TCP and UDP scans. Parameters are in following format WeightThreshold, DelayThreshold, LopPortWeight, HighPortWeight
        /// WeightThreshold - total weight of the latest TCP/UDP packets with different destination ports coming from the same host to be treated as port scan sequence
        /// DelayThreshold - delay for the packets with different destination ports coming from the same host to be treated as possible port scan subsequence
        /// LowPortWeight - weight of the packets with privileged(&lt;=1024) destination port
        /// HighPortWeight - weight of the packet with non-priviliged destination port
        /// </summary>
        [TikProperty("psd", WinboxLabel = "PSD")]
        public TikField<string?> Psd { get; set; }

        /// <summary>
        /// random: Matches packets randomly with given probability.
        /// </summary>
        [TikProperty("random", WinboxLabel = "Random")]
        public TikField<string?> Random { get; set; }

        /// <summary>
        /// realm: matches the packet's routing realm, a number (<c>!5</c> matches every other realm).
        /// </summary>
        [TikProperty("realm", Negatable = true, MinRouterOs = "7")]
        public TikField<string?> Realm { get; set; }

        /// <summary>
        /// reject-with: Specifies error to be sent back if packet is rejected. Applicable if action=reject
        /// </summary>
        [TikProperty("reject-with", WinboxLabel = "Reject With")]
        public TikField<string?> RejectWith { get; set; }

        /// <summary>
        /// routing-mark: Matches packets marked by mangle facility with particular routing mark
        /// </summary>
        [TikProperty("routing-mark", WinboxLabel = "Routing Mark", Negatable = true)]
        public TikField<string?> RoutingMark { get; set; }

        /// <summary>
        /// src-address: Matches packets which source is equal to specified IP or falls into specified IP range.
        /// </summary>
        [TikProperty("src-address", WinboxLabel = "Src. Address", Negatable = true)]
        public TikField<string?> SrcAddress { get; set; }

        /// <summary>
        /// src-address-list: Matches source address of a packet against user-defined  address list
        /// </summary>
        [TikProperty("src-address-list", WinboxLabel = "Src. Address List", Negatable = true)]
        public TikField<string?> SrcAddressList { get; set; }

        /// <summary>
        /// src-address-type: 
        /// Matches source address type:
        /// unicast - IP address used for point to point transmission
        /// local - if address is assigned to one of router's interfaces
        /// broadcast - packet is sent to all devices in subnet
        /// multicast - packet is forwarded to defined group of devices
        /// </summary>
        [TikProperty("src-address-type", WinboxLabel = "Src. Address Type", Negatable = true)]
        public TikField<TikValueList<FirewallAddressType>?> SrcAddressType { get; set; }

        /// <summary>
        /// src-port: List of source ports and ranges of source ports. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("src-port", WinboxLabel = "Src. Port", Negatable = true)]
        public TikField<TikValueList<TikPortRange>?> SrcPort { get; set; }

        /// <summary>
        /// src-mac-address: Matches source MAC address of the packet
        /// </summary>
        [TikProperty("src-mac-address", WinboxLabel = "Src. MAC Address", Negatable = true)]
        public TikField<string?> SrcMacAddress { get; set; }

        /// <summary>
        /// tcp-flags: Matches specified TCP flags
        /// ack - acknowledging data
        /// cwr - congestion window reduced
        /// ece - ECN-echo flag(explicit congestion notification)
        /// fin - close connection
        /// psh - push function
        /// rst - drop connection
        /// syn - new connection
        /// urg - urgent data
        /// </summary>
        [TikProperty("tcp-flags", WinboxLabel = "TCP Flags", Negatable = true, NegatableMembers = true, SetKeepsUnnamedHalf = true)]
        public TikField<TikValueList<FirewallTcpFlag>?> TcpFlags { get; set; }

        /// <summary>
        /// tcp-mss: Matches TCP MSS value of an IP packet
        /// </summary>
        [TikProperty("tcp-mss", WinboxLabel = "TCP MSS", Negatable = true)]
        public TikField<string?> TcpMss { get; set; }

        /// <summary>
        /// time: Allows to create filter based on the packets' arrival time and date or, for locally generated packets, departure time and date
        /// </summary>
        [TikProperty("time", WinboxLabel = "Time")]
        public TikField<string?> Time { get; set; }

        /// <summary>
        /// tls-host: Matches TLS SNI hostname (RouterOS 7+).
        /// </summary>
        [TikProperty("tls-host", WinboxLabel = "TLS Host", Negatable = true)]
        public TikField<string?> TlsHost { get; set; }

        /// <summary>
        /// tos: Matches the ToS (Type of Service) field of IP header.
        /// </summary>
        [TikProperty("tos", WinboxLabel = "TOS", Negatable = true, MinRouterOs = "7")]
        public TikField<string?> Tos { get; set; }

        /// <summary>
        /// ttl: Matches packets TTL value
        /// </summary>
        [TikProperty("ttl", WinboxLabel = "TTL")]
        public TikField<string?> Ttl { get; set; }

        /// <summary>
        /// Row disabled property.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// Row dynamic property.
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikField<bool?> Dynamic { get; private set; }

        /// <summary>
        /// Row invalid property.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; private set; }

        /// <summary>
        /// Statistics - bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true, WinboxLabel = "Bytes")]
        public TikField<long?> Bytes { get; private set; }

        /// <summary>
        /// Statistics - packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true, WinboxLabel = "Packets")]
        public TikField<long?> Packets { get; private set; }
    }
}
