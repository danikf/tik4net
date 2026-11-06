namespace tik4net.Objects.Radius
{
    /// <summary>A router service that asks a RADIUS server, a member of <see cref="Radius.Service"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>hotspot,ppp,login</c> reads back <c>ppp,login,hotspot</c>), so a list merge compares it by members.
    /// </remarks>
    public enum RadiusService
    {
        /// <summary>ppp — PPP authentication.</summary>
        [TikEnum("ppp")] Ppp,
        /// <summary>login — router user login.</summary>
        [TikEnum("login")] Login,
        /// <summary>hotspot — HotSpot users.</summary>
        [TikEnum("hotspot")] Hotspot,
        /// <summary>wireless — wireless MAC authentication.</summary>
        [TikEnum("wireless")] Wireless,
        /// <summary>dhcp — DHCP lease authorisation.</summary>
        [TikEnum("dhcp")] Dhcp,
        /// <summary>ipsec — IPsec EAP and XAuth.</summary>
        [TikEnum("ipsec")] Ipsec,
        /// <summary>dot1x — 802.1X port authentication.</summary>
        [TikEnum("dot1x")] Dot1x,
    }
}
