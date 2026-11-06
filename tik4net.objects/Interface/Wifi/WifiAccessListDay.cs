namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>A day of the week, a member of <see cref="WifiAccessList.Days"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>wed,mon,sun</c> reads back <c>sun,mon,wed</c>), so a list merge compares it by members.
    /// </remarks>
    public enum WifiAccessListDay
    {
        /// <summary>sun — Sunday.</summary>
        [TikEnum("sun")] Sunday,
        /// <summary>mon — Monday.</summary>
        [TikEnum("mon")] Monday,
        /// <summary>tue — Tuesday.</summary>
        [TikEnum("tue")] Tuesday,
        /// <summary>wed — Wednesday.</summary>
        [TikEnum("wed")] Wednesday,
        /// <summary>thu — Thursday.</summary>
        [TikEnum("thu")] Thursday,
        /// <summary>fri — Friday.</summary>
        [TikEnum("fri")] Friday,
        /// <summary>sat — Saturday.</summary>
        [TikEnum("sat")] Saturday,
    }
}
