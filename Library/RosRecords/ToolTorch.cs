using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /tool/torch. Should be loaded via async API.
    /// </summary>
    [RosRecord("/tool/torch", IsReadOnly = true)]
    public class ToolTorch {
        /// <summary>
        /// src-address
        /// </summary>
        [RosProperty("src-address",IsReadOnly = true)]
        public string SrcAddress { get; private set; }

        /// <summary>
        /// dst-address
        /// </summary>
        [RosProperty("dst-address",IsReadOnly = true)]
        public string DstAddress { get; private set; }

        /// <summary>
        /// ip-protocol
        /// </summary>
        [RosProperty("ip-protocol",IsReadOnly = true)]
        public string IpProtocol { get; private set; }

        /// <summary>
        /// src-port
        /// </summary>
        [RosProperty("src-port",IsReadOnly = true)]
        public string SrcPort { get; private set; }

        /// <summary>
        /// dst-port
        /// </summary>
        [RosProperty("dst-port",IsReadOnly = true)]
        public string DstPort { get; private set; }

        /// <summary>
        /// tx
        /// </summary>
        [RosProperty("tx",IsReadOnly = true)]
        public long Tx { get; private set; }

        /// <summary>
        /// rx
        /// </summary>
        [RosProperty("rx",IsReadOnly = true)]
        public long Rx { get; private set; }

        /// <summary>
        /// tx-packets
        /// </summary>
        [RosProperty("tx-packets",IsReadOnly = true)]
        public long TxPackets { get; private set; }

        /// <summary>
        /// rx-packets
        /// </summary>
        [RosProperty("rx-packets",IsReadOnly = true)]
        public long RxPackets { get; private set; }

        private static string FormatAddress(string ip, string port) {
            return (ip + ":" + port).PadRight(21);
        }

        /// <summary>
        /// ToString override to make life more easy.
        /// </summary>
        public override string ToString() {
            return string.Format("{0}{1} -> {2} ({3}/{4})",
                (IpProtocol ?? "").PadRight(10),
                FormatAddress(SrcAddress, SrcPort),
                FormatAddress(DstAddress, DstPort),
                Tx, Rx);
        }
    }
}