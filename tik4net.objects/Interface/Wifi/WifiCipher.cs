namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>A unicast cipher, a member of <see cref="WifiSecurity.Encryption"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>gcmp,ccmp,tkip</c> reads back <c>tkip,ccmp,gcmp</c>), so a list merge compares it by members.
    /// </remarks>
    public enum WifiCipher
    {
        /// <summary>tkip — TKIP (WPA).</summary>
        [TikEnum("tkip")] Tkip,
        /// <summary>ccmp — CCMP, AES-128 (WPA2).</summary>
        [TikEnum("ccmp")] Ccmp,
        /// <summary>gcmp — GCMP, AES-128.</summary>
        [TikEnum("gcmp")] Gcmp,
        /// <summary>ccmp-256 — CCMP, AES-256.</summary>
        [TikEnum("ccmp-256")] Ccmp256,
        /// <summary>gcmp-256 — GCMP, AES-256.</summary>
        [TikEnum("gcmp-256")] Gcmp256,
    }
}
