using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/pool: IP pools containing address pools for DHCP and PPP
    /// </summary>
    [TikEntity("/ip/pool", IncludeDetails = true)]
    public class IpPool
    {
        /// <summary>
        /// Row .id property.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// Row ranges property.
        /// comma seperated list of DNS server IP addresses
        /// </summary>
        [TikProperty("ranges", IsMandatory = true)]
        public string/*IPv4/IPv6 range list*/ Ranges { get; set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [TikProperty("next-pool")]
        public string NextPool { get; set; }
    }
}
