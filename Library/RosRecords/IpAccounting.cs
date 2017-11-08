namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// ip/accounting: Authentication, Authorization and Accounting feature provides a possibility of local and/or remote (on RADIUS server) Point-to-Point and HotSpot user management and traffic accounting (all IP traffic passing the router is accounted; local traffic acocunting is an option).
    /// </summary>
	[RosRecord("/ip/accounting", IsSingleton = true)]
    public class IpAccounting {
        /// <summary>
        /// account-local-traffic: whether to account the traffic to/from the router itself
        /// </summary>
        [RosProperty("account-local-traffic")]
        public bool AccountLocalTraffic { get; set; }

        /// <summary>
        /// enabled: whether local IP traffic accounting is enabled
        /// </summary>
        [RosProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// threshold: maximum number of IP pairs in the accounting table (maximal value is 8192)
        /// </summary>
        [RosProperty("threshold")]
        public int Threshold { get; set; } = 256;
    }
}
