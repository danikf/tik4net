using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /ip/pool: IP pools containing address pools for DHCP and PPP
    /// </summary>
    [RosRecord("/ip/pool")]
    public class IpPool  : SetRecordBase {
        /// <summary>
        /// Row name property.
        /// </summary>
        [RosProperty("name",IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// Row ranges property.
        /// comma seperated list of DNS server IP addresses
        /// </summary>
        [RosProperty("ranges",IsRequired = true)]
        public string/*IPv4/IPv6 range list*/ Ranges { get; set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [RosProperty("next-pool")]
        public string NextPool { get; set; }
    }
}
