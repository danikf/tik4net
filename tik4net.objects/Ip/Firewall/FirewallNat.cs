using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/nat
    /// </summary>
    [TikEntity("/ip/firewall/nat", IncludeDetails = true, IsOrdered = true)]
    public class FirewallNat
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
        /// to-addresses
        /// </summary>
        [TikProperty("to-addresses")]
        public string ToAddresses { get; set; }

        /// <summary>
        /// src-address
        /// </summary>
        [TikProperty("src-address")]
        public string SrcAddress { get; set; }

        /// <summary>
        /// out-interface
        /// </summary>
        [TikProperty("out-interface")]
        public string OutInterface { get; set; }

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
        /// src-address-list
        /// </summary>
        [TikProperty("src-address-list")]
        public string SrcAddressList { get; set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [TikProperty("dst-address")]
        public string DstAddress { get; set; }

        /// <summary>
        /// in-interface
        /// </summary>
        [TikProperty("in-interface")]
        public string InInterface { get; set; }

        /// <summary>
        /// protocol
        /// </summary>
        [TikProperty("protocol")]
        public string Protocol { get; set; }

        /// <summary>
        /// to-ports
        /// </summary>
        [TikProperty("to-ports")]
        public long ToPorts { get; set; }

        /// <summary>
        /// dst-port
        /// </summary>
        [TikProperty("dst-port")]
        public long DstPort { get; set; }

    }
}
