using System;
using System.Collections.Generic;
using System.Globalization;
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
        [TikProperty("chain", WinboxLabel = "Chain")]
        public TikField<string?> Chain { get; set; }

        /// <summary>
        /// action: determines how packets are processed (src-nat, dst-nat, masquerade, redirect, etc.).
        /// </summary>
        [TikProperty("action", WinboxLabel = "Action")]
        public TikField<string?> Action { get; set; }

        /// <summary>
        /// to-addresses: replacement IP address or address range for source/destination NAT operations.
        /// </summary>
        [TikProperty("to-addresses", WinboxLabel = "To Addresses")]
        public TikField<string?> ToAddresses { get; set; }

        /// <summary>
        /// src-address: identifies packets originating from specific internal IP addresses.
        /// </summary>
        [TikProperty("src-address", WinboxLabel = "Src. Address", Negatable = true)]
        public TikField<string?> SrcAddress { get; set; }

        /// <summary>
        /// out-interface: outgoing network interface for packet transmission.
        /// </summary>
        [TikProperty("out-interface", WinboxLabel = "Out. Interface", Negatable = true)]
        public TikField<string?> OutInterface { get; set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikField<bool?> Dynamic { get; private set; }

        /// <summary>
        /// disabled: temporarily deactivate the rule without deletion.
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// comment: documentation field for rule descriptions and organization.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// src-address-list: identifies packets from predefined address lists.
        /// </summary>
        [TikProperty("src-address-list", WinboxLabel = "Src. Address List", Negatable = true)]
        public TikField<string?> SrcAddressList { get; set; }

        /// <summary>
        /// dst-address: targets packets destined for particular IP addresses.
        /// </summary>
        [TikProperty("dst-address", WinboxLabel = "Dst. Address", Negatable = true)]
        public TikField<string?> DstAddress { get; set; }

        /// <summary>
        /// in-interface: incoming network interface packets traverse.
        /// </summary>
        [TikProperty("in-interface", WinboxLabel = "In. Interface", Negatable = true)]
        public TikField<string?> InInterface { get; set; }

        /// <summary>
        /// protocol: specifies the protocol (TCP, UDP, etc.) the rule applies to.
        /// </summary>
        [TikProperty("protocol", WinboxLabel = "Protocol", Negatable = true)]
        public TikField<string?> Protocol { get; set; }

        /// <summary>
        /// to-ports: replacement port or port range (0-65535) for modified packets.
        /// </summary>
        [TikProperty("to-ports", WinboxLabel = "To Ports")]
        public TikField<long?> ToPorts { get; set; }

        /// <summary>
        /// dst-port (integer [ -integer]: 0..65535; Default: )
        /// </summary>
        /// <seealso cref="DstPortStr"/>
        public long DstPort
        {
            get { string? text = DstPortStr.ValueOrDefault(null); return string.IsNullOrWhiteSpace(text) ? 0 : long.Parse(text, CultureInfo.InvariantCulture); }
            set { DstPortStr = value.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// dst-port (integer [ -integer]: 0..65535; Default: ) | List of destination port numbers or port number ranges
        /// </summary>
        /// <seealso cref="DstPort"/>
        [TikProperty("dst-port", WinboxLabel = "Dst. Port", Negatable = true)]
        public TikField<string?> DstPortStr { get; set; }

        /// <summary>
        /// src-port (integer [ -integer]: 0..65535; Default: )
        /// </summary>
        /// <seealso cref="SrcPortStr"/>
        public long SrcPort
        {
            get { string? text = SrcPortStr.ValueOrDefault(null); return string.IsNullOrWhiteSpace(text) ? 0 : long.Parse(text, CultureInfo.InvariantCulture); }
            set { SrcPortStr = value.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// src-port (integer [ -integer]: 0..65535; Default: ) | List of destination port numbers or port number ranges
        /// </summary>
        /// <seealso cref="SrcPort"/>
        [TikProperty("src-port", WinboxLabel = "Src. Port", Negatable = true)]
        public TikField<string?> SrcPortStr { get; set; }
    }
}
