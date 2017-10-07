using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
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
    public class BridgeFilter : BridgeFirewallBase
    {
        /// <summary>
        /// Firewall filter action type - <see cref="Action"/>
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
}
