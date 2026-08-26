using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/nat
    /// </summary>
    [TikEntity("/ip/firewall/nat", IncludeDetails = true, IsOrdered = true, IncludeCliStats = true)]
    public class FirewallNat
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// chain: firewall chain where the NAT rule applies (srcnat, dstnat, input, output, custom).
        /// </summary>
        [TikProperty("chain")]
        public string? Chain { get; set; }

        /// <summary>
        /// action: determines how packets are processed (src-nat, dst-nat, masquerade, redirect, etc.).
        /// </summary>
        [TikProperty("action")]
        public string? Action { get; set; }

        /// <summary>
        /// to-addresses: replacement IP address or address range for source/destination NAT operations.
        /// </summary>
        [TikProperty("to-addresses")]
        public string? ToAddresses { get; set; }

        /// <summary>
        /// src-address: identifies packets originating from specific internal IP addresses.
        /// </summary>
        [TikProperty("src-address")]
        public string? SrcAddress { get; set; }

        /// <summary>
        /// out-interface: outgoing network interface for packet transmission.
        /// </summary>
        [TikProperty("out-interface")]
        public string? OutInterface { get; set; }

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
        /// disabled: temporarily deactivate the rule without deletion.
        /// </summary>
        [TikProperty("disabled")]
        public bool? Disabled { get; set; }

        /// <summary>
        /// comment: documentation field for rule descriptions and organization.
        /// </summary>
        [TikProperty("comment")]
        public string? Comment { get; set; }

        /// <summary>
        /// src-address-list: identifies packets from predefined address lists.
        /// </summary>
        [TikProperty("src-address-list")]
        public string? SrcAddressList { get; set; }

        /// <summary>
        /// dst-address: targets packets destined for particular IP addresses.
        /// </summary>
        [TikProperty("dst-address")]
        public string? DstAddress { get; set; }

        /// <summary>
        /// in-interface: incoming network interface packets traverse.
        /// </summary>
        [TikProperty("in-interface")]
        public string? InInterface { get; set; }

        /// <summary>
        /// protocol: specifies the protocol (TCP, UDP, etc.) the rule applies to.
        /// </summary>
        [TikProperty("protocol")]
        public string? Protocol { get; set; }

        /// <summary>
        /// to-ports: replacement port or port range (0-65535) for modified packets.
        /// </summary>
        [TikProperty("to-ports")]
        public long ToPorts { get; set; }

        /// <summary>
        /// dst-port (integer [ -integer]: 0..65535; Default: )
        /// </summary>
        /// <seealso cref="DstPortStr"/>
        public long DstPort
        {
            get { return string.IsNullOrWhiteSpace(DstPortStr) ? 0 : long.Parse(DstPortStr); }
            set { DstPortStr = value.ToString(); }
        }

        /// <summary>
        /// dst-port (integer [ -integer]: 0..65535; Default: ) | List of destination port numbers or port number ranges
        /// </summary>
        /// <seealso cref="DstPort"/>
        [TikProperty("dst-port")]
        public string? DstPortStr { get; set; }

        /// <summary>
        /// src-port (integer [ -integer]: 0..65535; Default: )
        /// </summary>
        /// <seealso cref="SrcPortStr"/>
        public long SrcPort
        {
            get { return string.IsNullOrWhiteSpace(SrcPortStr) ? 0 : long.Parse(SrcPortStr); }
            set { SrcPortStr = value.ToString(); }
        }

        /// <summary>
        /// src-port (integer [ -integer]: 0..65535; Default: ) | List of destination port numbers or port number ranges
        /// </summary>
        /// <seealso cref="SrcPort"/>
        [TikProperty("src-port")]
        public string? SrcPortStr { get; set; }
    }
}
