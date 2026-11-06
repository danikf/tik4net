namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>A cipher of the wireless package, a member of the security profile's <c>unicast-ciphers</c> and <c>group-ciphers</c> and of the CAPsMAN <c>encryption</c>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order, so a list merge compares it by members. The
    /// security profile prints <c>tkip,aes-ccm</c>, CAPsMAN <c>aes-ccm,tkip</c>, whatever order was written.
    /// </remarks>
    public enum WirelessCipher
    {
        /// <summary>tkip — TKIP (WPA).</summary>
        [TikEnum("tkip")] Tkip,
        /// <summary>aes-ccm — AES-CCMP (WPA2).</summary>
        [TikEnum("aes-ccm")] AesCcm,
    }
}
