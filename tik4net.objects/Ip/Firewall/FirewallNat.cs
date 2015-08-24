using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Ip.Firewall
{
    [TikEntity("/ip/firewall/nat", IncludeDetails = true)]
    public class FirewallNat
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("chain")]
        public string Chain { get; set; }

        [TikProperty("action")]
        public string Action { get; set; }

        [TikProperty("to-addresses")]
        public string ToAddresses { get; set; }

        [TikProperty("src-address")]
        public string SrcAddress { get; set; }

        [TikProperty("out-interface")]
        public string OutInterface { get; set; }

        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        [TikProperty("comment")]
        public string Comment { get; set; }

        [TikProperty("src-address-list")]
        public string SrcAddressList { get; set; }

        [TikProperty("dst-address")]
        public string DstAddress { get; set; }

        [TikProperty("in-interface")]
        public string InInterface { get; set; }

        [TikProperty("protocol")]
        public string Protocol { get; set; }

        [TikProperty("to-ports")]
        public long ToPorts { get; set; }

        [TikProperty("dst-port")]
        public long DstPort { get; set; }

    }
}
