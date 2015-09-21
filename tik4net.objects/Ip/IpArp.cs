using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
	/// <summary>
	/// ip/arp: Even though IP packets are addressed using  IP addresses, hardware addresses must be used to actually transport data from one host to another.Address Resolution Protocol is used to map OSI level 3 IP addresses to OSI level 2 MAC addreses. Router has a table of currently used ARP entries.Normally the table is built dynamically, but to increase network security, it can be partialy or completely built statically by means of adding static entries.
	/// </summary>
	[TikEntity("ip/arp")]
    public class IpArp
    {
		/// <summary>
		/// .id: primary key of row
		/// </summary>
		[TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

		/// <summary>
		/// address: IP address to be mapped
		/// </summary>
		[TikProperty("address")]
        public string Address { get; set; }

		/// <summary>
		/// interface: Interface name the IP address is assigned to
		/// </summary>
        [TikProperty("interface")]
        public string Interface { get; set; }

		/// <summary>
		/// mac-address: MAC address to be mapped to
		/// </summary>
		[TikProperty("mac-address", DefaultValue = "00:00:00:00:00:00")]
        public string MacAddress { get; set; }

		/// <summary>
		/// dhcp: Whether ARP entry is added by DHCP server
		/// </summary>
		[TikProperty("dhcp", IsReadOnly = true)]
        public bool Dhcp { get; private set; }

		/// <summary>
		/// dynamic: Whether entry is dynamically created
		/// </summary>
		[TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

		/// <summary>
		/// invalid: Whether entry is not valid
		/// </summary>
		[TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }
    }

}
