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
        public string? Id { get; private set; }

        /// <summary>
        /// address
        /// </summary>
        [TikProperty("address")]
        public TikValue<string?> Address { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikValue<bool?> Dynamic { get; private set; }

        /// <summary>
        /// timeout  (00:00:00)
        /// </summary>
        [TikProperty("timeout", DefaultValue = "00:00:00")]
        public TikValue<TikDuration?> Timeout { get; set; }

        /// <summary>
        /// list
        /// </summary>
        [TikProperty("list")]
        public TikValue<string?> List { get; set; }
    }
}
