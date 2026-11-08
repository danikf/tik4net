using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// /interface/bridge/filter: a bridge firewall filter rule, which accepts, drops or marks packets going to, from or through a bridge.
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
    [TikEntity("/interface/bridge/filter", IsOrdered = true)]
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
        /// action: what the rule does with a packet it matches; <c>accept</c> by default.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>accept - accept the packet.Packet is not passed to next firewall rule.</description></item>
        /// <item><description>drop - silently drop the packet</description></item>
        /// <item><description>jump - jump to the user defined chain specified by the value of jump-target parameter</description></item>
        /// <item><description>log - add a message to the system log containing following data: in-interface, out-interface, src-mac, protocol, src-ip:port-&gt;dst-ip:port and length of the packet.After packet is matched it is passed to next rule in the list, similar as passthrough</description></item>
        /// <item><description>passthrough - ignore this rule and go to next one (useful for statistics).</description></item>
        /// <item><description>return  - passes control back to the chain from where the jump took place</description></item>
        /// </list>
        /// </remarks>
        [TikProperty("action", DefaultValue = "accept", WinboxLabel = "Action")]
        public TikField<ActionType?> Action { get; set; }

        /// <summary>
        /// jump-target: If action=jump specified, then specifies the user-defined firewall chain to process the packet
        /// </summary>
        [TikProperty("jump-target", WinboxLabel = "Jump Target")]
        public TikField<string?> JumpTarget { get; set; }

        /// <summary>
        /// log-prefix: Defines the prefix to be printed before the logging information
        /// </summary>
        [TikProperty("log-prefix", WinboxLabel = "Log Prefix")]
        public TikField<string?> LogPrefix { get; set; }

        /// <summary>
        /// new-packet-mark
        /// </summary>
        [TikProperty("new-packet-mark")]
        public TikField<string?> NewPacketMark { get; set; }

        /// <summary>
        /// new-priorityne
        /// </summary>
        [TikProperty("new-priority", DefaultValue = "0", WinboxLabel = "New Priority")]
        public TikField<string?> NewPriority { get; set; }

        /// <summary>
        /// passthrough
        /// </summary>
        [TikProperty("passthrough", DefaultValue = "yes", WinboxLabel = "Passthrough")]
        public TikField<bool?> Passthrough { get; set; }
    }
}
