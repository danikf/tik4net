namespace tik4net.Objects.Ppp
{
    /// <summary>
    /// A PPP authentication protocol, a member of the <c>authentication</c> list of the PPTP, L2TP, SSTP and PPPoE
    /// servers and of the <c>allow</c> / <c>authentication</c> list of their clients.
    /// </summary>
    /// <remarks>
    /// The router keeps these lists as a set and prints them in its own order (<c>mschap2,pap,chap</c> reads back
    /// <c>pap,chap,mschap2</c>; measured on 6.49.21 and 7.x), so they are <c>TikValueList&lt;PppAuthentication&gt;</c>,
    /// which a list merge compares by members.
    /// </remarks>
    public enum PppAuthentication
    {
        /// <summary>pap — Password Authentication Protocol (the password in clear text).</summary>
        [TikEnum("pap")] Pap,
        /// <summary>chap — Challenge Handshake Authentication Protocol.</summary>
        [TikEnum("chap")] Chap,
        /// <summary>mschap1 — Microsoft CHAP version 1.</summary>
        [TikEnum("mschap1")] Mschap1,
        /// <summary>mschap2 — Microsoft CHAP version 2.</summary>
        [TikEnum("mschap2")] Mschap2,
    }
}
