﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /ip/firewall/filter
    /// </summary>
    [RosRecord("/ip/firewall/filter", IncludeDetails = true, IsOrdered = true)]
    public class IpFirewallFilter : ISetRecord {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

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
        [RosProperty("action", IsRequired = true, DefaultValue = "accept")]
        public ActionType Action { get; set; } = ActionType.Accept;

        /// <summary>
        /// address-list: Name of the address list to be used. Applicable if action is add-dst-to-address-list or add-src-to-address-list 
        /// </summary>
        [RosProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// address-list-timeout: Time interval after which the address will be removed from the address list specified by address-list parameter. Used in conjunction with add-dst-to-address-list or add-src-to-address-list actions
        /// Value of 00:00:00 will leave the address in the address list forever
        /// </summary>
        [RosProperty("address-list-timeout", DefaultValue = "00:00:00")] // TODO: Not correct RosDataType
        public string/*time*/ AddressListTimeout { get; set; } = "00:00:00";

        /// <summary>
        /// chain: Specifies to which chain rule will be added. If the input does not match the name of an already defined chain, a new chain will be created. 
        /// </summary>
        /// <seealso cref="ChainType"/>
        [RosProperty("chain")]
        public string/*name*/ Chain { get; set; } = "forward"; // This is not an enum, as custom chains can be created

        /// <summary>
        /// connection-bytes: Matches packets only if a given amount of bytes has been transfered through the particular connection. 0 - means infinity, for example connection-bytes=2000000-0 means that the rule matches if more than 2MB has been transfered through the relevant connection 
        /// </summary>
        [RosProperty("connection-bytes")]
        public string ConnectionBytes { get; set; } = "0-0";

        /// <summary>
        /// connection-limit: Restrict connection limit per address or address block up to and including given value 
        /// </summary>
        [RosProperty("connection-limit", UnsetOnDefault = true)]
        public string ConnectionLimit { get; set; }

        /// <summary>
        /// connection-mark: Matches packets marked via mangle facility with particular connection mark. If no-mark is set, rule will match any unmarked connection.
        /// </summary>
        [RosProperty("connection-mark", UnsetOnDefault = true)]
        public string ConnectionMark { get; set; }

        /// <summary>
        /// connection-rate: Connection Rate is a firewall matcher that allow to capture traffic based on present speed of the connection.  Read more &gt;&gt;
        /// </summary>
        [RosProperty("connection-rate")]
        public string ConnectionRate { get; set; }

        /// <summary>
        /// connection-state: Interprets the connection tracking analysis data for a particular packet:
        /// established - a packet which belongs to an existing connection
        /// invalid - a packet which could not be identified for some reason
        /// new - the packet has started a new connection, or otherwise associated with a connection which has not seen packets in both directions.
        /// related - a packet which is related to, but not part of an existing connection, such as ICMP errors or a packet which begins FTP data connection
        /// </summary>
        [RosProperty("connection-state", UnsetOnDefault = true)]
        public ConnectionStateType? ConnectionState { get; set; }

        /// <summary>
        /// connection-type: Matches packets from related connections based on information from their connection tracking helpers. A relevant connection helper must be enabled under  /ip firewall service-port
        /// </summary>
        [RosProperty("connection-type", UnsetOnDefault = true)]
        public string ConnectionType { get; set; }

        /// <summary>
        /// content: Match packets that contain specified text
        /// </summary>
        [RosProperty("content", UnsetOnDefault = true)]
        public string Content { get; set; }

        /// <summary>
        /// dscp: Matches DSCP IP header field.
        /// </summary>
        [RosProperty("dscp", UnsetOnDefault = true)]
        public long? Dscp { get; set; }

        /// <summary>
        /// dst-address: Matches packets which destination is equal to specified IP or falls into specified IP range.
        /// </summary>
        [RosProperty("dst-address", UnsetOnDefault = true)]
        public string DstAddress { get; set; }

        /// <summary>
        /// dst-address-list: Matches destination address of a packet against user-defined address list
        /// </summary>
        [RosProperty("dst-address-list", UnsetOnDefault = true)]
        public string/*name*/ DstAddressList { get; set; }

        /// <summary>
        /// dst-address-type: Matches destination address type:
        /// unicast - IP address used for point to point transmission
        /// local - if dst-address is assigned to one of router's interfaces
        /// broadcast - packet is sent to all devices in subnet
        /// multicast - packet is forwarded to defined group of devices
        /// </summary>
        [RosProperty("dst-address-type", UnsetOnDefault = true)]
        public string DstAddressType { get; set; }

        /// <summary>
        /// dst-limit: Matches packets until a given rate is exceeded. Rate is defined as packets per time interval. As opposed to the limit matcher, every flow has it's own limit. Flow is defined by mode parameter. Parameters are written in following format: count[/time],burst,mode[/expire].
        /// count - packet count per time interval per flow to match
        /// time - specifies the time interval in which the packet count per flow cannot be exceeded(optional, 1s will be used if not specified)
        /// burst - initial number of packets per flow to match: this number gets recharged by one every time/count, up to this number
        /// mode - this parameter specifies what unique fields define flow(src-address, dst-address, src-and-dst-address, dst-address-and-port, addresses-and-dst-port)
        /// expire - specifies interval after which flow with no packets will be allowed to be deleted(optional)
        /// </summary>
        [RosProperty("dst-limit", UnsetOnDefault = true)]
        public string DstLimit { get; set; }

        /// <summary>
        /// dst-port: List of destination port numbers or port number ranges
        /// </summary>
        [RosProperty("dst-port", UnsetOnDefault = true)]
        public string DstPort { get; set; }

        /// <summary>
        /// fragment: Matches fragmented packets. First (starting) fragment does not count. If connection tracking is enabled there will be no fragments as system automatically assembles every packet
        /// </summary>
        [RosProperty("fragment", UnsetOnDefault = true)]
        public bool? Fragment { get; set; }

        /// <summary>
        /// hotspot: 
        /// </summary>
        [RosProperty("hotspot", UnsetOnDefault = true)]
        public string Hotspot { get; set; }

        /// <summary>
        /// icmp-options: Matches ICMP type:code fileds
        /// </summary>
        [RosProperty("icmp-options", UnsetOnDefault = true)]
        public string IcmpOptions { get; set; }

        /// <summary>
        /// in-bridge-port: Actual interface the packet has entered the router, if incoming interface is bridge. Works only if use-ip-firewall is enabled in bridge settings.
        /// </summary>
        [RosProperty("in-bridge-port", UnsetOnDefault = true)]
        public string/*name*/ InBridgePort { get; set; }

        /// <summary>
        /// in-interface: Interface the packet has entered the router
        /// </summary>
        [RosProperty("in-interface", UnsetOnDefault = true)]
        public string/*name*/ InInterface { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet. Priority may be derived from VLAN, WMM or MPLS EXP bit.  Read more&gt;&gt;
        /// </summary>
        [RosProperty("ingress-priority", UnsetOnDefault = true)]
        public long? IngressPriority { get; set; }

        ///// <summary>
        ///// ipsec-policy: Matches the policy used by IpSec. Value is written in following format: direction, policy. Direction is Used to select whether to match the policy used for decapsulation or the policy that will be used for encapsulation.            
        ///// in - valid in the PREROUTING, INPUT and FORWARD chains
        ///// out - valid in the POSTROUTING, OUTPUT and FORWARD chains
        ///// ipsec - matches if the packet is subject to IPsec processing;
        ///// none - matches ipsec transport packet.
        ///// For example, if router receives Ipsec encapsulated Gre packet, then rule ipsec-policy=in, ipsec will match Gre packet, but rule ipsec-policy=in, none will match ESP packet.
        ///// </summary>
        //[TikProperty("ipsec-policy")]
        //public string IpsecPolicy { get; set; }

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
        [RosProperty("ipv4-options", UnsetOnDefault = true)]
        public string Ipv4Options { get; set; }

        /// <summary>
        /// jump-target: Name of the target chain to jump to. Applicable only if action=jump
        /// </summary>
        [RosProperty("jump-target")]
        public string/*name*/ JumpTarget { get; set; }

        /// <summary>
        /// layer7-protocol: Layer7 filter name defined in  layer7 protocol menu.
        /// </summary>
        [RosProperty("layer7-protocol", UnsetOnDefault = true)]
        public string/*name*/ Layer7Protocol { get; set; }

        /// <summary>
        /// limit: Matches packets at a limited rate. Rule using this matcher will match until this limit is reached. Parameters are written in following format: count[/time],burst.
        /// count - packet count per time interval to match
        /// time - specifies the time interval in which the packet count cannot be exceeded(optional, 1s will be used if not specified)
        /// burst - initial number of packets to match: this number gets recharged by one every time/count, up to this number
        /// </summary>
        [RosProperty("limit", UnsetOnDefault = true)]
        public string Limit { get; set; }

        /// <summary>
        /// log-prefix: Adds specified text at the beginning of every log message. Applicable if action=log
        /// </summary>
        [RosProperty("log-prefix")]
        public string LogPrefix { get; set; }

        /// <summary>
        /// nth: Matches every nth packet.  Read more &gt;&gt;
        /// </summary>
        [RosProperty("nth", UnsetOnDefault = true)]
        public string Nth { get; set; }

        /// <summary>
        /// out-bridge-port: Actual interface the packet is leaving the router, if outgoing interface is bridge. Works only if use-ip-firewall is enabled in bridge settings.
        /// </summary>
        [RosProperty("out-bridge-port", UnsetOnDefault = true)]
        public string/*name*/ OutBridgePort { get; set; }

        /// <summary>
        /// out-interface: Interface the packet is leaving the router
        /// </summary>
        [RosProperty("out-interface", UnsetOnDefault = true)]
        public string OutInterface { get; set; }

        /// <summary>
        /// p2p: Matches packets from various peer-to-peer (P2P) protocols. Does not work on encrypted p2p packets.
        /// </summary>
        [RosProperty("p2p", UnsetOnDefault = true)]
        public string P2p { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with particular packet mark. If no-mark is set, rule will match any unmarked packet.
        /// </summary>
        [RosProperty("packet-mark", UnsetOnDefault = true)]
        public string PacketMark { get; set; }

        /// <summary>
        /// packet-size: Matches packets of specified size or size range in bytes.
        /// </summary>
        [RosProperty("packet-size", UnsetOnDefault = true)]
        public string PacketSize { get; set; }

        /// <summary>
        /// per-connection-classifier: PCC matcher allows to divide traffic into equal streams with ability to keep packets with specific set of options in one particular stream.  Read more &gt;&gt;
        /// </summary>
        [RosProperty("per-connection-classifier", UnsetOnDefault = true)]
        public string PerConnectionClassifier { get; set; }

        /// <summary>
        /// port: Matches if any (source or destination) port matches the specified list of ports or port ranges. Applicable only if protocol is TCP or UDP
        /// </summary>
        [RosProperty("port", UnsetOnDefault = true)]
        public string Port { get; set; }

        /// <summary>
        /// protocol: Matches particular IP protocol specified by protocol name or number
        /// </summary>
        [RosProperty("protocol", DefaultValue = "tcp", UnsetOnDefault = true)]
        public string Protocol { get; set; }

        /// <summary>
        /// psd: Attempts to detect TCP and UDP scans. Parameters are in following format WeightThreshold, DelayThreshold, LopPortWeight, HighPortWeight
        /// WeightThreshold - total weight of the latest TCP/UDP packets with different destination ports coming from the same host to be treated as port scan sequence
        /// DelayThreshold - delay for the packets with different destination ports coming from the same host to be treated as possible port scan subsequence
        /// LowPortWeight - weight of the packets with privileged(&lt;=1024) destination port
        /// HighPortWeight - weight of the packet with non-priviliged destination port
        /// </summary>
        [RosProperty("psd", UnsetOnDefault = true)]
        public string Psd { get; set; }

        /// <summary>
        /// random: Matches packets randomly with given probability.
        /// </summary>
        [RosProperty("random", UnsetOnDefault = true)]
        public string Random { get; set; }

        /// <summary>
        /// reject-with: Specifies error to be sent back if packet is rejected. Applicable if action=reject
        /// </summary>
        [RosProperty("reject-with")]
        public string RejectWith { get; set; }

        /// <summary>
        /// routing-mark: Matches packets marked by mangle facility with particular routing mark
        /// </summary>
        [RosProperty("routing-mark", UnsetOnDefault = true)]
        public string RoutingMark { get; set; }

        /// <summary>
        /// src-address: Matches packets which source is equal to specified IP or falls into specified IP range.
        /// </summary>
        [RosProperty("src-address", UnsetOnDefault = true)]
        public string SrcAddress { get; set; }

        /// <summary>
        /// src-address-list: Matches source address of a packet against user-defined  address list
        /// </summary>
        [RosProperty("src-address-list", UnsetOnDefault = true)]
        public string/*name*/ SrcAddressList { get; set; }

        /// <summary>
        /// src-address-type: 
        /// Matches source address type:
        /// unicast - IP address used for point to point transmission
        /// local - if address is assigned to one of router's interfaces
        /// broadcast - packet is sent to all devices in subnet
        /// multicast - packet is forwarded to defined group of devices
        /// </summary>
        [RosProperty("src-address-type", UnsetOnDefault = true)]
        public string SrcAddressType { get; set; }

        /// <summary>
        /// src-port: List of source ports and ranges of source ports. Applicable only if protocol is TCP or UDP.
        /// </summary>
        [RosProperty("src-port", UnsetOnDefault = true)]
        public string SrcPort { get; set; }

        /// <summary>
        /// src-mac-address: Matches source MAC address of the packet
        /// </summary>
        [RosProperty("src-mac-address", UnsetOnDefault = true)]
        public string SrcMacAddress { get; set; }

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
        [RosProperty("tcp-flags", UnsetOnDefault = true)]
        public string TcpFlags { get; set; }

        /// <summary>
        /// tcp-mss: Matches TCP MSS value of an IP packet
        /// </summary>
        [RosProperty("tcp-mss", UnsetOnDefault = true)]
        public string TcpMss { get; set; }

        /// <summary>
        /// time: Allows to create filter based on the packets' arrival time and date or, for locally generated packets, departure time and date
        /// </summary>
        [RosProperty("time", UnsetOnDefault = true)]
        public string Time { get; set; }

        /// <summary>
        /// ttl: Matches packets TTL value
        /// </summary>
        [RosProperty("ttl", UnsetOnDefault = true)]
        public string Ttl { get; set; }

        /// <summary>
        /// Row disabled property.
        /// </summary>
        [RosProperty("disabled", IsRequired = true)]
        public bool Disabled { get; set; }
        
        /// <summary>
        /// comment: Descriptive comment for the rule.
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }


        /// <summary>
        /// Row dynamic property.
        /// </summary>
        [RosProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; set; }

        /// <summary>
        /// Row invalid property.
        /// </summary>
        [RosProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; set; }




        /// <summary>
        /// Firewall filter action type - <see cref="IpFirewallFilter.Action"/>
        /// </summary>
        public enum ActionType {
            /// <summary>
            /// accept the packet. Packet is not passed to next firewall rule.
            /// </summary>
            [RosEnum("accept")]
            Accept,

            /// <summary>
            /// add-dst-to-address-list - add destination address to address list specified by address-list parameter
            /// </summary>
            [RosEnum("add-dst-to-address-list")]
            AddDstToAddressList,

            /// <summary>
            /// add-src-to-address-list - add source address to address list specified by address-list parameter
            /// </summary>
            [RosEnum("add-src-to-address-list")]
            AddSrcToAddressList,

            /// <summary>
            /// drop - silently drop the packet
            /// </summary>
            [RosEnum("drop")]
            Drop,

            /// <summary>
            /// jump - jump to the user defined chain specified by the value of jump-target parameter
            /// </summary>
            [RosEnum("jump")]
            Jump,

            /// <summary>
            /// log - add a message to the system log containing following data: in-interface, out-interface, src-mac, protocol, src-ip:port->dst-ip:port and length of the packet.After packet is matched it is passed to next rule in the list, similar as passthrough
            /// </summary>
            [RosEnum("log")]
            Log,

            /// <summary>
            /// passthrough - ignore this rule and go to next one (useful for statistics).
            /// </summary>
            [RosEnum("passthrough")]
            Passthrough,

            /// <summary>
            /// reject - drop the packet and send an ICMP reject message
            /// </summary>
            [RosEnum("reject")]
            Reject,

            /// <summary>
            /// return - passes control back to the chain from where the jump took place
            /// </summary>
            [RosEnum("return")]
            Return,

            /// <summary>
            /// tarpit - captures and holds TCP connections(replies with SYN/ACK to the inbound TCP SYN packet)
            /// </summary>
            [RosEnum("tarpit")]
            Tarpit,
        }

        /// <summary>
        /// Firewall filter connection state - <see cref="IpFirewallFilter.ConnectionState"/>
        /// </summary>
        public enum ConnectionStateType {
            /// <summary>
            /// Default when not filled
            /// </summary>
            [RosEnum("")]
            Empty,

            /// <summary>
            /// established - a packet which belongs to an existing connection
            /// </summary>
            [RosEnum("established")]
            Established,

            /// <summary>
            /// invalid - a packet which could not be identified for some reason
            /// </summary>
            [RosEnum("invalid")]
            Invalid,

            /// <summary>
            /// new - the packet has started a new connection, or otherwise associated with a connection which has not seen packets in both directions.
            /// </summary>
            [RosEnum("new")]
            New,

            /// <summary>
            /// related - a packet which is related to, but not part of an existing connection, such as ICMP errors or a packet which begins FTP data connection
            /// </summary>
            [RosEnum("related")]
            Related,
        }
    }
}
