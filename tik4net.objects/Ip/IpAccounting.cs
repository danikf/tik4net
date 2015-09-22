using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// ip/accounting: Authentication, Authorization and Accounting feature provides a possibility of local and/or remote (on RADIUS server) Point-to-Point and HotSpot user management and traffic accounting (all IP traffic passing the router is accounted; local traffic acocunting is an option).
    /// </summary>
	[TikEntity("ip/accounting", IsSingleton = true)]
    public class IpAccounting
    {
        #region Submenu classes
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

        /// <summary>
        /// /ip/accounting/uncounted: In case no more IP pairs can be added to the accounting table (the accounting threshold has been reached), all traffic that does not belong to any of the known IP pairs is summed together and totals are shown in this menu
        /// </summary>
        [TikEntity("/ip/accounting/uncounted", IsReadOnly = true, IsSingleton = true)]
        public class AccountingUncounted
        {
            /// <summary>
            /// bytes: byte count
            /// </summary>
            [TikProperty("bytes", IsReadOnly = true)]
            public int Bytes { get; private set; }

            /// <summary>
            /// packets: packet count
            /// </summary>
            [TikProperty("packets", IsReadOnly = true)]
            public int Packets { get; private set; }
        }

        /// <summary>
        /// ip/accounting/web-access: The web page report make it possible to use the standard Unix/Linux tool wget to collect the traffic data and save it to a file or to use MikroTik shareware Traffic Counter to display the table. If the web report is enabled and the web page is viewed, the snapshot will be made when connection is initiated to the web page. The snapshot will be displayed on the web page. TCP protocol, used by http connections with the wget tool guarantees that none of the traffic data will be lost. The snapshot image will be made when the connection from wget is initiated. Web browsers or wget should connect to URL: http://routerIP/accounting/ip.cgi
        /// </summary>
        [TikEntity("ip/accounting/web-access", IsSingleton = true)]
        public class AccountingWebAccess
        {
            /// <summary>
            /// accessible-via-web: whether the snapshot is available via web
            /// </summary>
            [TikProperty("accessible-via-web", DefaultValue = "no")]
            public string AccessibleViaWeb { get; set; }

            /// <summary>
            /// address: IP address range that is allowed to access the snapshot
            /// </summary>
            [TikProperty("address", DefaultValue = "0.0.0.0/0")]
            public string Address { get; set; }
        }
        #endregion

        private const string DEFAULT_TRESHOLD = "256";

        /// <summary>
        /// account-local-traffic: whether to account the traffic to/from the router itself
        /// </summary>
        [TikProperty("account-local-traffic", DefaultValue = "no")]
        public string AccountLocalTraffic { get; set; }

        /// <summary>
        /// enabled: whether local IP traffic accounting is enabled
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public string Enabled { get; set; }

        /// <summary>
        /// threshold: maximum number of IP pairs in the accounting table (maximal value is 8192)
        /// </summary>
        [TikProperty("threshold", DefaultValue = DEFAULT_TRESHOLD)]
        public int Threshold { get; set; }

        /// <summary>
        /// .ctor
        /// </summary>
        public IpAccounting()
        {
            Threshold = int.Parse(DEFAULT_TRESHOLD);
        }
    }
}
