namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// ip/hotspot/active
    /// 
    /// The sessions of the users currently logged in to a HotSpot server.
    /// <para>
    /// The menu offers <c>remove</c> and neither <c>add</c> nor <c>set</c>: a row can be dropped -
    /// <see cref="TikConnectionExtensions.Delete">Delete</see> kicks the session - while every field stays
    /// read-only.
    /// </para>
    /// </summary>
    [TikEntity("/ip/hotspot/active", SupportedOperations = TikEntityOperations.Remove)]
    public class HotspotActive
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// Server
        /// </summary>
        [TikProperty("server", IsReadOnly = true, WinboxLabel = "Server")]
        public TikValue<string?> Server { get; set; }

        /// <summary>
        /// address: IP address
        /// </summary>
        [TikProperty("address", IsReadOnly = true, WinboxLabel = "Address")]
        public TikValue<string?> Address { get; set; }

        /// <summary>
        /// The active user's name
        /// </summary>
        [TikProperty("user", IsReadOnly = true, WinboxLabel = "User")]
        public TikValue<string?> UserName { get; set; }

        /// <summary>
        /// The connection's Mac Address
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true, WinboxLabel = "MAC Address")]
        public TikValue<string?> MacAddress { get; set; }

        /// <summary>
        /// The connection's Mac Address
        /// </summary>
        [TikProperty("login-by", IsReadOnly = true, WinboxLabel = "Login By")]
        public TikValue<string?> LoginBy { get; set; }

        /// <summary>
        /// The amount of time the user has been connected
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true, WinboxLabel = "Uptime")]
        public TikValue<string?> /*time*/ UpTime { get; set; }

        /// <summary>
        /// The amount of time the connection has been idle
        /// </summary>
        [TikProperty("idle-time", IsReadOnly = true, WinboxLabel = "Idle Time")]
        public TikValue<string?> /*time*/ IdleTime { get; set; }

        /// <summary>
        /// The amount of time left for the session
        /// </summary>
        [TikProperty("session-time-left", IsReadOnly = true, WinboxLabel = "Session Time Left")]
        public TikValue<string?> /*time*/ SessionTimeLeft { get; set; }

        /// <summary>
        /// The amount of time until the connection will timeout if it remains to be idle
        /// </summary>
        [TikProperty("idle-timeout", IsReadOnly = true, WinboxLabel = "Idle Timeout")]
        public TikValue<string?> /*time*/ IdleTimeout { get; set; }

        /// <summary>
        /// bytes-in: 
        /// </summary>
        [TikProperty("bytes-in", IsReadOnly = true, WinboxLabel = "Bytes In")]
        public TikValue<long?> BytesIn { get; private set; }

        /// <summary>
        /// bytes-out: 
        /// </summary>
        [TikProperty("bytes-out", IsReadOnly = true, WinboxLabel = "Bytes Out")]
        public TikValue<long?> BytesOut { get; private set; }
    }
}
