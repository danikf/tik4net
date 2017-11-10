using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /ip/firewall/address-list
    /// </summary>
    [RosRecord("/ip/firewall/address-list", IncludeDetails = true)]
    public class IpFirewallAddressList  : SetRecordBase {
        /// <summary>
        /// address
        /// </summary>
        [RosProperty("address",IsRequired = true)]
        public string Address { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// disabled
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [RosProperty("dynamic",IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// timeout  (00:00:00)
        /// </summary>
        [RosProperty("timeout", DefaultValue = "00:00:00")]
        public string Timeout { get; set; }

        /// <summary>
        /// list
        /// </summary>
        [RosProperty("list",IsRequired = true)]
        public string List { get; set; }
    }
}
