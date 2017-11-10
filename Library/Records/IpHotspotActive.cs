namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/hotspot/user
    /// 
    /// This is the menu, where client's user/password information is actually added, additional configuration options for HotSpot users are configured here as well.
    /// </summary>
    [RosRecord("/ip/hotspot/active", IsReadOnly = true)]
    public class IpHotspotActive  : SetRecordBase {
        /// <summary>
        /// Server
        /// </summary>
        [RosProperty("server",IsReadOnly = true)]
        public string Server { get; set; }

        /// <summary>
        /// address: IP address
        /// </summary>
        [RosProperty("address",IsReadOnly = true)]
        public string/*IP*/ Address { get; set; }

        /// <summary>
        /// The active user's name
        /// </summary>
        [RosProperty("user",IsReadOnly = true)]
        public string UserName { get; set; }

        /// <summary>
        /// The connection's Mac Address
        /// </summary>
        [RosProperty("mac-address",IsReadOnly = true)]
        public string MacAddress { get; set; }

        /// <summary>
        /// The connection's Mac Address
        /// </summary>
        [RosProperty("login-by",IsReadOnly = true)]
        public string LoginBy { get; set; }

        /// <summary>
        /// The amount of time the user has been connected
        /// </summary>
        [RosProperty("uptime",IsReadOnly = true)]
        public /*time*/ string UpTime { get; set; }

        /// <summary>
        /// The amount of time the connection has been idle
        /// </summary>
        [RosProperty("idle-time",IsReadOnly = true)]
        public /*time*/ string IdleTime { get; set; }

        /// <summary>
        /// The amount of time left for the session
        /// </summary>
        [RosProperty("session-time-left",IsReadOnly = true)]
        public /*time*/ string SessionTimeLeft { get; set; }

        /// <summary>
        /// The amount of time until the connection will timeout if it remains to be idle
        /// </summary>
        [RosProperty("idle-timeout",IsReadOnly = true)]
        public /*time*/ string IdleTimeout { get; set; }

        /// <summary>
        /// bytes-in: 
        /// </summary>
        [RosProperty("bytes-in",IsReadOnly = true)]
        public long BytesIn { get; private set; }

        /// <summary>
        /// bytes-out: 
        /// </summary>
        [RosProperty("bytes-out",IsReadOnly = true)]
        public long BytesOut { get; private set; }
    }
}
