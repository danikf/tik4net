using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// /tool/torch (R/O). Should be loaded via async API.
    /// </summary>
    [TikEntity("/tool/torch", IsReadOnly = true, IncludeProplist = false)]
    public class ToolTorch
    {
        /// <summary>
        /// src-address
        /// </summary>
        [TikProperty("src-address", IsReadOnly = true)]
        public string SrcAddress { get; private set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [TikProperty("dst-address", IsReadOnly = true)]
        public string DstAddress { get; private set; }

        /// <summary>
        /// ip-protocol
        /// </summary>
        [TikProperty("ip-protocol", IsReadOnly = true)]
        public string IpProtocol { get; private set; }

        /// <summary>
        /// src-port
        /// </summary>
        [TikProperty("src-port", IsReadOnly = true)]
        public string SrcPort { get; private set; }

        /// <summary>
        /// dst-port
        /// </summary>
        [TikProperty("dst-port", IsReadOnly = true)]
        public string DstPort { get; private set; }

        /// <summary>
        /// tx
        /// </summary>
        [TikProperty("tx", IsReadOnly = true)]
        public long Tx { get; private set; }

        /// <summary>
        /// rx
        /// </summary>
        [TikProperty("rx", IsReadOnly = true)]
        public long Rx { get; private set; }

        /// <summary>
        /// tx-packets
        /// </summary>
        [TikProperty("tx-packets", IsReadOnly = true)]
        public long TxPackets { get; private set; }

        /// <summary>
        /// rx-packets
        /// </summary>
        [TikProperty("rx-packets", IsReadOnly = true)]
        public long RxPackets { get; private set; }

        private static string FormatAddress(string ip, string port)
        {
            return (ip + ":" + port).PadRight(21);
        }

        /// <summary>
        /// ToString override to make life more easy.
        /// </summary>
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