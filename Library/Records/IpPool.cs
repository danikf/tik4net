using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /ip/pool: IP pools containing address pools for DHCP and PPP
    /// </summary>
    [TikRecord("/ip/pool", IncludeDetails = true)]
    public class IpPool  : IHasId {
        /// <summary>
        /// Row .id property.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [TikProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// Row ranges property.
        /// comma seperated list of DNS server IP addresses
        /// </summary>
        [TikProperty("ranges", IsRequired = true)]
        public string/*IPv4/IPv6 range list*/ Ranges { get; set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [TikProperty("next-pool")]
        public string NextPool { get; set; }
    }
}
