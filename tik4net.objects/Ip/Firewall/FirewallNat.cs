using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/nat
    /// </summary>
    [TikEntity("/ip/firewall/nat", IncludeDetails = true, IsOrdered = true, IncludeCliStats = true)]
    public class FirewallNat
    {
        /// <summary>
        /// .id: primary key of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// chain: Specifies to which chain rule will be added. If the input does not match the name of an already defined chain, a new chain will be created. 
        /// </summary>
        [TikProperty("chain", WinboxLabel = "Chain")]
        public TikField<string?> Chain { get; set; }

        /// <summary>
        /// action: determines how packets are processed (src-nat, dst-nat, masquerade, redirect, etc.).
        /// </summary>
        [TikProperty("action", DefaultValue = "accept", WinboxLabel = "Action")]
        public TikField<string?> Action { get; set; }

        /// <summary>
        /// to-addresses: replacement IP address or address range for source/destination NAT operations.
        /// </summary>
        [TikProperty("to-addresses", WinboxLabel = "To Addresses")]
        public TikField<string?> ToAddresses { get; set; }

        /// <summary>
        /// src-address: Matches packets whose source equals the specified IP or falls into the specified IP range.
        /// </summary>
        [TikProperty("src-address", WinboxLabel = "Src. Address")]
        public TikNegatableField<string?> SrcAddress { get; set; }

        /// <summary>
        /// out-interface: Interface the packet is leaving the router through.
        /// </summary>
        [TikProperty("out-interface", WinboxLabel = "Out. Interface")]
        public TikNegatableField<string?> OutInterface { get; set; }

        /// <summary>
        /// invalid: Whether the rule is invalid (read-only).
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; private set; }

        /// <summary>
        /// dynamic: Whether the rule was added dynamically (read-only).
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikField<bool?> Dynamic { get; private set; }

        /// <summary>
        /// disabled: Whether the rule is disabled.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// comment: Descriptive comment for the rule.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// src-address-list: Matches source address of a packet against a user-defined address list.
        /// </summary>
        [TikProperty("src-address-list", WinboxLabel = "Src. Address List")]
        public TikNegatableField<string?> SrcAddressList { get; set; }

        /// <summary>
        /// dst-address: Matches packets whose destination equals the specified IP or falls into the specified IP range.
        /// </summary>
        [TikProperty("dst-address", WinboxLabel = "Dst. Address")]
        public TikNegatableField<string?> DstAddress { get; set; }

        /// <summary>
        /// in-interface: Interface the packet has entered the router.
        /// </summary>
        [TikProperty("in-interface", WinboxLabel = "In. Interface")]
        public TikNegatableField<string?> InInterface { get; set; }

        /// <summary>
        /// protocol: Matches particular IP protocol specified by protocol name or number.
        /// </summary>
        [TikProperty("protocol", WinboxLabel = "Protocol")]
        public TikNegatableField<string?> Protocol { get; set; }

        /// <summary>
        /// to-ports: the replacement port, or port range (<c>8000-8010</c>), for modified packets: <c>rule.ToPorts = new TikPortRange(8000, 8010)</c>.
        /// </summary>
        [TikProperty("to-ports", WinboxLabel = "To Ports")]
        public TikField<TikPortRange?> ToPorts { get; set; }

        /// <summary>
        /// dst-port: List of destination port numbers or port number ranges. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("dst-port", WinboxLabel = "Dst. Port")]
        public TikNegatableField<TikValueList<TikPortRange>?> DstPort { get; set; }

        /// <summary>
        /// src-port: List of source ports and ranges of source ports. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [TikProperty("src-port", WinboxLabel = "Src. Port")]
        public TikNegatableField<TikValueList<TikPortRange>?> SrcPort { get; set; }

        /// <summary>
        /// address-list: Name of the address list to be used. Applicable if action is add-dst-to-address-list or add-src-to-address-list 
        /// </summary>
        [TikProperty("address-list", WinboxLabel = "Address List")]
        public TikField<string?> AddressList { get; set; }

        /// <summary>
        /// address-list-timeout: Time interval after which the address will be removed from the address list specified by address-list parameter. Used in conjunction with add-dst-to-address-list or add-src-to-address-list actions.
        /// Value of 00:00:00 will leave the address in the address list forever
        /// </summary>
        [TikProperty("address-list-timeout", DefaultValue = "00:00:00")]
        public TikField<TikDuration?> AddressListTimeout { get; set; }

        /// <summary>
        /// bytes: Statistics — total bytes matched by this rule (read-only).
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true, WinboxLabel = "Bytes")]
        public TikField<long?> Bytes { get; private set; }

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
        [TikProperty("connection-limit")]
        public TikNegatableField<string?> ConnectionLimit { get; set; }

        /// <summary>
        /// connection-mark: Matches packets marked via mangle facility with particular connection mark. If no-mark is set, rule will match any unmarked connection.
        /// </summary>
        [TikProperty("connection-mark", WinboxLabel = "Connection Mark")]
        public TikNegatableField<string?> ConnectionMark { get; set; }

        /// <summary>
        /// connection-rate: Connection Rate is a firewall matcher that allow to capture traffic based on present speed of the connection.  Read more &gt;&gt;
        /// </summary>
        /// <remarks>
        /// A range in bits per second, <c>low-high</c>, spelled out as the API prints it (<c>0-100000</c>); the CLI's
        /// <c>0-100k</c> is read the same.
        /// </remarks>
        [TikProperty("connection-rate", WinboxLabel = "Connection Rate")]
        public TikNegatableField<string?> ConnectionRate { get; set; }

        /// <summary>
        /// connection-type: Matches packets from related connections based on information from their connection tracking helpers. A relevant connection helper must be enabled under  /ip firewall service-port
        /// </summary>
        [TikProperty("connection-type", WinboxLabel = "Connection Type")]
        public TikNegatableField<string?> ConnectionType { get; set; }

        /// <summary>
        /// content: Match packets that contain the specified text.
        /// </summary>
        [TikProperty("content", WinboxLabel = "Content")]
        public TikNegatableField<string?> Content { get; set; }

        /// <summary>
        /// dscp: Matches DSCP IP header field.
        /// </summary>
        [TikProperty("dscp", WinboxLabel = "DSCP")]
        public TikNegatableField<int?> Dscp { get; set; }

        /// <summary>
        /// dst-address-list: Matches destination address of a packet against a user-defined address list.
        /// </summary>
        [TikProperty("dst-address-list", WinboxLabel = "Dst. Address List")]
        public TikNegatableField<string?> DstAddressList { get; set; }

        /// <summary>
        /// dst-address-type: Matches destination address type:
        /// unicast - IP address used for point to point transmission
        /// local - if dst-address is assigned to one of router's interfaces
        /// broadcast - packet is sent to all devices in subnet
        /// multicast - packet is forwarded to defined group of devices
        /// </summary>
        [TikProperty("dst-address-type", WinboxLabel = "Dst. Address Type")]
        public TikNegatableField<TikValueList<FirewallAddressType>?> DstAddressType { get; set; }

        /// <summary>
        /// dst-limit: Matches packets until a given rate is exceeded. Rate is defined as packets per time interval. As opposed to the limit matcher, every flow has its own limit. Flow is defined by mode parameter. Parameters are written in following format: count[/time],burst,mode[/expire].
        /// count - packet count per time interval per flow to match
        /// time - specifies the time interval in which the packet count per flow cannot be exceeded(optional, 1s will be used if not specified)
        /// burst - initial number of packets per flow to match: this number gets recharged by one every time/count, up to this number
        /// mode - this parameter specifies what unique fields define flow(src-address, dst-address, src-and-dst-address, dst-address-and-port, addresses-and-dst-port)
        /// expire - specifies interval after which flow with no packets will be allowed to be deleted(optional)
        /// </summary>
        [TikProperty("dst-limit", WinboxLabel = "Dst. Limit")]
        public TikField<string?> DstLimit { get; set; }

        /// <summary>
        /// fragment: Matches fragmented packets. First (starting) fragment does not count. If connection tracking is enabled there will be no fragments as system automatically assembles every packet
        /// </summary>
        [TikProperty("fragment")]
        public TikField<bool?> Fragment { get; set; }

        /// <summary>
        /// hotspot: Matches packets in a HotSpot scenario by the specified attribute.
        /// </summary>
        [TikProperty("hotspot", WinboxLabel = "Hotspot")]
        public TikField<TikValueList<TikNegatableValue<FirewallHotspotMatch>>?> Hotspot { get; set; }

        /// <summary>
        /// icmp-options: Matches ICMP type:code fields.
        /// </summary>
        [TikProperty("icmp-options", WinboxLabel = "ICMP Options")]
        public TikNegatableField<string?> IcmpOptions { get; set; }

        /// <summary>
        /// in-bridge-port: Actual interface the packet has entered the router, if incoming interface is bridge. Works only if use-ip-firewall is enabled in bridge settings.
        /// </summary>
        [TikProperty("in-bridge-port", WinboxLabel = "In. Bridge Port")]
        public TikNegatableField<string?> InBridgePort { get; set; }

        /// <summary>
        /// in-bridge-port-list: Matches in-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("in-bridge-port-list", WinboxLabel = "In. Bridge Port List")]
        public TikNegatableField<string?> InBridgePortList { get; set; }

        /// <summary>
        /// in-interface-list: Matches in-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("in-interface-list", WinboxLabel = "In. Interface List")]
        public TikNegatableField<string?> InInterfaceList { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet (VLAN, WMM, MPLS EXP).
        /// </summary>
        [TikProperty("ingress-priority", WinboxLabel = "Ingress Priority")]
        public TikNegatableField<int?> IngressPriority { get; set; }

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
        /// loose-source-routing - match packets with loose source routing option. This option is used to route the internet datagram based on information supplied by the source
        /// no-record-route - match packets with no record route option. This option is used to route the internet datagram based on information supplied by the source
        /// no-router-alert - match packets with no router alert option
        /// no-source-routing - match packets with no source routing option
        /// no-timestamp - match packets with no timestamp option
        /// record-route - match packets with record route option
        /// router-alert - match packets with router alert option
        /// strict-source-routing - match packets with strict source routing option
        /// timestamp - match packets with timestamp
        /// </summary>
        [TikProperty("ipv4-options", WinboxLabel = "IPv4 Options")]
        public TikField<string?> Ipv4Options { get; set; }

        /// <summary>
        /// jump-target: Name of the target chain to jump to. Applicable only if action=jump.
        /// </summary>
        [TikProperty("jump-target", WinboxLabel = "Jump Target")]
        public TikField<string?> JumpTarget { get; set; }

        /// <summary>
        /// layer7-protocol: Layer7 filter name defined in  layer7 protocol menu.
        /// </summary>
        [TikProperty("layer7-protocol", WinboxLabel = "Layer7 Protocol")]
        public TikNegatableField<string?> Layer7Protocol { get; set; }

        /// <summary>
        /// limit: Matches packets at a limited rate. Rule using this matcher will match until this limit is reached. Parameters are written in following format: count[/time],burst.
        /// count - packet count per time interval to match
        /// time - specifies the time interval in which the packet count cannot be exceeded(optional, 1s will be used if not specified)
        /// burst - initial number of packets to match: this number gets recharged by one every time/count, up to this number
        /// </summary>
        [TikProperty("limit", WinboxLabel = "Limit")]
        public TikNegatableField<string?> Limit { get; set; }

        /// <summary>
        /// log: Whether to log matched packets (shorthand flag; use action=log for full log action).
        /// </summary>
        [TikProperty("log", DefaultValue = "no", WinboxLabel = "Log")]
        public TikField<bool?> Log { get; set; }

        /// <summary>
        /// log-prefix: Adds specified text at the beginning of every log message. Applicable if action=log or log=yes.
        /// </summary>
        [TikProperty("log-prefix", WinboxLabel = "Log Prefix")]
        public TikField<string?> LogPrefix { get; set; }

        /// <summary>
        /// nth: Matches every nth packet.
        /// </summary>
        [TikProperty("nth", WinboxLabel = "Nth")]
        public TikNegatableField<string?> Nth { get; set; }

        /// <summary>
        /// out-bridge-port: Actual interface the packet is leaving the router, if outgoing interface is bridge. Works only if use-ip-firewall is enabled in bridge settings.
        /// </summary>
        [TikProperty("out-bridge-port", WinboxLabel = "Out. Bridge Port")]
        public TikNegatableField<string?> OutBridgePort { get; set; }

        /// <summary>
        /// out-bridge-port-list: Matches out-bridge-port against a user-defined interface list.
        /// </summary>
        [TikProperty("out-bridge-port-list", WinboxLabel = "Out. Bridge Port List")]
        public TikNegatableField<string?> OutBridgePortList { get; set; }

        /// <summary>
        /// out-interface-list: Matches out-interface against a user-defined interface list.
        /// </summary>
        [TikProperty("out-interface-list", WinboxLabel = "Out. Interface List")]
        public TikNegatableField<string?> OutInterfaceList { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with particular packet mark. If no-mark is set, rule will match any unmarked packet.
        /// </summary>
        [TikProperty("packet-mark", WinboxLabel = "Packet Mark")]
        public TikNegatableField<string?> PacketMark { get; set; }

        /// <summary>
        /// packet-size: Matches packets of specified size or size range in bytes.
        /// </summary>
        [TikProperty("packet-size", WinboxLabel = "Packet Size")]
        public TikNegatableField<string?> PacketSize { get; set; }

        /// <summary>
        /// packets: Statistics — total packets matched by this rule (read-only).
        /// </summary>
        [TikProperty("packets", IsReadOnly = true, WinboxLabel = "Packets")]
        public TikField<long?> Packets { get; private set; }

        /// <summary>
        /// per-connection-classifier: PCC matcher divides traffic into equal streams.
        /// </summary>
        [TikProperty("per-connection-classifier", WinboxLabel = "Per Connection Classifier")]
        public TikField<string?> PerConnectionClassifier { get; set; }

        /// <summary>
        /// port: Matches if any (source or destination) port matches the specified list of ports or port ranges. Applicable only if protocol is TCP or UDP
        /// </summary>
        [TikProperty("port")]
        public TikNegatableField<TikValueList<TikPortRange>?> Port { get; set; }

        /// <summary>
        /// priority: Matches packet priority (VLAN or WMM priority tag).
        /// </summary>
        [TikProperty("priority", WinboxLabel = "Priority")]
        public TikNegatableField<string?> Priority { get; set; }

        /// <summary>
        /// psd: Attempts to detect TCP and UDP scans. Parameters are in following format WeightThreshold, DelayThreshold, LowPortWeight, HighPortWeight
        /// WeightThreshold - total weight of the latest TCP/UDP packets with different destination ports coming from the same host to be treated as port scan sequence
        /// DelayThreshold - delay for the packets with different destination ports coming from the same host to be treated as possible port scan subsequence
        /// LowPortWeight - weight of the packets with privileged(&lt;=1024) destination port
        /// HighPortWeight - weight of the packet with non-privileged destination port
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
        [TikProperty("realm", MinRouterOs = "7")]
        public TikNegatableField<string?> Realm { get; set; }

        /// <summary>
        /// routing-mark: Matches packets marked by mangle facility with particular routing mark
        /// </summary>
        [TikProperty("routing-mark", WinboxLabel = "Routing Mark")]
        public TikNegatableField<string?> RoutingMark { get; set; }

        /// <summary>
        /// src-address-type: 
        /// Matches source address type:
        /// unicast - IP address used for point to point transmission
        /// local - if address is assigned to one of router's interfaces
        /// broadcast - packet is sent to all devices in subnet
        /// multicast - packet is forwarded to defined group of devices
        /// </summary>
        [TikProperty("src-address-type", WinboxLabel = "Src. Address Type")]
        public TikNegatableField<TikValueList<FirewallAddressType>?> SrcAddressType { get; set; }

        /// <summary>
        /// src-mac-address: Matches source MAC address of the packet.
        /// </summary>
        [TikProperty("src-mac-address", WinboxLabel = "Src. MAC Address")]
        public TikNegatableField<string?> SrcMacAddress { get; set; }

        /// <summary>
        /// tcp-mss: Matches TCP MSS value of an IP packet.
        /// </summary>
        [TikProperty("tcp-mss", WinboxLabel = "TCP MSS")]
        public TikNegatableField<string?> TcpMss { get; set; }

        /// <summary>
        /// time: Allows to create filter based on the packets' arrival time and date or, for locally generated packets, departure time and date
        /// </summary>
        [TikProperty("time", WinboxLabel = "Time")]
        public TikField<string?> Time { get; set; }

        /// <summary>
        /// tos: Matches the ToS (Type of Service) field of IP header.
        /// </summary>
        [TikProperty("tos", WinboxLabel = "TOS", MinRouterOs = "7")]
        public TikNegatableField<string?> Tos { get; set; }

        /// <summary>
        /// ttl: Matches packets TTL value.
        /// </summary>
        [TikProperty("ttl", WinboxLabel = "TTL")]
        public TikField<string?> Ttl { get; set; }
    }
}
