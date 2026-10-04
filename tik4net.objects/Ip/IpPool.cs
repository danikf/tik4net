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
        public string? Id { get; private set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// Row ranges property.
        /// comma separated list of DNS server IP addresses
        /// </summary>
        [TikProperty("ranges", WinboxLabel = "Addresses")]
        public TikField<string?> Ranges { get; set; }

        /// <summary>
        /// Row name property.
        /// </summary>
        [TikProperty("next-pool", WinboxLabel = "Next Pool")]
        public TikField<string?> NextPool { get; set; }
    }
}
