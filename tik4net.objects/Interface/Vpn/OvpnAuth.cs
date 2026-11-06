namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>An HMAC algorithm, a member of <see cref="OvpnServer.Auth"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>sha512,md5,sha1</c> reads back <c>sha1,md5,sha512</c>), so a list merge compares it by members.
    /// <see cref="Sha256"/>, <see cref="Sha384"/> and <see cref="Sha512"/> are RouterOS 7 only.
    /// </remarks>
    public enum OvpnAuth
    {
        /// <summary>md5 — HMAC-MD5.</summary>
        [TikEnum("md5")] Md5,
        /// <summary>sha1 — HMAC-SHA1.</summary>
        [TikEnum("sha1")] Sha1,
        /// <summary>sha256 — HMAC-SHA256 (RouterOS 7).</summary>
        [TikEnum("sha256")] Sha256,
        /// <summary>sha384 — HMAC-SHA384 (RouterOS 7).</summary>
        [TikEnum("sha384")] Sha384,
        /// <summary>sha512 — HMAC-SHA512 (RouterOS 7).</summary>
        [TikEnum("sha512")] Sha512,
        /// <summary>null — no HMAC.</summary>
        [TikEnum("null")] Null,
    }
}
