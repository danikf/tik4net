using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
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
    [RosRecord("/interface/bridge/nat")]
    public class BridgeNat : BridgeFirewallBase {
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
        [RosProperty("action", DefaultValue = "accept")]
        public ActionType Action { get; set; }

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
