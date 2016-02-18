using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/accounting/snapshot: When a snapshot is made for data collection, the accounting table is cleared and new IP pairs and traffic data are added. The more frequently traffic data is collected, the less likelihood that the IP pairs thereshold limit will be reached.
    /// </summary>
    [TikEntity("/ip/accounting/snapshot", IsReadOnly = true)]
    public class AccountingSnapshot
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// bytes: total number of bytes, matched by this entry
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public int Bytes { get; private set; }

        /// <summary>
        /// dst-address: destination IP address
        /// </summary>
        [TikProperty("dst-address", IsReadOnly = true)]
        public string DstAddress { get; private set; }

        /// <summary>
        /// dst-user: recipient's name (if applicable)
        /// </summary>
        [TikProperty("dst-user", IsReadOnly = true)]
        public string DstUser { get; private set; }

        /// <summary>
        /// packets: total number of packets, matched by this entry
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public int Packets { get; private set; }

        /// <summary>
        /// src-address: source IP address
        /// </summary>
        [TikProperty("src-address", IsReadOnly = true)]
        public string SrcAddress { get; private set; }

        /// <summary>
        /// src-user: sender's name (if aplicable)
        /// </summary>
        [TikProperty("src-user", IsReadOnly = true)]
        public string SrcUser { get; private set; }

        /// <summary>
        /// Take new snapshot
        /// </summary>
        /// <param name="connection"></param>
        public static void Take(ITikConnection connection)
        {
            var cmd = connection.CreateCommand("/ip/accounting/snapshot/take");
            cmd.ExecuteNonQuery();
        }
    }
}
