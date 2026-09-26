using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.DhcpServer
{
    /// <summary>
    /// ip/dhcp-server/network : 
    /// </summary>
    [TikEntity("/ip/dhcp-server/network", IncludeDetails = true)]
    public class DhcpServerNetwork
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// address: the network DHCP server(s) will lease addresses from
        /// </summary>
        [TikProperty("address", WinboxLabel = "Address")]
        public TikValue<string?> Address { get; set; }

        /// <summary>
        /// boot-file-name: Boot file name
        /// </summary>
        [TikProperty("boot-file-name", WinboxLabel = "Boot File Name")]
        public TikValue<string?> BootFileName { get; set; }

        /// <summary>
        /// caps-manager: Comma-separated list of IP addresses for one or more CAPsMan system managers.
        /// </summary>
        [TikProperty("caps-manager", WinboxLabel = "CAPS Managers")]
        public TikValue<string?> CapsManager { get; set; }

        /// <summary>
        /// dhcp-option: Add additional DHCP options from  option list.
        /// </summary>
        [TikProperty("dhcp-option", WinboxLabel = "DHCP Options")]
        public TikValue<string?> DhcpOption { get; set; }

        /// <summary>
        /// dns-server: the DHCP client will use these as the default DNS servers. Two comma-separated DNS servers can be specified to be used by the DHCP client as primary and secondary DNS servers
        /// </summary>
        [TikProperty("dns-server", WinboxLabel = "DNS Servers")]
        public TikValue<string?> DnsServer { get; set; }

        /// <summary>
        /// domain: The DHCP client will use this as the 'DNS domain' setting for the network adapter.
        /// </summary>
        [TikProperty("domain", WinboxLabel = "Domain")]
        public TikValue<string?> Domain { get; set; }

        /// <summary>
        /// gateway: The default gateway to be used by DHCP Client.
        /// </summary>
        [TikProperty("gateway", DefaultValue = "0.0.0.0", WinboxLabel = "Gateway")]
        public TikValue<string?> Gateway { get; set; }

        /// <summary>
        /// netmask: The actual network mask to be used by DHCP client. If set to '0' - netmask from network address will be used.
        /// </summary>
        [TikProperty("netmask", DefaultValue = "0", WinboxLabel = "Netmask")]
        public TikValue<string?> Netmask { get; set; }

        /// <summary>
        /// next-server: IP address of next server to use in bootstrap.
        /// </summary>
        [TikProperty("next-server", WinboxLabel = "Next Server")]
        public TikValue<string?> NextServer { get; set; }

        /// <summary>
        /// ntp-server: the DHCP client will use these as the default NTP servers. Two comma-separated NTP servers can be specified to be used by the DHCP client as primary and secondary NTP servers
        /// </summary>
        [TikProperty("ntp-server", WinboxLabel = "NTP Servers")]
        public TikValue<string?> NtpServer { get; set; }

        /// <summary>
        /// wins-server: The Windows DHCP client will use these as the default WINS servers. Two comma-separated WINS servers can be specified to be used by the DHCP client as primary and secondary WINS servers
        /// </summary>
        [TikProperty("wins-server", WinboxLabel = "WINS Servers")]
        public TikValue<string?> WinsServer { get; set; }

        /// <summary>
        /// comment: Short description of the client
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }
    }
}
