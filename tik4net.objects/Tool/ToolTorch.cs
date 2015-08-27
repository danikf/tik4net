using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Tool
{
    [TikEntity("/tool/torch", IsReadOnly = true, IncludeProplist = false)]
    public class ToolTorch
    {
        [TikProperty("src-address", IsReadOnly = true)]
        public string SrcAddress { get; private set; }

        [TikProperty("dst-address", IsReadOnly = true)]
        public string DstAddress { get; private set; }

        [TikProperty("ip-protocol", IsReadOnly = true)]
        public string IpProtocol { get; private set; }

        [TikProperty("src-port", IsReadOnly = true)]
        public string SrcPort { get; private set; }

        [TikProperty("dst-port", IsReadOnly = true)]
        public string DstPort { get; private set; }

        [TikProperty("tx", IsReadOnly = true)]
        public long Tx { get; private set; }

        [TikProperty("rx", IsReadOnly = true)]
        public long Rx { get; private set; }

        [TikProperty("tx-packets", IsReadOnly = true)]
        public long TxPackets { get; private set; }

        [TikProperty("rx-packets", IsReadOnly = true)]
        public long RxPackets { get; private set; }

        private static string FormatAddress(string ip, string port)
        {
            return (ip + ":" + port).PadRight(21);
        }

        public override string ToString()
        {
            return string.Format("{0}{1} -> {2} ({3}/{4})",
                (IpProtocol ?? "").PadRight(10),
                FormatAddress(SrcAddress, SrcPort),
                FormatAddress(DstAddress, DstPort),
                Tx, Rx);
        }
    }
}
