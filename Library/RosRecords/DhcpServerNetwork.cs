using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// ip/dhcp-server/network : 
    /// </summary>
    [RosRecord("/ip/dhcp-server/network")]
    public class DhcpServerNetwork  : IHasId {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// address: the network DHCP server(s) will lease addresses from
        /// </summary>
        [RosProperty("address")]
        public string/*IP/netmask*/ Address { get; set; }

        /// <summary>
        /// boot-file-name: Boot file name
        /// </summary>
        [RosProperty("boot-file-name")]
        public string BootFileName { get; set; }

        /// <summary>
        /// caps-manager: Comma-separated list of IP addresses for one or more CAPsMan system managers.
        /// </summary>
        [RosProperty("caps-manager")]
        public string CapsManager { get; set; }

        /// <summary>
        /// dhcp-option: Add additional DHCP options from  option list.
        /// </summary>
        [RosProperty("dhcp-option")]
        public string DhcpOption { get; set; }

        /// <summary>
        /// dns-server: the DHCP client will use these as the default DNS servers. Two comma-separated DNS servers can be specified to be used by the DHCP client as primary and secondary DNS servers
        /// </summary>
        [RosProperty("dns-server")]
        public string DnsServer { get; set; }

        /// <summary>
        /// domain: The DHCP client will use this as the 'DNS domain' setting for the network adapter.
        /// </summary>
        [RosProperty("domain")]
        public string Domain { get; set; }

        /// <summary>
        /// gateway: The default gateway to be used by DHCP Client.
        /// </summary>
        [RosProperty("gateway", DefaultValue = "0.0.0.0")]
        public string/*IP*/ Gateway { get; set; }

        /// <summary>
        /// netmask: The actual network mask to be used by DHCP client. If set to '0' - netmask from network address will be used.
        /// </summary>
        [RosProperty("netmask", DefaultValue = "0")]
        public string/*integer: 0..32*/ Netmask { get; set; }

        /// <summary>
        /// next-server: IP address of next server to use in bootstrap.
        /// </summary>
        [RosProperty("next-server")]
        public string/*IP*/ NextServer { get; set; }

        /// <summary>
        /// ntp-server: the DHCP client will use these as the default NTP servers. Two comma-separated NTP servers can be specified to be used by the DHCP client as primary and secondary NTP servers
        /// </summary>
        [RosProperty("ntp-server")]
        public string/*IP*/ NtpServer { get; set; }

        /// <summary>
        /// wins-server: The Windows DHCP client will use these as the default WINS servers. Two comma-separated WINS servers can be specified to be used by the DHCP client as primary and secondary WINS servers
        /// </summary>
        [RosProperty("wins-server")]
        public string/*IP*/ WinsServer { get; set; }

        /// <summary>
        /// comment: Short description of the client
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }
    }
}
