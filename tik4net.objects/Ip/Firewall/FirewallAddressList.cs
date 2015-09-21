using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/address-list
    /// </summary>
    [TikEntity("/ip/firewall/address-list", IncludeDetails = true)]
    public class FirewallAddressList
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// address
        /// </summary>
        [TikProperty("address", IsMandatory = true)]
        public string Address { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// timeout  (00:00:00)
        /// </summary>
        [TikProperty("timeout", DefaultValue = "00:00:00")]
        public string Timeout { get; set; }

        /// <summary>
        /// list
        /// </summary>
        [TikProperty("list", IsMandatory = true)]
        public string List { get; set; }
    }
}
