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
        /// Obsolete: use Accounting.AccountingSnapshot class.
        /// </summary>
        [Obsolete("use Accounting.AccountingSnapshot class.", true)]
        public abstract class AccountingSnapshot
        {
        }

        /// <summary>
        /// Obsolete: use Accounting.AccountingUncounted class.
        /// </summary>
        [Obsolete("use Accounting.AccountingUncounted class.", true)]

        public abstract class AccountingUncounted
        {

        }

        /// <summary>
        /// Obsolete: use Accounting.AccountingWebAccess class.
        /// </summary>
        [Obsolete("use Accounting.AccountingWebAccess class.", true)]
        public class AccountingWebAccess
        {

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
