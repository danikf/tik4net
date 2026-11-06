namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>A data-channel cipher, a member of <see cref="OvpnServer.Cipher"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>aes256-cbc,aes128-cbc,blowfish128</c> reads back <c>blowfish128,aes128-cbc,aes256-cbc</c>), so a list merge compares it by members.
    /// RouterOS 6 names the CBC ciphers <c>aes128</c>, <c>aes192</c> and <c>aes256</c>; RouterOS 7 adds the mode to the name.
    /// </remarks>
    public enum OvpnCipher
    {
        /// <summary>blowfish128 — Blowfish, 128-bit key.</summary>
        [TikEnum("blowfish128")] Blowfish128,
        /// <summary>aes128 — AES-128 CBC (RouterOS 6 spelling).</summary>
        [TikEnum("aes128")] Aes128,
        /// <summary>aes192 — AES-192 CBC (RouterOS 6 spelling).</summary>
        [TikEnum("aes192")] Aes192,
        /// <summary>aes256 — AES-256 CBC (RouterOS 6 spelling).</summary>
        [TikEnum("aes256")] Aes256,
        /// <summary>aes128-cbc — AES-128, CBC mode (RouterOS 7).</summary>
        [TikEnum("aes128-cbc")] Aes128Cbc,
        /// <summary>aes192-cbc — AES-192, CBC mode (RouterOS 7).</summary>
        [TikEnum("aes192-cbc")] Aes192Cbc,
        /// <summary>aes256-cbc — AES-256, CBC mode (RouterOS 7).</summary>
        [TikEnum("aes256-cbc")] Aes256Cbc,
        /// <summary>aes128-gcm — AES-128, GCM mode (RouterOS 7).</summary>
        [TikEnum("aes128-gcm")] Aes128Gcm,
        /// <summary>aes192-gcm — AES-192, GCM mode (RouterOS 7).</summary>
        [TikEnum("aes192-gcm")] Aes192Gcm,
        /// <summary>aes256-gcm — AES-256, GCM mode (RouterOS 7).</summary>
        [TikEnum("aes256-gcm")] Aes256Gcm,
        /// <summary>null — no encryption.</summary>
        [TikEnum("null")] Null,
    }
}
