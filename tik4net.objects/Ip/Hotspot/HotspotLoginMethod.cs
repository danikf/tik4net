namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>A way a HotSpot client may log in, a member of <see cref="HotspotServerProfile.LoginBy"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>https,cookie,mac</c> reads back <c>mac,cookie,https</c>), so a list merge compares it by members.
    /// </remarks>
    public enum HotspotLoginMethod
    {
        /// <summary>mac — the client's MAC address is its login.</summary>
        [TikEnum("mac")] Mac,
        /// <summary>cookie — a cookie from an earlier login.</summary>
        [TikEnum("cookie")] Cookie,
        /// <summary>http-chap — a login page over HTTP, the password CHAP-hashed.</summary>
        [TikEnum("http-chap")] HttpChap,
        /// <summary>http-pap — a login page over HTTP, the password in clear text.</summary>
        [TikEnum("http-pap")] HttpPap,
        /// <summary>https — a login page over HTTPS.</summary>
        [TikEnum("https")] Https,
        /// <summary>trial — a trial login without credentials.</summary>
        [TikEnum("trial")] Trial,
        /// <summary>mac-cookie — a cookie bound to the client's MAC address.</summary>
        [TikEnum("mac-cookie")] MacCookie,
    }
}
