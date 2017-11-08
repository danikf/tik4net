
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /ip/accounting/snapshot: When a snapshot is made for data collection, the accounting table is cleared and new IP pairs and traffic data are added. The more frequently traffic data is collected, the less likelihood that the IP pairs thereshold limit will be reached.
    /// </summary>
    [RosRecord("/ip/accounting/snapshot", IsReadOnly = true)]
    public class IpAccountingSnapshot : ISetRecord {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// bytes: total number of bytes, matched by this entry
        /// </summary>
        [RosProperty("bytes",IsReadOnly = true)]
        public int Bytes { get; private set; }

        /// <summary>
        /// dst-address: destination IP address
        /// </summary>
        [RosProperty("dst-address",IsReadOnly = true)]
        public string DstAddress { get; private set; }

        /// <summary>
        /// dst-user: recipient's name (if applicable)
        /// </summary>
        [RosProperty("dst-user",IsReadOnly = true)]
        public string DstUser { get; private set; }

        /// <summary>
        /// packets: total number of packets, matched by this entry
        /// </summary>
        [RosProperty("packets",IsReadOnly = true)]
        public int Packets { get; private set; }

        /// <summary>
        /// src-address: source IP address
        /// </summary>
        [RosProperty("src-address",IsReadOnly = true)]
        public string SrcAddress { get; private set; }

        /// <summary>
        /// src-user: sender's name (if aplicable)
        /// </summary>
        [RosProperty("src-user",IsReadOnly = true)]
        public string SrcUser { get; private set; }

        /* TODO
        public static void Take(ITikConnection connection) {
            var cmd = connection.CreateCommand("/ip/accounting/snapshot/take");
            cmd.ExecuteNonQuery();
        }*/
    }
}
