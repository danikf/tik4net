using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// 
    /// The bridge firewall implements packet filtering and thereby provides security functions that are used to manage data flow to, from and through bridge.
    /// 
    /// Packet flow diagram shows how packets are processed through router. It is possible to force bridge traffic to go through /ip firewall filter rules (see: Bridge Settings)
    /// 
    /// There are two bridge firewall tables:
    /// 
    /// filter - bridge firewall with three predefined chains:
    /// input - filters packets, where the destination is the bridge (including those packets that will be routed, as they are destined to the bridge MAC address anyway)
    /// output - filters packets, which come from the bridge (including those packets that has been routed normally)
    /// forward - filters packets, which are to be bridged (note: this chain is not applied to the packets that should be routed through the router, just to those that are traversing between the ports of the same bridge)
    /// nat - bridge Option address translation provides ways for changing source/destination MAC addresses of the packets traversing a bridge. Has two built-in chains:
    /// srcnat - used for "hiding" a host or a Option behind a different MAC address. This chain is applied to the packets leaving the router through a bridged interface
    /// dstnat - used for redirecting some packets to other destinations
    /// You can put packet marks in bridge firewall (filter and NAT), which are the same as the packet marks in IP firewall put by '/ip firewall mangle'. In this way, packet marks put by bridge firewall can be used in 'IP firewall', and vice versa.
    /// 
    /// General bridge firewall properties are described in this section. Some parameters that differ between nat and filter rules are described in further sections.
    /// </summary>
    [RosRecord("/interface/bridge/nat")]
    public class BridgeNat : SetRecordBase {
        /// <summary>
        /// chain: Bridge firewall chain, which the filter is functioning in (either a built-in one, or a user defined)
        /// </summary>
        /// <seealso cref="BridgeChainType"/>
        [RosProperty("chain")]
        public string/*text*/ Chain { get; set; }

        /// <summary>
        /// in-bridge: Bridge interface through which the packet is coming in
        /// </summary>
        [RosProperty("in-bridge", UnsetOnDefault = true)]
        public string/*name*/ InBridge { get; set; }

        /// <summary>
        /// in-interface: Physical interface (i.e., bridge port) through which the packet is coming in
        /// </summary>
        [RosProperty("in-interface", UnsetOnDefault = true)]
        public string/*name*/ InInterface { get; set; }

        /// <summary>
        /// out-bridge: Outgoing bridge interface
        /// </summary>
        [RosProperty("out-bridge", UnsetOnDefault = true)]
        public string/*name*/ OutBridge { get; set; }

        /// <summary>
        /// out-interface: Interface that the packet is leaving the bridge through
        /// </summary>
        [RosProperty("out-interface", UnsetOnDefault = true)]
        public string/*name*/ OutInterface { get; set; }

        /// <summary>
        /// src-mac-address: Source MAC address
        /// </summary>
        [RosProperty("src-mac-address", UnsetOnDefault = true)]
        public string/*MAC address*/ SrcMacAddress { get; set; }

        /// <summary>
        /// dst-mac-address: Destination MAC address
        /// </summary>
        [RosProperty("dst-mac-address", UnsetOnDefault = true)]
        public string/*MAC address*/ DstMacAddress { get; set; }

        /// <summary>
        /// mac-protocol
        /// Ethernet payload type (MAC-level protocol)
        /// 802.2
        /// arp - Type 0x0806 - ARP
        /// ip - Type 0x0800 - IPv4
        /// ipv6 - Type 0x86dd - IPv6
        /// ipx - Type 0x8137 - "InterOption Packet Exchange"
        /// length
        /// mpls-multicast - Type 0x8848 - MPLS Multicast
        /// mpls-unicast - Type 0x8847 - MPLS Unicast
        /// ppoe - Type 0x8864 - PPPoE Session
        /// ppoe-discovery - Type 0x8863 - PPPoE Discovery
        /// rarp - Type 0x8035 - Reverse ARP
        /// vlan - Type 0x8100 - 802.1Q tagged VLAN
        /// </summary>
        [RosProperty("mac-protocol", UnsetOnDefault = true)]
        public string/*802.2 | arp | ip | ipv6 | ipx | length | mpls-multicast | mpls-unicast | pppoe | pppoe-discovery | rarp | vlan or integer: 0..65535 decimal format or 0x0000-0xffff hex format*/ MacProtocol { get; set; }

        /// <summary>
        /// src-address: Source IP address (only if MAC protocol is set to IPv4)
        /// </summary>
        [RosProperty("src-address", UnsetOnDefault = true)]
        public string/*IP address*/ SrcAddress { get; set; }

        /// <summary>
        /// src-port: Source port number or range (only for TCP or UDP protocols)
        /// </summary>
        [RosProperty("src-port", UnsetOnDefault = true)]
        public string/*integer 0..65535*/ SrcPort { get; set; }

        /// <summary>
        /// dst-address: Destination IP address (only if MAC protocol is set to IPv4)
        /// </summary>
        [RosProperty("dst-address", UnsetOnDefault = true)]
        public string/*IP address*/ DstAddress { get; set; }

        /// <summary>
        /// dst-port: Destination port number or range (only for TCP or UDP protocols)
        /// </summary>
        [RosProperty("dst-port", UnsetOnDefault = true)]
        public string/*integer 0..65535*/ DstPort { get; set; }

        /// <summary>
        /// ip-protocol
        /// 
        /// IP protocol (only if MAC protocol is set to IPv4)
        /// 
        /// ddp - datagram delivery protocol
        /// egp - exterior gateway protocol
        /// encap - ip encapsulation
        /// etherip - 
        /// ggp - gateway-gateway protocol
        /// gre - general routing encapsulation
        /// hmp - host monitoring protocol
        /// icmp - IPv4 internet control message protocol
        /// icmpv6 - IPv6 internet control message protocol
        /// idpr-cmtp - idpr control message transport
        /// igmp - internet group management protocol
        /// ipencap - ip encapsulated in ip
        /// ipip - ip encapsulation
        /// ipsec-ah - IPsec AH protocol
        /// ipsec-esp - IPsec ESP protocol
        /// ipv6 - 
        /// ipv6-frag - 
        /// ipv6-nonxt - 
        /// ipv6-opts - 
        /// ipv6-route - 
        /// iso-tp4 - iso transport protocol class 4 
        /// l2tp - 
        /// ospf - open shortest path first
        /// pim - protocol independent multicast 
        /// pup - parc universal packet protocol 
        /// rspf - radio shortest path first
        /// rsvp - 
        /// rdp - reliable datagram protocol
        /// st - st datagram mode
        /// tcp - transmission control protocol
        /// udp - user datagram protocol
        /// vmtp - versatile message transport
        /// vrrp - Virtual Router Redundancy Protocol
        /// xns-idp - xerox ns idp
        /// xtp – xpress transfer protocol
        ///     
        /// </summary>
        [RosProperty("ip-protocol", UnsetOnDefault = true)]
        public string/*ddp | egp | encap | etherip | ggp | gre | hmp | icmp | icmpv6 | idpr-cmtp | igmp | ipencap | ipip | ipsec-ah | ipsec-esp | ipv6 | ipv6-frag | ipv6-nonxt | ipv6-opts | ipv6-route | iso-tp4 | l2tp | ospf | pim | pup | rdp | rspf | rsvp | st | tcp | udp | vmtp | vrrp | xns-idp | xtp*/ IpProtocol { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with particular packet mark. If no-mark is set, rule will match any unmarked packet.
        /// </summary>
        [RosProperty("packet-mark", UnsetOnDefault = true)]
        public string PacketMark { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet. Priority may be derived from VLAN, WMM or MPLS EXP bit.  read more»
        /// </summary>
        [RosProperty("ingress-priority")]
        public string/*integer 0..63*/ IngressPriority { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// Bridge NAT action type - <see cref="Action"/>
        /// </summary>
        /// <seealso cref="Action"/>
        public enum ActionType {
            /// <summary>
            /// accept - accept the packet.No action, i.e., the packet is passed through without undertaking any action, and no more rules are processed in the relevant list/chain
            /// </summary>
            [RosEnum("accept")]
            Accept,

            /// <summary>
            /// arp-reply - send a reply to an ARP request(any other packets will be ignored by this rule) with the specified MAC address(only valid in dstnat chain)
            /// </summary>
            [RosEnum("arp-reply")]
            ArpReply,

            /// <summary>
            /// drop - silently drop the packet(without sending the ICMP reject message)
            /// </summary>
            [RosEnum("drop")]
            Drop,

            /// <summary>
            /// dst-nat - change destination MAC address of a packet(only valid in dstnat chain)
            /// </summary>
            [RosEnum("dst-nat")]
            DstNat,

            /// <summary>
            /// jump - jump to the chain specified by the value of the jump-target argument
            /// </summary>
            [RosEnum("jump")]
            Jump,

            /// <summary>
            /// log - log the packet
            /// </summary>
            [RosEnum("log")]
            Log,

            /// <summary>
            /// mark - mark the packet to use the mark later
            /// </summary>
            [RosEnum("mark")]
            Mark,

            /// <summary>
            /// passthrough - ignore this rule and go on to the next one. Acts the same way as a disabled rule, except for ability to count packets
            /// </summary>
            [RosEnum("passthrough")]
            Passthrough,

            /// <summary>
            /// redirect - redirect the packet to the bridge itself (only valid in dstnat chain)
            /// </summary>
            [RosEnum("redirect")]
            Redirect,

            /// <summary>
            /// return - return to the previous chain, from where the jump took place
            /// </summary>
            [RosEnum("return")]
            Return,

            /// <summary>
            /// set-priority - set priority specified by the new- priority parameter on the packets sent out through a link that is capable of transporting priority(VLAN or WMM - enabled wireless interface). Read more>
            /// </summary>
            [RosEnum("set-priority")]
            SetPriority,

            /// <summary>
            /// src-nat - change source MAC address of a packet(only valid in srcnat chain)
            /// </summary>
            [RosEnum("src-nat")]
            SrcNat,
        }

        /// <summary>
        /// action: Action to take if packet is matched by the rule: 
        /// accept - accept the packet.No action, i.e., the packet is passed through without undertaking any action, and no more rules are processed in the relevant list/chain
        /// arp-reply - send a reply to an ARP request(any other packets will be ignored by this rule) with the specified MAC address(only valid in dstnat chain)
        /// drop - silently drop the packet(without sending the ICMP reject message)
        /// dst-nat - change destination MAC address of a packet(only valid in dstnat chain)
        /// jump - jump to the chain specified by the value of the jump-target argument
        /// log - log the packet
        /// mark - mark the packet to use the mark later
        /// passthrough - ignore this rule and go on to the next one. Acts the same way as a disabled rule, except for ability to count packets
        /// redirect - redirect the packet to the bridge itself (only valid in dstnat chain)
        /// return - return to the previous chain, from where the jump took place
        /// set-priority - set priority specified by the new- priority parameter on the packets sent out through a link that is capable of transporting priority(VLAN or WMM - enabled wireless interface). Read more>
        /// src-nat - change source MAC address of a packet(only valid in srcnat chain)            
        /// </summary>
        [RosProperty("action")]
        public ActionType Action { get; set; } = ActionType.Accept;

        /// <summary>
        /// to-arp-reply-mac-address: Source MAC address to put in Ethernet frame and ARP payload, when action=arp-reply is selected
        /// </summary>
        [RosProperty("to-arp-reply-mac-address")]
        public string/*MAC address*/ ToArpReplyMacAddress { get; set; }

        /// <summary>
        /// to-dst-mac-address: Destination MAC address to put in Ethernet frames, when action=dst-nat is selected
        /// </summary>
        [RosProperty("to-dst-mac-address")]
        public string/*MAC address*/ ToDstMacAddress { get; set; }

        /// <summary>
        /// to-src-mac-address: Source MAC address to put in Ethernet frames, when action=src-nat is selected
        /// </summary>
        [RosProperty("to-src-mac-address")]
        public string/*MAC address*/ ToSrcMacAddress { get; set; }
    }
}
