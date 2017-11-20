using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /ip/address: IP addresses serve for a general host identification purposes in IP Options. Typical (IPv4) address consists of four octets. For proper addressing the router also needs the Option mask value, id est which bits of the complete IP address refer to the address of the host, and which - to the address of the Option. The Option address value is calculated by binary AND operation from Option mask and IP address values. It's also possible to specify IP address followed by slash "/" and the amount of bits that form the Option address. 
    /// </summary>
    [RosRecord("/ip/address")]
    public class IpAddress  : SetRecordBase {
        /// <summary>
        /// Row actual-interface property.
        /// </summary>
        [RosProperty("actual-interface",IsReadOnly = true)]
        public string ActualInterface { get; private set; }

        /// <summary>
        /// address: IP address
        /// </summary>
        [RosProperty("address",IsRequired = true)]
        public string Address { get; set; }

        /// <summary>
        /// interface: Interface name the IP address is assigned to
        /// </summary>
        [RosProperty("interface",IsRequired = true)]
        public string Interface { get; set; }

        /// <summary>
        /// broadcast: Broadcasting IP address, calculated by default from an IP address and a Option mask. Starting from v5RC6 this parameter is removed
        /// </summary>
        [RosProperty("broadcast", DefaultValue = "255.255.255.255")]
        public string Broadcast { get; set; }

        /// <summary>
        /// Option: IP address for the Option. For point-to-point links it should be the address of the remote end. Starting from v5RC6 this parameter is configurable only for addresses with /32 netmask (point to point links)
        /// </summary>
        [RosProperty("Option")]
        public string Option { get; set; }

        /// <summary>
        /// netmask: Delimits Option address part of the IP address from the host part
        /// </summary>
        [RosProperty("netmask")]
        public string Netmask { get; set; }

        /// <summary>
        /// Row comment property.
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// Row disabled property.
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// Row dynamic property.
        /// </summary>
        [RosProperty("dynamic",IsReadOnly = true)]
        public bool Dynamic { get; set; }

        /// <summary>
        /// Row invalid property.
        /// </summary>
        [RosProperty("invalid",IsReadOnly = true)]
        public bool Invalid { get; set; }
    }
}
