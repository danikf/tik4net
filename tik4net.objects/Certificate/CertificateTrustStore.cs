namespace tik4net.Objects.Certificate
{
    /// <summary>A service that may verify a peer against the certificate, a member of <see cref="Certificate.TrustStore"/> (RouterOS 7).</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>www,api,ipsec</c> reads back <c>ipsec,www,api</c>), so a list merge compares it by members.
    /// <see cref="All"/> stands for every service. A word a later RouterOS adds reads as a token rather than failing the read.
    /// </remarks>
    public enum CertificateTrustStore
    {
        /// <summary>all — every service.</summary>
        [TikEnum("all")] All,
        /// <summary>api — the api service.</summary>
        [TikEnum("api")] Api,
        /// <summary>capsman — the capsman service.</summary>
        [TikEnum("capsman")] Capsman,
        /// <summary>container — the container service.</summary>
        [TikEnum("container")] Container,
        /// <summary>dns — the dns service.</summary>
        [TikEnum("dns")] Dns,
        /// <summary>dot1x — the dot1x service.</summary>
        [TikEnum("dot1x")] Dot1x,
        /// <summary>email — the email service.</summary>
        [TikEnum("email")] Email,
        /// <summary>fetch — the fetch service.</summary>
        [TikEnum("fetch")] Fetch,
        /// <summary>ipsec — the ipsec service.</summary>
        [TikEnum("ipsec")] Ipsec,
        /// <summary>logging — the logging service.</summary>
        [TikEnum("logging")] Logging,
        /// <summary>lora — the lora service.</summary>
        [TikEnum("lora")] Lora,
        /// <summary>mqtt — the mqtt service.</summary>
        [TikEnum("mqtt")] Mqtt,
        /// <summary>netwatch — the netwatch service.</summary>
        [TikEnum("netwatch")] Netwatch,
        /// <summary>openflow — the openflow service.</summary>
        [TikEnum("openflow")] Openflow,
        /// <summary>ovpn — the ovpn service.</summary>
        [TikEnum("ovpn")] Ovpn,
        /// <summary>radius — the radius service.</summary>
        [TikEnum("radius")] Radius,
        /// <summary>reverse-proxy — the reverse-proxy service.</summary>
        [TikEnum("reverse-proxy")] ReverseProxy,
        /// <summary>sstp — the sstp service.</summary>
        [TikEnum("sstp")] Sstp,
        /// <summary>tr069 — the tr069 service.</summary>
        [TikEnum("tr069")] Tr069,
        /// <summary>userman — the userman service.</summary>
        [TikEnum("userman")] Userman,
        /// <summary>wiliot — the wiliot service.</summary>
        [TikEnum("wiliot")] Wiliot,
        /// <summary>wpa-eap — the wpa-eap service.</summary>
        [TikEnum("wpa-eap")] WpaEap,
        /// <summary>www — the www service.</summary>
        [TikEnum("www")] Www,
    }
}
