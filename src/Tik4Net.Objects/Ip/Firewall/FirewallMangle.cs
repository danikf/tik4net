using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/mangle
    /// </summary>
    [TikEntity("/ip/firewall/mangle", IncludeDetails = true, IsOrdered = true)]
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
            /// 
            /// </summary>
            [TikEnum("")]
            MarkRouting,
            //mark-routing - place a mark specified by the new-routing-mark parameter on a packet.This kind of marks is used for policy routing purposes only

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
        public string Id { get; private set; }

        /// <summary>
        /// chain
        /// </summary>
        [TikProperty("chain")]
        public string Chain { get; set; }

        /// <summary>
        /// action
        /// </summary>
        [TikProperty("action")]
        public ActionType Action { get; set; }

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

        /// <summary>
        /// src-address-list
        /// </summary>
        [TikProperty("src-address-list", UnsetOnDefault = true)]
        public string SrcAddressList { get; set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// new-packet-mark
        /// </summary>
        [TikProperty("new-packet-mark")]
        public string NewPacketMark { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// dst-address-list
        /// </summary>
        [TikProperty("dst-address-list", UnsetOnDefault = true)]
        public string DstAddressList { get; set; }

        /// <summary>
        /// protocol
        /// </summary>
        [TikProperty("protocol", UnsetOnDefault = true)]
        public string Protocol { get; set; }

        /// <summary>
        /// src-address
        /// </summary>
        [TikProperty("src-address", UnsetOnDefault = true)]
        public string SrcAddress { get; set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [TikProperty("dst-address", UnsetOnDefault = true)]
        public string DstAddress { get; set; }

        /// <summary>
        /// jump-target
        /// </summary>
        [TikProperty("jump-target")]
        public string JumpTarget { get; set; }
        
        /// <summary>
        /// address-list
        /// </summary>
        [TikProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// address-list-timeout
        /// </summary>
        [TikProperty("address-list-timeout", DefaultValue = "00:00:00")]
        public string AddressListTimeout { get; set; }

        /// <summary>
        /// ToString override.
        /// </summary>
        public override string ToString()
        {
            return base.ToString() + string.Format(" (Chain:{0}, Action:{1}, SrcAddress:{2}, DstAddress:{3}, Comment:{4})", Chain, Action, SrcAddress, DstAddress, Comment);
        }
    }

}
