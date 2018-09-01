using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ppp
{
    /// <summary>
    /// ppp/aaa: Settings in this submenu allows to set RADIUS accounting and authentication. Note that RADIUS user database is consulted only if the required username is not found in local user database. 
    /// https://wiki.mikrotik.com/wiki/Manual:PPP_AAA
    /// </summary>
    [TikEntity("ppp/aaa", IsSingleton = true)]
    public class PppAaa
    {
        /// <summary>
        /// accounting: Enable RADIUS accounting
        /// </summary>
        [TikProperty("accounting", DefaultValue = "yes")]
        public bool Accounting { get; set; }

        /// <summary>
        /// interim-update: Interim-Update time interval
        /// </summary>
        [TikProperty("interim-update", DefaultValue = "0s")]
        public string/*time*/ InterimUpdate { get; set; }

        /// <summary>
        /// use-radius: Enable user authentication via RADIUS. If entry in local secret database is not found, then client will be authenticated via RADIUS.
        /// </summary>
        [TikProperty("use-radius", DefaultValue = "no")]
        public bool UseRadius { get; set; }
    }
}
