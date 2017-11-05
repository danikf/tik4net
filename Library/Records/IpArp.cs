using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/arp: Even though IP packets are addressed using  IP addresses, hardware addresses must be used to actually transport data from one host to another.Address Resolution Protocol is used to map OSI level 3 IP addresses to OSI level 2 MAC addreses. Router has a table of currently used ARP entries.Normally the table is built dynamically, but to increase network security, it can be partialy or completely built statically by means of adding static entries.
    /// </summary>
    [TikRecord("/ip/arp")]
    public class IpArp : IHasId {
        /// <summary>
        /// Unique identifier
        /// </summary>
        [TikProperty(".id", DataType.Id, IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// IP Address
        /// </summary>
        [TikProperty("address", DataType.String)]
        public string Address { get; set; }

        /// <summary>
        /// Interface name the IP address is assigned to
        /// </summary>
        [TikProperty("interface", DataType.String)]
        public string Interface { get; set; }

        /// <summary>
        /// MAC address to be mapped to
        /// </summary>
        [TikProperty("mac-address", DataType.MacAddress, DefaultValue = "00:00:00:00:00:00")]
        public string MacAddress { get; set; }


        /// <summary>
        /// Whether ARP entry is added by DHCP server
        /// </summary>
        [TikProperty("dhcp", DataType.Boolean, IsReadOnly = true)]
        public bool Dhcp { get; private set; }

        /// <summary>
        /// Whether entry is dynamically created
        /// </summary>
        [TikProperty("dynamic", DataType.Boolean, IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// Whether entry is not valid
        /// </summary>
        [TikProperty("invalid", DataType.Boolean, IsReadOnly = true)]
        public bool Invalid { get; private set; }
    }

}
