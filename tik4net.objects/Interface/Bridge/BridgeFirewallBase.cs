using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// Base class for <seealso cref="BridgeFilter"/> and <seealso cref="BridgeNat"/> with common fields.
    /// </summary>
    public abstract class BridgeFirewallBase
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; protected set; }

        /// <summary>
        /// chain: Bridge firewall chain, which the filter is functioning in (either a built-in one, or a user defined)
        /// </summary>
        /// <seealso cref="BridgeFirewallChainType"/>
        [TikProperty("chain")]
        public TikValue<string?> Chain { get; set; }

        /// <summary>
        /// in-bridge: Bridge interface through which the packet is coming in
        /// </summary>
        [TikProperty("in-bridge", Negatable = true)]
        public TikValue<string?> InBridge { get; set; }

        /// <summary>
        /// in-interface: Physical interface (i.e., bridge port) through which the packet is coming in
        /// </summary>
        [TikProperty("in-interface", Negatable = true)]
        public TikValue<string?> InInterface { get; set; }

        /// <summary>
        /// out-bridge: Outgoing bridge interface
        /// </summary>
        [TikProperty("out-bridge", Negatable = true)]
        public TikValue<string?> OutBridge { get; set; }

        /// <summary>
        /// out-interface: Interface that the packet is leaving the bridge through
        /// </summary>
        [TikProperty("out-interface", Negatable = true)]
        public TikValue<string?> OutInterface { get; set; }

        /// <summary>
        /// src-mac-address: Source MAC address
        /// </summary>
        [TikProperty("src-mac-address", Negatable = true)]
        public TikValue<string?> SrcMacAddress { get; set; }

        /// <summary>
        /// dst-mac-address: Destination MAC address
        /// </summary>
        [TikProperty("dst-mac-address", Negatable = true)]
        public TikValue<string?> DstMacAddress { get; set; }

        /// <summary>
        /// mac-protocol
        /// Ethernet payload type (MAC-level protocol)
        /// 802.2
        /// arp - Type 0x0806 - ARP
        /// ip - Type 0x0800 - IPv4
        /// ipv6 - Type 0x86dd - IPv6
        /// ipx - Type 0x8137 - "Internetwork Packet Exchange"
        /// length
        /// mpls-multicast - Type 0x8848 - MPLS Multicast
        /// mpls-unicast - Type 0x8847 - MPLS Unicast
        /// ppoe - Type 0x8864 - PPPoE Session
        /// ppoe-discovery - Type 0x8863 - PPPoE Discovery
        /// rarp - Type 0x8035 - Reverse ARP
        /// vlan - Type 0x8100 - 802.1Q tagged VLAN
        /// </summary>
        [TikProperty("mac-protocol", Negatable = true)]
        public TikValue<string?> MacProtocol { get; set; }

        /// <summary>
        /// src-address: Source IP address (only if MAC protocol is set to IPv4)
        /// </summary>
        [TikProperty("src-address", Negatable = true)]
        public TikValue<string?> SrcAddress { get; set; }

        /// <summary>
        /// src-port: Source port number or range (only for TCP or UDP protocols)
        /// </summary>
        [TikProperty("src-port", Negatable = true)]
        public TikValue<string?> SrcPort { get; set; }

        /// <summary>
        /// dst-address: Destination IP address (only if MAC protocol is set to IPv4)
        /// </summary>
        [TikProperty("dst-address", Negatable = true)]
        public TikValue<string?> DstAddress { get; set; }

        /// <summary>
        /// dst-port: Destination port number or range (only for TCP or UDP protocols)
        /// </summary>
        [TikProperty("dst-port", Negatable = true)]
        public TikValue<string?> DstPort { get; set; }

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
        [TikProperty("ip-protocol")]
        public TikValue<string?> IpProtocol { get; set; }

        /// <summary>
        /// packet-mark: Matches packets marked via mangle facility with particular packet mark. If no-mark is set, rule will match any unmarked packet.
        /// </summary>
        [TikProperty("packet-mark", Negatable = true)]
        public TikValue<string?> PacketMark { get; set; }

        /// <summary>
        /// ingress-priority: Matches ingress priority of the packet. Priority may be derived from VLAN, WMM or MPLS EXP bit.  read more»
        /// </summary>
        [TikProperty("ingress-priority", Negatable = true)]
        public TikValue<string?> IngressPriority { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }
    }
}
