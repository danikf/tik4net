namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// ip/hotspot/user
    /// 
    /// This is the menu, where client's user/password information is actually added, additional configuration options for HotSpot users are configured here as well.
    /// </summary>
    [TikEntity("ip/hotspot/active", IsReadOnly = true)]
    public class HotspotActive
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Server
        /// </summary>
        [TikProperty("server", IsReadOnly = true)]
        public string Server { get; set; }

        /// <summary>
        /// address: IP address
        /// </summary>
        [TikProperty("address", IsReadOnly = true)]
        public string/*IP*/ Address { get; set; }

        /// <summary>
        /// The active user's name
        /// </summary>
        [TikProperty("user", IsReadOnly = true)]
        public string UserName { get; set; }

        /// <summary>
        /// The connection's Mac Address
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; set; }

        /// <summary>
        /// The connection's Mac Address
        /// </summary>
        [TikProperty("login-by", IsReadOnly = true)]
        public string LoginBy { get; set; }

        /// <summary>
        /// The amount of time the user has been connected
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public /*time*/ string UpTime { get; set; }

        /// <summary>
        /// The amount of time the connection has been idle
        /// </summary>
        [TikProperty("idle-time", IsReadOnly = true)]
        public /*time*/ string IdleTime { get; set; }

        /// <summary>
        /// The amount of time left for the session
        /// </summary>
        [TikProperty("session-time-left", IsReadOnly = true)]
        public /*time*/ string SessionTimeLeft { get; set; }

        /// <summary>
        /// The amount of time until the connection will timeout if it remains to be idle
        /// </summary>
        [TikProperty("idle-timeout", IsReadOnly = true)]
        public /*time*/ string IdleTimeout { get; set; }

        /// <summary>
        /// bytes-in: 
        /// </summary>
        [TikProperty("bytes-in", IsReadOnly = true)]
        public long BytesIn { get; private set; }

        /// <summary>
        /// bytes-out: 
        /// </summary>
        [TikProperty("bytes-out", IsReadOnly = true)]
        public long BytesOut { get; private set; }
    }
}
