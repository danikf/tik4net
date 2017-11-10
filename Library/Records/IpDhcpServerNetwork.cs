using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dhcp-server/Option : 
    /// </summary>
    [RosRecord("/ip/dhcp-server/network")]
    public class IpDhcpServerNetwork  : SetRecordBase {
        /// <summary>
        /// The Option DHCP server(s) will lease addresses from
        /// </summary>
        [RosProperty("address")]
        public string/*IP/netmask*/ Address { get; set; }

        /// <summary>
        /// Boot file name
        /// </summary>
        [RosProperty("boot-file-name")]
        public string BootFileName { get; set; }

        /// <summary>
        /// Comma-separated list of IP addresses for one or more CAPsMan system managers.
        /// </summary>
        [RosProperty("caps-manager")]
        public string CapsManager { get; set; }

        /// <summary>
        /// Add additional DHCP options from  option list.
        /// </summary>
        [RosProperty("dhcp-option")]
        public string DhcpOption { get; set; }

        /// <summary>
        /// The DHCP client will use these as the default DNS servers. Two comma-separated DNS servers can be specified to be used by the DHCP client as primary and secondary DNS servers
        /// </summary>
        [RosProperty("dns-server")]
        public string DnsServer { get; set; }

        /// <summary>
        /// The DHCP client will use this as the 'DNS domain' setting for the Option adapter.
        /// </summary>
        [RosProperty("domain")]
        public string Domain { get; set; }

        /// <summary>
        /// The default gateway to be used by DHCP Client.
        /// </summary>
        [RosProperty("gateway", DefaultValue = "0.0.0.0")]
        public string/*IP*/ Gateway { get; set; }

        /// <summary>
        /// The actual Option mask to be used by DHCP client. If set to '0' - netmask from Option address will be used.
        /// </summary>
        [RosProperty("netmask", DefaultValue = "0")]
        public int? /*integer: 0..32*/ Netmask { get; set; }

        /// <summary>
        /// IP address of next server to use in bootstrap.
        /// </summary>
        [RosProperty("next-server")]
        public string/*IP*/ NextServer { get; set; }

        /// <summary>
        /// The DHCP client will use these as the default NTP servers. Two comma-separated NTP servers can be specified to be used by the DHCP client as primary and secondary NTP servers
        /// </summary>
        [RosProperty("ntp-server")]
        public string/*IP*/ NtpServer { get; set; }

        /// <summary>
        /// The Windows DHCP client will use these as the default WINS servers. Two comma-separated WINS servers can be specified to be used by the DHCP client as primary and secondary WINS servers
        /// </summary>
        [RosProperty("wins-server")]
        public string/*IP*/ WinsServer { get; set; }

        /// <summary>
        /// Short description of the client
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }
    }
}
