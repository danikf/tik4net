
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /ip/firewall/mangle
    /// </summary>
    [RosRecord("/ip/firewall/mangle", IncludeDetails = true, IsOrdered = true)]
    public class IpFirewallMangle : ISetRecord {
        /// <summary>
        /// Mangle action type - <see cref="IpFirewallMangle.Action"/>
        /// </summary>
        public enum ActionType {
            /// <summary>
            /// accept - accept the packet. Packet is not passed to next firewall rule.
            /// </summary>
            [RosEnum("accept")]
            Accept,

            /// <summary>
            /// add-dst-to-address-list - add destination address to Address list specified by address-list parameter
            /// </summary>
            [RosEnum("add-dst-to-address-list")]
            AddDstToAddressList,

            /// <summary>
            /// add-src-to-address-list - add source address to Address list specified by address-list parameter
            /// </summary>
            [RosEnum("add-src-to-address-list")]
            AddSrcToAddressList,

            /// <summary>
            /// change-dscp - change Differentiated Services Code Point (DSCP) field value specified by the new-dscp parameter
            /// </summary>
            [RosEnum("change-dscp")]
            ChangeDscp,

            /// <summary>
            /// change-mss - change Maximum Segment Size field value of the packet to a value specified by the new-mss parameter
            /// </summary>
            [RosEnum("change-mss")]
            ChangeMms,

            /// <summary>
            /// change-ttl - change Time to Live field value of the packet to a value specified by the new-ttl parameter
            /// </summary>
            [RosEnum("change-ttl")]
            ChangeTtl,

            /// <summary>
            /// clear-df - clear 'Do Not Fragment' Flag
            /// </summary>
            [RosEnum("clear-df")]
            ClearDf,

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
            /// mark-connection - place a mark specified by the new-connection-mark parameter on the entire connection that matches the rule
            /// </summary>
            [RosEnum("mark-connection")]
            MarkConnection,

            /// <summary>
            /// place a mark specified by the new-packet-mark parameter on a packet that matches the rule
            /// </summary>
            [RosEnum("mark-packet")]
            MarkPacket,

            /// <summary>
            /// 
            /// </summary>
            [RosEnum("")]
            MarkRouting,
            //mark-routing - place a mark specified by the new-routing-mark parameter on a packet.This kind of marks is used for policy routing purposes only

            /// <summary>
            /// ignore this rule and go to next one (useful for statistics).
            /// </summary>
            [RosEnum("passthrough")]
            Passthrough,

            /// <summary>
            /// return - pass control back to the chain from where the jump took place
            /// </summary>
            [RosEnum("return")]
            Return,

            /// <summary>
            /// set-priority - set priority specified by the new- priority parameter on the packets sent out through a link that is capable of transporting priority(VLAN or WMM - enabled wireless interface). Read more>
            /// </summary>
            [RosEnum("set-priority")]
            SetPriority,

            /// <summary>
            /// sniff-pc
            /// </summary>
            [RosEnum("sniff-pc")]
            SniffPc,

            /// <summary>
            /// sniff-tzsp - send packet to a remote TZSP compatible system(such as Wireshark). Set remote target with sniff-target and sniff-target-port parameters(Wireshark recommends port 37008)
            /// </summary>
            [RosEnum("sniff-tzsp")]
            SniffTzsp,

            /// <summary>
            /// strip-ipv4-options - strip IPv4 option fields from IP header.
            /// </summary>
            [RosEnum("strip-ipv4-options")]
            StripIpv4Options,
        }

        /// <summary>
        /// .id
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// chain
        /// </summary>
        [RosProperty("chain")]
        public string Chain { get; set; }

        /// <summary>
        /// action
        /// </summary>
        [RosProperty("action")]
        public ActionType Action { get; set; }

        /// <summary>
        /// new-priorityne
        /// </summary>
        [RosProperty("new-priority")]
        public string NewPriority { get; set; } = "0";

        /// <summary>
        /// passthrough
        /// </summary>
        [RosProperty("passthrough")]
        public bool Passthrough { get; set; } = true;

        /// <summary>
        /// src-address-list
        /// </summary>
        [RosProperty("src-address-list", UnsetOnDefault = true)]
        public string SrcAddressList { get; set; }

        /// <summary>
        /// invalid
        /// </summary>
        [RosProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [RosProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// new-packet-mark
        /// </summary>
        [RosProperty("new-packet-mark")]
        public string NewPacketMark { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// dst-address-list
        /// </summary>
        [RosProperty("dst-address-list", UnsetOnDefault = true)]
        public string DstAddressList { get; set; }

        /// <summary>
        /// protocol
        /// </summary>
        [RosProperty("protocol", UnsetOnDefault = true)]
        public string Protocol { get; set; }

        /// <summary>
        /// src-address
        /// </summary>
        [RosProperty("src-address", UnsetOnDefault = true)]
        public string SrcAddress { get; set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [RosProperty("dst-address", UnsetOnDefault = true)]
        public string DstAddress { get; set; }

        /// <summary>
        /// jump-target
        /// </summary>
        [RosProperty("jump-target")]
        public string JumpTarget { get; set; }

        /// <summary>
        /// address-list
        /// </summary>
        [RosProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// address-list-timeout
        /// </summary>
        [RosProperty("address-list-timeout")]
        public string AddressListTimeout { get; set; } = "00:00:00";

        /// <summary>
        /// ToString override.
        /// </summary>
        public override string ToString() {
            return base.ToString() + string.Format(" (Chain:{0}, Action:{1}, SrcAddress:{2}, DstAddress:{3}, Comment:{4})", Chain, Action, SrcAddress, DstAddress, Comment);
        }
    }

}
