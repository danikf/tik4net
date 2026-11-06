namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>A Phase 1 encryption algorithm, a member of <see cref="IpsecProfile.EncAlgorithm"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>3des,aes-256,aes-128</c> reads back <c>aes-256,aes-128,3des</c>), so a list merge compares it by members.
    /// <see cref="Blowfish"/> and the Camellia ciphers are refused by RouterOS 7.
    /// </remarks>
    public enum IpsecProfileEncAlgorithm
    {
        /// <summary>des — DES.</summary>
        [TikEnum("des")] Des,
        /// <summary>3des — Triple DES.</summary>
        [TikEnum("3des")] TripleDes,
        /// <summary>aes-128 — AES, 128-bit key.</summary>
        [TikEnum("aes-128")] Aes128,
        /// <summary>aes-192 — AES, 192-bit key.</summary>
        [TikEnum("aes-192")] Aes192,
        /// <summary>aes-256 — AES, 256-bit key.</summary>
        [TikEnum("aes-256")] Aes256,
        /// <summary>blowfish — Blowfish (RouterOS 6).</summary>
        [TikEnum("blowfish")] Blowfish,
        /// <summary>camellia-128 — Camellia, 128-bit key (RouterOS 6).</summary>
        [TikEnum("camellia-128")] Camellia128,
        /// <summary>camellia-192 — Camellia, 192-bit key (RouterOS 6).</summary>
        [TikEnum("camellia-192")] Camellia192,
        /// <summary>camellia-256 — Camellia, 256-bit key (RouterOS 6).</summary>
        [TikEnum("camellia-256")] Camellia256,
    }
}
