using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/mangle — rules that mark, change or redirect packets: the filter's matchers, plus what an action
    /// writes (<see cref="NewPacketMark"/>, <see cref="NewConnectionMark"/>, <see cref="NewRoutingMark"/>, …).
    /// </summary>
    [TikEntity("/ip/firewall/mangle", IncludeDetails = true, IsOrdered = true, IncludeCliStats = true)]
    public class FirewallMangle
    {
        /// <summary>
        /// Mangle action type - <see cref="FirewallMangle.Action"/>
        /// </summary>
        public enum ActionType
        {
            /// <summary>
            /// accept - accept the packet. Packet is not passed to next firewall rule.
            /// </summary>
            [TikEnum("accept")]
            Accept,

            /// <summary>
            /// add-dst-to-address-list - add destination address to Address list specified by address-list parameter
            /// </summary>
            [TikEnum("add-dst-to-address-list")]            
            AddDstToAddressList,

            /// <summary>
            /// add-src-to-address-list - add source address to Address list specified by address-list parameter
            /// </summary>
            [TikEnum("add-src-to-address-list")]
            AddSrcToAddressList,

            /// <summary>
            /// change-dscp - change Differentiated Services Code Point (DSCP) field value specified by the new-dscp parameter
            /// </summary>
            [TikEnum("change-dscp")]
            ChangeDscp,

            /// <summary>
            /// change-mss - change Maximum Segment Size field value of the packet to a value specified by the new-mss parameter
            /// </summary>
            [TikEnum("change-mss")]
            ChangeMms,

            /// <summary>
            /// change-ttl - change Time to Live field value of the packet to a value specified by the new-ttl parameter
            /// </summary>
            [TikEnum("change-ttl")]        
            ChangeTtl,

            /// <summary>
            /// clear-df - clear 'Do Not Fragment' Flag
            /// </summary>
            [TikEnum("clear-df")]
            ClearDf,

            /// <summary>
            /// drop - silently drop the packet
            /// </summary>
            [TikEnum("drop")]
            Drop,

            /// <summary>
            /// fasttrack-connection - show a FastPath counter and mark the connection for FastPath,
            /// bypassing the rest of the firewall for its subsequent packets
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
            /// mark-connection - place a mark specified by the new-connection-mark parameter on the entire connection that matches the rule
            /// </summary>
            [TikEnum("mark-connection")]
            MarkConnection,

            /// <summary>
            /// place a mark specified by the new-packet-mark parameter on a packet that matches the rule
            /// </summary>
            [TikEnum("mark-packet")]
            MarkPacket,

            /// <summary>
            /// place a mark specified by the new-routing-mark parameter on a packet. This kind of mark is
            /// used for policy routing purposes only
            /// </summary>
            [TikEnum("mark-routing")]
            MarkRouting,

            /// <summary>
            /// ignore this rule and go to next one (useful for statistics).
            /// </summary>
            [TikEnum("passthrough")]
            Passthrough,

            /// <summary>
            /// return - pass control back to the chain from where the jump took place
            /// </summary>
            [TikEnum("return")]
            Return,

            /// <summary>
            /// route - force the packet to the gateway specified by the route-dst parameter
            /// </summary>
            [TikEnum("route")]
            Route,

            /// <summary>
            /// set-priority - set priority specified by the new- priority parameter on the packets sent out through a link that is capable of transporting priority(VLAN or WMM - enabled wireless interface). Read more>
            /// </summary>
            [TikEnum("set-priority")]
            SetPriority,

            /// <summary>
            /// sniff-pc
            /// </summary>
            [TikEnum("sniff-pc")]
            SniffPc,

            /// <summary>
            /// sniff-tzsp - send packet to a remote TZSP compatible system(such as Wireshark). Set remote target with sniff-target and sniff-target-port parameters(Wireshark recommends port 37008)
            /// </summary>
            [TikEnum("sniff-tzsp")]
            SniffTzsp,

            /// <summary>
            /// strip-ipv4-options - strip IPv4 option fields from IP header.
            /// </summary>
            [TikEnum("strip-ipv4-options")]
            StripIpv4Options,
        }

        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// chain: firewall chain this rule applies to (prerouting, input, output, forward, postrouting).
        /// </summary>
        [TikProperty("chain", WinboxLabel = "Chain")]
        public TikField<string?> Chain { get; set; }

        /// <summary>
        /// action: determines packet processing behavior for matched rules.
        /// </summary>
        [TikProperty("action", WinboxLabel = "Action")]
        public TikField<ActionType?> Action { get; set; }

        /// <summary>
        /// new-priority: sets priority for packets (VLAN, WMM, DSCP, or MPLS EXP priority).
        /// </summary>
        [TikProperty("new-priority", DefaultValue = "0", WinboxLabel = "New Priority")]
        public TikField<string?> NewPriority { get; set; }

        /// <summary>
        /// passthrough: when enabled, matched packets proceed to subsequent rules rather than stopping.
        /// </summary>
        [TikProperty("passthrough", DefaultValue = "yes", WinboxLabel = "Passthrough")]
        public TikField<bool?> Passthrough { get; set; }

        /// <summary>
        /// src-address-list: references predefined address list for source IP filtering.
        /// </summary>
        [TikProperty("src-address-list", WinboxLabel = "Src. Address List", Negatable = true)]
        public TikField<string?> SrcAddressList { get; set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikField<bool?> Dynamic { get; private set; }

        /// <summary>
        /// disabled: toggles rule activation without deletion.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// new-packet-mark: sets a new packet-mark value for matching packets.
        /// </summary>
        [TikProperty("new-packet-mark", WinboxLabel = "New Packet Mark")]
        public TikField<string?> NewPacketMark { get; set; }

        /// <summary>
        /// comment: rule documentation and identification text.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// dst-address-list: references predefined address list for destination IP filtering.
        /// </summary>
        [TikProperty("dst-address-list", WinboxLabel = "Dst. Address List", Negatable = true)]
        public TikField<string?> DstAddressList { get; set; }

        /// <summary>
        /// protocol: filters packets by protocol type (TCP, UDP, ICMP, etc.).
        /// </summary>
        [TikProperty("protocol", WinboxLabel = "Protocol", Negatable = true)]
        public TikField<string?> Protocol { get; set; }

        /// <summary>
        /// src-address: matches packets based on source IP address or prefix.
        /// </summary>
        [TikProperty("src-address", WinboxLabel = "Src. Address", Negatable = true)]
        public TikField<string?> SrcAddress { get; set; }

        /// <summary>
        /// dst-address: matches packets based on destination IP address or prefix.
        /// </summary>
        [TikProperty("dst-address", WinboxLabel = "Dst. Address", Negatable = true)]
        public TikField<string?> DstAddress { get; set; }

        /// <summary>
        /// jump-target: redirects rule processing to custom chains for advanced logic.
        /// </summary>
        [TikProperty("jump-target", WinboxLabel = "Jump Target")]
        public TikField<string?> JumpTarget { get; set; }
        
        /// <summary>
        /// address-list: specifies address list name for address-based matching.
        /// </summary>
        [TikProperty("address-list", WinboxLabel = "Address List")]
        public TikField<string?> AddressList { get; set; }

        /// <summary>
        /// address-list-timeout: timeout duration for addresses added to address lists.
        /// </summary>
        [TikProperty("address-list-timeout", DefaultValue = "00:00:00")]
        public TikField<TikDuration?> AddressListTimeout { get; set; }

        // ── The matchers it shares with the filter and raw rules ──

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
        [TikProperty("in-bridge-port-list", Negatable = true)]
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
        /// ipsec-policy: Matches the policy used by IPsec. Format: direction,policy.
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
        [TikProperty("out-bridge-port-list", Negatable = true)]
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
        /// routing-mark: Matches packets marked by mangle facility with particular routing mark
        /// </summary>
        [TikProperty("routing-mark", WinboxLabel = "Routing Mark", Negatable = true)]
        public TikField<string?> RoutingMark { get; set; }

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
        /// src-mac-address: Matches source MAC address of the packet
        /// </summary>
        [TikProperty("src-mac-address", WinboxLabel = "Src. MAC Address", Negatable = true)]
        public TikField<string?> SrcMacAddress { get; set; }

        /// <summary>
        /// src-port: List of source ports and ranges of source ports. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("src-port", WinboxLabel = "Src. Port", Negatable = true)]
        public TikField<TikValueList<TikPortRange>?> SrcPort { get; set; }

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
        [TikProperty("tos", WinboxLabel = "TOS", Negatable = true)]
        public TikField<string?> Tos { get; set; }

        /// <summary>
        /// ttl: Matches packets TTL value
        /// </summary>
        [TikProperty("ttl", WinboxLabel = "TTL")]
        public TikField<string?> Ttl { get; set; }

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

        // ── Mangle's own: what an action writes, and the matchers the filter does not have ──

        /// <summary>
        /// new-connection-mark: the mark <c>mark-connection</c> places on the whole connection.
        /// </summary>
        [TikProperty("new-connection-mark", WinboxLabel = "New Connection Mark")]
        public TikField<string?> NewConnectionMark { get; set; }

        /// <summary>
        /// new-routing-mark: the routing mark (a routing table) <c>mark-routing</c> places on the packet, for policy routing.
        /// </summary>
        [TikProperty("new-routing-mark", WinboxLabel = "New Routing Mark")]
        public TikField<string?> NewRoutingMark { get; set; }

        /// <summary>
        /// new-dscp: the DSCP value (0–63) <c>change-dscp</c> writes.
        /// </summary>
        [TikProperty("new-dscp")]
        public TikField<int?> NewDscp { get; set; }

        /// <summary>
        /// new-mss: the MSS <c>change-mss</c> writes — a number, or <c>clamp-to-pmtu</c>.
        /// </summary>
        [TikProperty("new-mss")]
        public TikField<string?> NewMss { get; set; }

        /// <summary>
        /// new-ttl: how <c>change-ttl</c> changes the TTL — <c>set:64</c>, <c>increment:1</c> or <c>decrement:1</c>.
        /// </summary>
        [TikProperty("new-ttl", WinboxLabel = "New TTL")]
        public TikField<string?> NewTtl { get; set; }

        /// <summary>
        /// route-dst: the gateway <c>route</c> sends the packet to.
        /// </summary>
        [TikProperty("route-dst", WinboxLabel = "Route Dst.")]
        public TikField<string?> RouteDst { get; set; }

        /// <summary>
        /// sniff-target: the TZSP receiver <c>sniff-tzsp</c> sends the packet to.
        /// </summary>
        [TikProperty("sniff-target", WinboxLabel = "Sniff Target")]
        public TikField<string?> SniffTarget { get; set; }

        /// <summary>
        /// sniff-target-port: the TZSP receiver's UDP port.
        /// </summary>
        [TikProperty("sniff-target-port", DefaultValue = "37008", WinboxLabel = "Sniff Target Port")]
        public TikField<int?> SniffTargetPort { get; set; }

        /// <summary>
        /// sniff-id: the sensor id <c>sniff-pc</c> sends with the packet.
        /// </summary>
        [TikProperty("sniff-id", WinboxLabel = "Sniff ID")]
        public TikField<int?> SniffId { get; set; }

        /// <summary>
        /// realm: matches the packet's routing realm, a number (<c>!5</c> matches every other realm).
        /// </summary>
        [TikProperty("realm", Negatable = true)]
        public TikField<string?> Realm { get; set; }

        /// <summary>
        /// connection-nat-state: matches a connection by the NAT applied to it. The whole list takes a <c>!</c>
        /// (<c>!srcnat,dstnat</c>); a member alone does not — the router refuses <c>srcnat,!dstnat</c>.
        /// </summary>
        [TikProperty("connection-nat-state", WinboxLabel = "Connection NAT State", Negatable = true)]
        public TikField<TikValueList<FirewallConnectionNatState>?> ConnectionNatState { get; set; }

        /// <summary>
        /// ToString override.
        /// </summary>
        public override string ToString()
        {
            return base.ToString() + string.Format(" (Chain:{0}, Action:{1}, SrcAddress:{2}, DstAddress:{3}, Comment:{4})", Chain, Action, SrcAddress, DstAddress, Comment);
        }
    }

}
