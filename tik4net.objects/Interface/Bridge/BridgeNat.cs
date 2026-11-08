using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// /interface/bridge/nat: a bridge NAT rule, which changes the source or destination MAC address of packets traversing a bridge.
    /// </summary>
    /// <remarks>
    /// <para>The bridge firewall implements packet filtering and thereby provides security functions that are used to manage data flow to, from and through bridge.</para>
    /// <para>Packet flow diagram shows how packets are processed through router. It is possible to force bridge traffic to go through /ip firewall filter rules (see: Bridge Settings)</para>
    /// <para>There are two bridge firewall tables:</para>
    /// <list type="bullet">
    /// <item><description>filter - bridge firewall with three predefined chains:</description></item>
    /// <item><description>input - filters packets, where the destination is the bridge (including those packets that will be routed, as they are destined to the bridge MAC address anyway)</description></item>
    /// <item><description>output - filters packets, which come from the bridge (including those packets that has been routed normally)</description></item>
    /// <item><description>forward - filters packets, which are to be bridged (note: this chain is not applied to the packets that should be routed through the router, just to those that are traversing between the ports of the same bridge)</description></item>
    /// <item><description>nat - bridge network address translation provides ways for changing source/destination MAC addresses of the packets traversing a bridge. Has two built-in chains:</description></item>
    /// <item><description>srcnat - used for "hiding" a host or a network behind a different MAC address. This chain is applied to the packets leaving the router through a bridged interface</description></item>
    /// <item><description>dstnat - used for redirecting some packets to other destinations</description></item>
    /// </list>
    /// <para>You can put packet marks in bridge firewall (filter and NAT), which are the same as the packet marks in IP firewall put by '/ip firewall mangle'. In this way, packet marks put by bridge firewall can be used in 'IP firewall', and vice versa.</para>
    /// <para>General bridge firewall properties are described in this section. Some parameters that differ between nat and filter rules are described in further sections.</para>
    /// </remarks>
    [TikEntity("/interface/bridge/nat", IsOrdered = true)]
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
            /// mark-packet - mark the packet to use the mark later
            /// </summary>
            [TikEnum("mark-packet")]
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
        /// action: what the rule does with a packet it matches; <c>accept</c> by default.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>accept - accept the packet.No action, i.e., the packet is passed through without undertaking any action, and no more rules are processed in the relevant list/chain</description></item>
        /// <item><description>arp-reply - send a reply to an ARP request(any other packets will be ignored by this rule) with the specified MAC address(only valid in dstnat chain)</description></item>
        /// <item><description>drop - silently drop the packet(without sending the ICMP reject message)</description></item>
        /// <item><description>dst-nat - change destination MAC address of a packet(only valid in dstnat chain)</description></item>
        /// <item><description>jump - jump to the chain specified by the value of the jump-target argument</description></item>
        /// <item><description>log - log the packet</description></item>
        /// <item><description>mark - mark the packet to use the mark later</description></item>
        /// <item><description>passthrough - ignore this rule and go on to the next one. Acts the same way as a disabled rule, except for ability to count packets</description></item>
        /// <item><description>redirect - redirect the packet to the bridge itself (only valid in dstnat chain)</description></item>
        /// <item><description>return - return to the previous chain, from where the jump took place</description></item>
        /// <item><description>set-priority - set priority specified by the new- priority parameter on the packets sent out through a link that is capable of transporting priority(VLAN or WMM - enabled wireless interface). Read more></description></item>
        /// <item><description>src-nat - change source MAC address of a packet(only valid in srcnat chain)</description></item>
        /// </list>
        /// </remarks>
        [TikProperty("action", DefaultValue = "accept", WinboxLabel = "Action")]
        public TikField<ActionType?> Action { get; set; }

        /// <summary>
        /// to-arp-reply-mac-address: Source MAC address to put in Ethernet frame and ARP payload, when action=arp-reply is selected
        /// </summary>
        [TikProperty("to-arp-reply-mac-address")]
        public TikField<string?> ToArpReplyMacAddress { get; set; }

        /// <summary>
        /// to-dst-mac-address: Destination MAC address to put in Ethernet frames, when action=dst-nat is selected
        /// </summary>
        [TikProperty("to-dst-mac-address")]
        public TikField<string?> ToDstMacAddress { get; set; }

        /// <summary>
        /// to-src-mac-address: Source MAC address to put in Ethernet frames, when action=src-nat is selected
        /// </summary>
        [TikProperty("to-src-mac-address")]
        public TikField<string?> ToSrcMacAddress { get; set; }
    }
}
