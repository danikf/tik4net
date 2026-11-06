namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>A TLS cipher suite, a member of the SSTP client's and server's <c>ciphers</c> (RouterOS 7).</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>aes256-gcm-sha384,aes256-sha</c> reads back <c>aes256-sha,aes256-gcm-sha384</c>), so a list merge compares it by members.
    /// </remarks>
    public enum SstpCipher
    {
        /// <summary>aes256-sha — AES-256 CBC with SHA-1.</summary>
        [TikEnum("aes256-sha")] Aes256Sha,
        /// <summary>aes256-gcm-sha384 — AES-256 GCM with SHA-384.</summary>
        [TikEnum("aes256-gcm-sha384")] Aes256GcmSha384,
    }
}
