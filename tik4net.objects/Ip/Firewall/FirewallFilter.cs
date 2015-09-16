using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/filter
    /// </summary>
    [TikEntity("/ip/firewall/filter", IncludeDetails = true, IsOrdered = true)]
    public class FirewallFilter
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// chain
        /// </summary>
        [TikProperty("chain")]
        public string Chain { get; set; }

        /// <summary>
        /// action
        /// </summary>
        [TikProperty("action")]
        public string Action { get; set; }

        /// <summary>
        /// protocol
        /// </summary>
        [TikProperty("protocol")]
        public string Protocol { get; set; }

        /// <summary>
        /// src-address
        /// </summary>
        [TikProperty("src-address")]
        public string SrcAddress { get; set; }

        /// <summary>
        /// address-list
        /// </summary>
        [TikProperty("address-list")]
        public string AddressList { get; set; }

        /// <summary>
        /// address-list-timeout  (00:00:00)
        /// </summary>
        [TikProperty("address-list-timeout")]
        public string AddressListTimeout { get; set; }

        /// <summary>
        /// dst-port
        /// </summary>
        [TikProperty("dst-port")]
        public long DstPort { get; set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [TikProperty("dst-address")]
        public string DstAddress { get; set; }

    }
}
