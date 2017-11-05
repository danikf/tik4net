using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /ip/firewall/nat
    /// </summary>
    [RosRecord("/ip/firewall/nat", IncludeDetails = true, IsOrdered = true)]
    public class FirewallNat  : IHasId {
        /// <summary>
        /// .id
        /// </summary>
        [RosProperty(".id", IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// chain
        /// </summary>
        [RosProperty("chain")]
        public string Chain { get; set; }

        /// <summary>
        /// action
        /// </summary>
        [RosProperty("action")]
        public string Action { get; set; }

        /// <summary>
        /// to-addresses
        /// </summary>
        [RosProperty("to-addresses")]
        public string ToAddresses { get; set; }

        /// <summary>
        /// src-address
        /// </summary>
        [RosProperty("src-address")]
        public string SrcAddress { get; set; }

        /// <summary>
        /// out-interface
        /// </summary>
        [RosProperty("out-interface")]
        public string OutInterface { get; set; }

        /// <summary>
        /// invalid
        /// </summary>
        [RosProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [RosProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// src-address-list
        /// </summary>
        [RosProperty("src-address-list")]
        public string SrcAddressList { get; set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [RosProperty("dst-address")]
        public string DstAddress { get; set; }

        /// <summary>
        /// in-interface
        /// </summary>
        [RosProperty("in-interface")]
        public string InInterface { get; set; }

        /// <summary>
        /// protocol
        /// </summary>
        [RosProperty("protocol")]
        public string Protocol { get; set; }

        /// <summary>
        /// to-ports
        /// </summary>
        [RosProperty("to-ports")]
        public long ToPorts { get; set; }

        /// <summary>
        /// dst-port
        /// </summary>
        [RosProperty("dst-port")]
        public long DstPort { get; set; }

    }
}
