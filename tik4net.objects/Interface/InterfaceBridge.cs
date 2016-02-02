using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// interface/bridge
    /// Ethernet-like networks (Ethernet, Ethernet over IP, IEEE802.11 in ap-bridge or bridge mode, WDS, VLAN) can be connected together using MAC bridges. The bridge feature allows the interconnection of hosts connected to separate LANs (using EoIP, geographically distributed networks can be bridged as well if any kind of IP network interconnection exists between them) as if they were attached to a single LAN. As bridges are transparent, they do not appear in traceroute list, and no utility can make a distinction between a host working in one LAN and a host working in another LAN if these LANs are bridged (depending on the way the LANs are interconnected, latency and data rate between hosts may vary).
    /// Network loops may emerge (intentionally or not) in complex topologies. Without any special treatment, loops would prevent network from functioning normally, as they would lead to avalanche-like packet multiplication. Each bridge runs an algorithm which calculates how the loop can be prevented. STP and RSTP allows bridges to communicate with each other, so they can negotiate a loop free topology. All other alternative connections that would otherwise form loops, are put to standby, so that should the main connection fail, another connection could take its place. This algorithm exchanges  configuration messages (BPDU - Bridge Protocol Data Unit) periodically, so that all bridges are updated with the newest information about changes in network topology. (R)STP selects a root bridge which is responsible for network reconfiguration, such as blocking and opening ports on other bridges. The root bridge is the bridge with the lowest bridge ID.
    /// </summary>
    [TikEntity("interface/bridge")]
    public class InterfaceBridge
    {
        #region Submenu classes
        /// <summary>
        /// interface/bridge/settings: 
        /// </summary>
        [TikEntity("interface/bridge/settings", IsSingleton = true)]
        public class BridgeSettings
        {
            /// <summary>
            /// allow-fast-path: Allows  fast path
            /// </summary>
            [TikProperty("allow-fast-path", DefaultValue = "yes")]
            public bool AllowFastPath { get; set; }

            /// <summary>
            /// use-ip-firewall: Force bridged traffic to also be processed by prerouting, forward and postrouting sections of IP routing (http://wiki.mikrotik.com/wiki/Manual:Packet_Flow_v6). This does not apply to routed traffic.
            /// </summary>
            [TikProperty("use-ip-firewall", DefaultValue = "no")]
            public bool UseIpFirewall { get; set; }

            /// <summary>
            /// use-ip-firewall-for-pppoe: Send bridged un-encrypted PPPoE traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
            /// </summary>
            [TikProperty("use-ip-firewall-for-pppoe", DefaultValue = "no")]
            public bool UseIpFirewallForPppoe { get; set; }

            /// <summary>
            /// use-ip-firewall-for-vlan: Send bridged VLAN traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
            /// </summary>
            [TikProperty("use-ip-firewall-for-vlan", DefaultValue = "no")]
            public bool UseIpFirewallForVlan { get; set; }
        }

        /// <summary>
        /// interface/bridge/port: Port submenu is used to enslave interfaces in a particular bridge interface.
        /// </summary>
        [TikEntity("interface/bridge/port")]
        public class BridgePort
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; private set; }

            /// <summary>
            /// interface: Name of the interface
            /// </summary>
            [TikProperty("interface")]
            public string/*name*/ Interface { get; set; }

            /// <summary>
            /// bridge:  The bridge interface the respective interface is grouped in
            /// </summary>
            [TikProperty("bridge")]
            public string/*name*/ Bridge { get; set; }

            /// <summary>
            /// priority: The priority of the interface in comparison with other going to the same subnet
            /// </summary>
            [TikProperty("priority", DefaultValue = "128")]
            public int/*integer: 0..255*/ Priority { get; set; }

            /// <summary>
            /// path-cost: Path cost to the interface, used by STP to determine the "best" path
            /// </summary>
            [TikProperty("path-cost", DefaultValue = "10")]
            public int/*integer: 0..65535*/ PathCost { get; set; }

            /// <summary>
            /// horizon: Use split horizon bridging to prevent bridging loops.  read more»
            /// </summary>
            [TikProperty("horizon", DefaultValue = "none")]
            public string/*none | integer 0..429496729*/ Horizon { get; set; }

            /// <summary>
            /// edge: Set port as edge port or non-edge port, or enable automatic detection. Edge ports are connected to a LAN that has no other bridges attached. If the port is configured to discover edge port then as soon as the bridge detects a BPDU coming to an edge port, the port becomes a non-edge port.
            /// </summary>
            [TikProperty("edge", DefaultValue = "auto")]
            public string/*auto | no | no-discover | yes | yes-discover*/ Edge { get; set; }

            /// <summary>
            /// point-to-point: 
            /// </summary>
            [TikProperty("point-to-point", DefaultValue = "auto")]
            public string/*auto | yes | no*/ PointToPoint { get; set; }

            /// <summary>
            /// external-fdb: Whether to use wireless registration table to speed up bridge host learning
            /// </summary>
            [TikProperty("external-fdb", DefaultValue = "auto")]
            public string/*auto | no | yes*/ ExternalFdb { get; set; }

            /// <summary>
            /// auto-isolate: Prevents STP blocking port from erroneously moving into a forwarding state if no BPDU's are received on the bridge.
            /// </summary>
            [TikProperty("auto-isolate", DefaultValue = "no")]
            public bool AutoIsolate { get; set; }

            /// <summary>
            /// ctor
            /// </summary>
            public BridgePort()
            {
                Priority = 0x80;
                PathCost = 10;
                Horizon = "none";
            }
        }

        /// <summary>
        /// Base class for <seealso cref="BridgeFilter"/> and <seealso cref="BridgeNat"/> with common fields.
        /// </summary>
        public abstract class BridgeFirewallBase
        {
            /// <summary>
            /// .id: primary key of row
            /// </summary>
            [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
            public string Id { get; protected set; }

            /// <summary>
            /// Firewall chain type - <see cref="Chain"/>
            /// </summary>
            /// <seealso cref="Chain"/>
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
            /// chain: Bridge firewall chain, which the filter is functioning in (either a built-in one, or a user defined)
            /// </summary>
            /// <seealso cref="ChainType"/>
            [TikProperty("chain")]
            public string/*text*/ Chain { get; set; }

            /// <summary>
            /// in-bridge: Bridge interface through which the packet is coming in
            /// </summary>
            [TikProperty("in-bridge", UnsetOnDefault = true)]
            public string/*name*/ InBridge { get; set; }

            /// <summary>
            /// in-interface: Physical interface (i.e., bridge port) through which the packet is coming in
            /// </summary>
            [TikProperty("in-interface", UnsetOnDefault = true)]
            public string/*name*/ InInterface { get; set; }

            /// <summary>
            /// out-bridge: Outgoing bridge interface
            /// </summary>
            [TikProperty("out-bridge", UnsetOnDefault = true)]
            public string/*name*/ OutBridge { get; set; }

            /// <summary>
            /// out-interface: Interface that the packet is leaving the bridge through
            /// </summary>
            [TikProperty("out-interface", UnsetOnDefault = true)]
            public string/*name*/ OutInterface { get; set; }

            /// <summary>
            /// src-mac-address: Source MAC address
            /// </summary>
            [TikProperty("src-mac-address", UnsetOnDefault = true)]
            public string/*MAC address*/ SrcMacAddress { get; set; }

            /// <summary>
            /// dst-mac-address: Destination MAC address
            /// </summary>
            [TikProperty("dst-mac-address", UnsetOnDefault = true)]
            public string/*MAC address*/ DstMacAddress { get; set; }

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
            [TikProperty("mac-protocol", UnsetOnDefault = true)]
            public string/*802.2 | arp | ip | ipv6 | ipx | length | mpls-multicast | mpls-unicast | pppoe | pppoe-discovery | rarp | vlan or integer: 0..65535 decimal format or 0x0000-0xffff hex format*/ MacProtocol { get; set; }

            /// <summary>
            /// src-address: Source IP address (only if MAC protocol is set to IPv4)
            /// </summary>
            [TikProperty("src-address", UnsetOnDefault = true)]
            public string/*IP address*/ SrcAddress { get; set; }

            /// <summary>
            /// src-port: Source port number or range (only for TCP or UDP protocols)
            /// </summary>
            [TikProperty("src-port", UnsetOnDefault = true)]
            public string/*integer 0..65535*/ SrcPort { get; set; }

            /// <summary>
            /// dst-address: Destination IP address (only if MAC protocol is set to IPv4)
            /// </summary>
            [TikProperty("dst-address", UnsetOnDefault = true)]
            public string/*IP address*/ DstAddress { get; set; }

            /// <summary>
            /// dst-port: Destination port number or range (only for TCP or UDP protocols)
            /// </summary>
            [TikProperty("dst-port", UnsetOnDefault = true)]
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
            [TikProperty("ip-protocol", UnsetOnDefault = true)]
            public string/*ddp | egp | encap | etherip | ggp | gre | hmp | icmp | icmpv6 | idpr-cmtp | igmp | ipencap | ipip | ipsec-ah | ipsec-esp | ipv6 | ipv6-frag | ipv6-nonxt | ipv6-opts | ipv6-route | iso-tp4 | l2tp | ospf | pim | pup | rdp | rspf | rsvp | st | tcp | udp | vmtp | vrrp | xns-idp | xtp*/ IpProtocol { get; set; }

            /// <summary>
            /// packet-mark: Matches packets marked via mangle facility with particular packet mark. If no-mark is set, rule will match any unmarked packet.
            /// </summary>
            [TikProperty("packet-mark", UnsetOnDefault = true)]
            public string PacketMark { get; set; }

            /// <summary>
            /// ingress-priority: Matches ingress priority of the packet. Priority may be derived from VLAN, WMM or MPLS EXP bit.  read more»
            /// </summary>
            [TikProperty("ingress-priority")]
            public string/*integer 0..63*/ IngressPriority { get; set; }

            /// <summary>
            /// comment
            /// </summary>
            [TikProperty("comment")]
            public string Comment { get; set; }
        }

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
        /// nat - bridge network address translation provides ways for changing source/destination MAC addresses of the packets traversing a bridge. Has two built-in chains:
        /// srcnat - used for "hiding" a host or a network behind a different MAC address. This chain is applied to the packets leaving the router through a bridged interface
        /// dstnat - used for redirecting some packets to other destinations
        /// You can put packet marks in bridge firewall (filter and NAT), which are the same as the packet marks in IP firewall put by '/ip firewall mangle'. In this way, packet marks put by bridge firewall can be used in 'IP firewall', and vice versa.
        /// 
        /// General bridge firewall properties are described in this section. Some parameters that differ between nat and filter rules are described in further sections.
        /// </summary>
        [TikEntity("/interface/bridge/filter")]
        public class BridgeFilter: BridgeFirewallBase
        {
            /// <summary>
            /// Firewall filter action type - <see cref="InterfaceBridge.BridgeFilter.Action"/>
            /// </summary>
            public enum ActionType
            {
                /// <summary>
                /// accept the packet. Packet is not passed to next firewall rule.
                /// </summary>
                [TikEnum("accept")]
                Accept,

                /// <summary>
                /// drop - silently drop the packet
                /// </summary>
                [TikEnum("drop")]
                Drop,

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
                /// place a mark specified by the new-packet-mark parameter on a packet that matches the rule
                /// </summary>
                [TikEnum("mark-packet")]
                MarkPacket,

                /// <summary>
                /// passthrough - ignore this rule and go to next one (useful for statistics).
                /// </summary>
                [TikEnum("passthrough")]
                Passthrough,


                /// <summary>
                /// return - passes control back to the chain from where the jump took place
                /// </summary>
                [TikEnum("return")]
                Return,

                /// <summary>
                /// set-priority - set priority specified by the new-priority parameter on the packets sent out through a link that is capable of transporting priority (VLAN or WMM-enabled wireless interface). 
                /// </summary>
                [TikEnum("set-priority")]
                SetPriority,
            }

            /// <summary>
            /// action: Action to take if packet is matched by the rule: 
            /// accept - accept the packet.Packet is not passed to next firewall rule.
            /// drop - silently drop the packet
            /// jump - jump to the user defined chain specified by the value of jump-target parameter
            /// log - add a message to the system log containing following data: in-interface, out-interface, src-mac, protocol, src-ip:port-&gt;dst-ip:port and length of the packet.After packet is matched it is passed to next rule in the list, similar as passthrough
            /// passthrough - ignore this rule and go to next one (useful for statistics).
            /// return  - passes control back to the chain from where the jump took place
            /// </summary>
            [TikProperty("action", DefaultValue = "accept")]
            public ActionType Action { get; set; }

            /// <summary>
            /// jump-target: If action=jump specified, then specifies the user-defined firewall chain to process the packet
            /// </summary>
            [TikProperty("jump-target")]
            public string/*name*/ JumpTarget { get; set; }

            /// <summary>
            /// log-prefix: Defines the prefix to be printed before the logging information
            /// </summary>
            [TikProperty("log-prefix")]
            public string/*text*/ LogPrefix { get; set; }

            /// <summary>
            /// new-packet-mark
            /// </summary>
            [TikProperty("new-packet-mark")]
            public string NewPacketMark { get; set; }

            /// <summary>
            /// new-priorityne
            /// </summary>
            [TikProperty("new-priority", DefaultValue = "0")]
            public string NewPriority { get; set; }

            /// <summary>
            /// passthrough
            /// </summary>
            [TikProperty("passthrough", DefaultValue = "yes")]
            public bool Passthrough { get; set; }
        }

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
        /// nat - bridge network address translation provides ways for changing source/destination MAC addresses of the packets traversing a bridge. Has two built-in chains:
        /// srcnat - used for "hiding" a host or a network behind a different MAC address. This chain is applied to the packets leaving the router through a bridged interface
        /// dstnat - used for redirecting some packets to other destinations
        /// You can put packet marks in bridge firewall (filter and NAT), which are the same as the packet marks in IP firewall put by '/ip firewall mangle'. In this way, packet marks put by bridge firewall can be used in 'IP firewall', and vice versa.
        /// 
        /// General bridge firewall properties are described in this section. Some parameters that differ between nat and filter rules are described in further sections.
        /// </summary>
        [TikEntity("/interface/bridge/nat")]
        public class BridgeNat : BridgeFirewallBase
        {
            /// <summary>
            /// Bridge NAT action type - <see cref="Action"/>
            /// </summary>
            /// <seealso cref="Action"/>
            public enum ActionType
            {
                /// <summary>
                /// accept - accept the packet.No action, i.e., the packet is passed through without undertaking any action, and no more rules are processed in the relevant list/chain
                /// </summary>
                [TikEnum("accept")]
                Accept,

                /// <summary>
                /// arp-reply - send a reply to an ARP request(any other packets will be ignored by this rule) with the specified MAC address(only valid in dstnat chain)
                /// </summary>
                [TikEnum("arp-reply")]
                ArpReply,

                /// <summary>
                /// drop - silently drop the packet(without sending the ICMP reject message)
                /// </summary>
                [TikEnum("drop")]
                Drop,

                /// <summary>
                /// dst-nat - change destination MAC address of a packet(only valid in dstnat chain)
                /// </summary>
                [TikEnum("dst-nat")]
                DstNat,

                /// <summary>
                /// jump - jump to the chain specified by the value of the jump-target argument
                /// </summary>
                [TikEnum("jump")]
                Jump,

                /// <summary>
                /// log - log the packet
                /// </summary>
                [TikEnum("log")]
                Log,

                /// <summary>
                /// mark - mark the packet to use the mark later
                /// </summary>
                [TikEnum("mark")]
                Mark,

                /// <summary>
                /// passthrough - ignore this rule and go on to the next one. Acts the same way as a disabled rule, except for ability to count packets
                /// </summary>
                [TikEnum("passthrough")]
                Passthrough,

                /// <summary>
                /// redirect - redirect the packet to the bridge itself (only valid in dstnat chain)
                /// </summary>
                [TikEnum("redirect")]
                Redirect,

                /// <summary>
                /// return - return to the previous chain, from where the jump took place
                /// </summary>
                [TikEnum("return")]
                Return,

                /// <summary>
                /// set-priority - set priority specified by the new- priority parameter on the packets sent out through a link that is capable of transporting priority(VLAN or WMM - enabled wireless interface). Read more>
                /// </summary>
                [TikEnum("set-priority")]
                SetPriority,

                /// <summary>
                /// src-nat - change source MAC address of a packet(only valid in srcnat chain)
                /// </summary>
                [TikEnum("src-nat")]
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
            [TikProperty("action", DefaultValue = "accept")]
            public ActionType Action { get; set; }

            /// <summary>
            /// to-arp-reply-mac-address: Source MAC address to put in Ethernet frame and ARP payload, when action=arp-reply is selected
            /// </summary>
            [TikProperty("to-arp-reply-mac-address")]
            public string/*MAC address*/ ToArpReplyMacAddress { get; set; }

            /// <summary>
            /// to-dst-mac-address: Destination MAC address to put in Ethernet frames, when action=dst-nat is selected
            /// </summary>
            [TikProperty("to-dst-mac-address")]
            public string/*MAC address*/ ToDstMacAddress { get; set; }

            /// <summary>
            /// to-src-mac-address: Source MAC address to put in Ethernet frames, when action=src-nat is selected
            /// </summary>
            [TikProperty("to-src-mac-address")]
            public string/*MAC address*/ ToSrcMacAddress { get; set; }
        }

        #endregion


        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// admin-mac: Static MAC address of the bridge (takes effect if auto-mac=no)
        /// </summary>
        [TikProperty("admin-mac")]
        public string/*MAC address*/ AdminMac { get; set; }

        /// <summary>
        /// ageing-time: How long a host's information will be kept in the bridge database
        /// </summary>
        [TikProperty("ageing-time", DefaultValue = "00:05:00")]
        public string/*time*/ AgeingTime { get; set; }

        /// <summary>
        /// Address Resolution Protocol setting  
        /// </summary>
        /// <seealso cref="Arp"/>
        public enum ArpMode
        {
            /// <summary>
            /// disabled - the interface will not use ARP
            /// </summary>
            [TikEnum("disabled")]
            Disabled,
            /// <summary>
            /// enabled - the interface will use ARP
            /// </summary>
            [TikEnum("enabled")]
            Enabled,
            /// <summary>
            /// proxy-arp - the interface will use the ARP proxy feature
            /// </summary>
            [TikEnum("proxy-arp")]
            ProxyArp,
            /// <summary>
            /// reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the "/ip arp" table. No dynamic entries will be automatically stored in the "/ip arp" table. Therefore for communications to be successful, a valid static entry must already exist.
            /// </summary>
            [TikEnum("reply-only")]
            ReplyOnly
        }

        /// <summary>
        /// arp
        /// Address Resolution Protocol setting    
        ///          disabled - the interface will not use ARP
        ///          enabled - the interface will use ARP
        ///          proxy-arp - the interface will use the ARP proxy feature
        ///          reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the "/ip arp" table. No dynamic entries will be automatically stored in the "/ip arp" table. Therefore for communications to be successful, a valid static entry must already exist.
        /// </summary>
        /// <seealso cref="ArpMode"/>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>
        /// auto-mac: Automatically select the smallest MAC address of bridge ports as a bridge MAC address
        /// </summary>
        [TikProperty("auto-mac", DefaultValue = "yes")]
        public bool AutoMac { get; set; }

        /// <summary>
        /// forward-delay: Time which is spent during the initialization phase of the bridge interface (i.e., after router startup or enabling the interface) in listening/learning state before the bridge will start functioning normally
        /// </summary>
        [TikProperty("forward-delay", DefaultValue = "00:00:15")]
        public string/*time*/ ForwardDelay { get; set; }

        /// <summary>
        /// l2mtu: Layer2 Maximum transmission unit.  read more&#187; 
        /// </summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string/*integer; read-only*/ L2mtu { get;}

        /// <summary>
        /// max-message-age: How long to remember Hello messages received from other bridges
        /// </summary>
        [TikProperty("max-message-age", DefaultValue = "00:00:20")]
        public string/*time*/ MaxMessageAge { get; set; }

        /// <summary>
        /// mtu: Maximum Transmission Unit
        /// </summary>
        [TikProperty("mtu", DefaultValue = "1500")]
        public int Mtu { get; set; }

        /// <summary>
        /// name: Name of the bridge interface
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string/*text*/ Name { get; set; }

        /// <summary>
        /// priority
        /// Spanning tree protocol priority for bridge interface. Bridge with the smallest (lowest) bridge ID becomes a Root-Bridge. Bridge ID consists of two numbers - priority and MAC address of the bridge. To compare two bridge IDs, the priority is compared first. If two bridges have equal priority, then the MAC addresses are compared.
        /// </summary>
        [TikProperty("priority", DefaultValue = "8000")]
        public string/*integer: 0..65535 decimal format or 0x0000-0xffff hex format*/ Priority { get; set; }

        /// <summary>
        /// protocol-mode: Select Spanning tree protocol (STP) or Rapid spanning tree protocol (RSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change.
        /// </summary>
        /// <seealso cref="ProtocolMode"/>
        public enum ProtocolModeModes
        {
            /// <summary>
            /// 
            /// </summary>
            [TikEnum("none")]
            None,
            /// <summary>
            /// rstp - Select Spanning tree protocol (STP)
            /// </summary>
            [TikEnum("rstp")]
            Rstp,
            /// <summary>
            /// rstp - Rapid spanning tree protocol (RSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change.
            /// </summary>
            [TikEnum("stp")]
            Stp,
        }

        /// <summary>
        /// protocol-mode: Select Spanning tree protocol (STP) or Rapid spanning tree protocol (RSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change.
        /// </summary>
        /// <seealso cref="ProtocolModeModes"/>
        [TikProperty("protocol-mode", DefaultValue = "rstp")]
        public ProtocolModeModes ProtocolMode { get; set; }

        /// <summary>
        /// transmit-hold-count: The Transmit Hold Count used by the Port Transmit state machine to limit transmission rate
        /// </summary>
        [TikProperty("transmit-hold-count", DefaultValue = "6")]
        public int/*integer: 1..10*/ TransmitHoldCount { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public InterfaceBridge()
        {
            AgeingTime = "00:05:00";
            Arp = ArpMode.Enabled;
            AutoMac = true;
            ForwardDelay = "00:00:15";
            MaxMessageAge = "00:00:20";
            Mtu = 1500;
            Priority = "8000";
            ProtocolMode = ProtocolModeModes.Rstp;
            TransmitHoldCount = 6;
        }
    }
}
