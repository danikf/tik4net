namespace tik4net.Objects.Ip.Ipsec
{
    /// <summary>An integrity algorithm, a member of <see cref="IpsecProposal.AuthAlgorithms"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>sha512,md5,sha256,sha1</c> reads back <c>sha512,sha256,sha1,md5</c>), so a list merge compares it by members.
    /// </remarks>
    public enum IpsecAuthAlgorithm
    {
        /// <summary>md5 — HMAC-MD5.</summary>
        [TikEnum("md5")] Md5,
        /// <summary>sha1 — HMAC-SHA1.</summary>
        [TikEnum("sha1")] Sha1,
        /// <summary>sha256 — HMAC-SHA256.</summary>
        [TikEnum("sha256")] Sha256,
        /// <summary>sha512 — HMAC-SHA512.</summary>
        [TikEnum("sha512")] Sha512,
        /// <summary>null — no integrity check.</summary>
        [TikEnum("null")] Null,
    }
}
