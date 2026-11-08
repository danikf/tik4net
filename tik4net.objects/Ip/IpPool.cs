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
        /// .id: the router's identifier of the row.
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name: the pool's name, by which DHCP servers, PPP profiles and other pools refer to it.
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// ranges: the addresses the pool hands out, each a single address or a range (<c>10.0.0.10-10.0.0.100</c>).
        /// </summary>
        [TikProperty("ranges", WinboxLabel = "Addresses")]
        public TikField<TikValueList<string>?> Ranges { get; set; }

        /// <summary>
        /// next-pool: the name of the pool an address is taken from once this one is exhausted.
        /// </summary>
        [TikProperty("next-pool", WinboxLabel = "Next Pool")]
        public TikField<string?> NextPool { get; set; }
    }
}
