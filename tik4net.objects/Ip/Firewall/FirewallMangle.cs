using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Ip.Firewall
{
    [TikEntity("/ip/firewall/mangle", IncludeDetails = true)]
    public class FirewallMangle
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("chain")]
        public string Chain { get; set; }

        [TikProperty("action")]
        public string Action { get; set; }

        [TikProperty("new-priority")]
        public long NewPriority { get; set; }

        [TikProperty("passthrough")]
        public bool Passthrough { get; set; }

        [TikProperty("src-address-list")]
        public string SrcAddressList { get; set; }

        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        [TikProperty("new-packet-mark")]
        public string NewPacketMark { get; set; }

        [TikProperty("comment")]
        public string Comment { get; set; }

        [TikProperty("dst-address-list")]
        public string DstAddressList { get; set; }

        [TikProperty("protocol")]
        public string Protocol { get; set; }

        [TikProperty("src-address")]
        public string SrcAddress { get; set; }

        [TikProperty("dst-address")]
        public string DstAddress { get; set; }

        [TikProperty("jump-target")]
        public string JumpTarget { get; set; }

        [TikProperty("address-list")]
        public string AddressList { get; set; }

        [TikProperty("address-list-timeout")]
        public string AddressListTimeout { get; set; }
    }

}
