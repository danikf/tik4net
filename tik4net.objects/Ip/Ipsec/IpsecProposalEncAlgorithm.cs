namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>A Phase 2 encryption algorithm, a member of <see cref="IpsecProposal.EncAlgorithms"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>aes-128-cbc,aes-256-cbc,3des</c> reads back <c>aes-256-cbc,aes-128-cbc,3des</c>), so a list merge compares it by members.
    /// <see cref="Chacha20poly1305"/> is RouterOS 7 only.
    /// </remarks>
    public enum IpsecProposalEncAlgorithm
    {
        /// <summary>des — DES.</summary>
        [TikEnum("des")] Des,
        /// <summary>3des — Triple DES.</summary>
        [TikEnum("3des")] TripleDes,
        /// <summary>aes-128-cbc — AES, 128-bit key, CBC mode.</summary>
        [TikEnum("aes-128-cbc")] Aes128Cbc,
        /// <summary>aes-192-cbc — AES, 192-bit key, CBC mode.</summary>
        [TikEnum("aes-192-cbc")] Aes192Cbc,
        /// <summary>aes-256-cbc — AES, 256-bit key, CBC mode.</summary>
        [TikEnum("aes-256-cbc")] Aes256Cbc,
        /// <summary>aes-128-ctr — AES, 128-bit key, counter mode.</summary>
        [TikEnum("aes-128-ctr")] Aes128Ctr,
        /// <summary>aes-192-ctr — AES, 192-bit key, counter mode.</summary>
        [TikEnum("aes-192-ctr")] Aes192Ctr,
        /// <summary>aes-256-ctr — AES, 256-bit key, counter mode.</summary>
        [TikEnum("aes-256-ctr")] Aes256Ctr,
        /// <summary>aes-128-gcm — AES, 128-bit key, GCM (authenticated) mode.</summary>
        [TikEnum("aes-128-gcm")] Aes128Gcm,
        /// <summary>aes-192-gcm — AES, 192-bit key, GCM (authenticated) mode.</summary>
        [TikEnum("aes-192-gcm")] Aes192Gcm,
        /// <summary>aes-256-gcm — AES, 256-bit key, GCM (authenticated) mode.</summary>
        [TikEnum("aes-256-gcm")] Aes256Gcm,
        /// <summary>blowfish — Blowfish.</summary>
        [TikEnum("blowfish")] Blowfish,
        /// <summary>twofish — Twofish.</summary>
        [TikEnum("twofish")] Twofish,
        /// <summary>camellia-128 — Camellia, 128-bit key.</summary>
        [TikEnum("camellia-128")] Camellia128,
        /// <summary>camellia-192 — Camellia, 192-bit key.</summary>
        [TikEnum("camellia-192")] Camellia192,
        /// <summary>camellia-256 — Camellia, 256-bit key.</summary>
        [TikEnum("camellia-256")] Camellia256,
        /// <summary>chacha20poly1305 — ChaCha20-Poly1305 (RouterOS 7).</summary>
        [TikEnum("chacha20poly1305")] Chacha20poly1305,
        /// <summary>null — no encryption.</summary>
        [TikEnum("null")] Null,
    }
}
