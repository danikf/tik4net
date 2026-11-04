namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>An authentication type of the wifi package, a member of <see cref="WifiSecurity.AuthenticationTypes"/>.</summary>
    public enum WifiAuthenticationType
    {
        /// <summary>owe — Opportunistic Wireless Encryption (an open network, encrypted).</summary>
        [TikEnum("owe")] Owe,
        /// <summary>wpa-psk — WPA with a pre-shared key.</summary>
        [TikEnum("wpa-psk")] WpaPsk,
        /// <summary>wpa2-psk — WPA2 with a pre-shared key.</summary>
        [TikEnum("wpa2-psk")] Wpa2Psk,
        /// <summary>wpa2-psk-sha2 — WPA2 with a pre-shared key and SHA-256 key derivation.</summary>
        [TikEnum("wpa2-psk-sha2")] Wpa2PskSha2,
        /// <summary>wpa3-psk — WPA3 (SAE).</summary>
        [TikEnum("wpa3-psk")] Wpa3Psk,
        /// <summary>wpa-eap — WPA with EAP (802.1X).</summary>
        [TikEnum("wpa-eap")] WpaEap,
        /// <summary>wpa2-eap — WPA2 with EAP (802.1X).</summary>
        [TikEnum("wpa2-eap")] Wpa2Eap,
        /// <summary>wpa3-eap — WPA3 with EAP.</summary>
        [TikEnum("wpa3-eap")] Wpa3Eap,
        /// <summary>wpa3-eap-192 — WPA3 with EAP, 192-bit mode.</summary>
        [TikEnum("wpa3-eap-192")] Wpa3Eap192,
    }

    /// <summary>An elliptic-curve group for SAE and EAP-PWD, a member of <see cref="WifiSecurity.DhGroups"/>.</summary>
    public enum WifiDhGroup
    {
        /// <summary>19 — 256-bit NIST curve (P-256).</summary>
        [TikEnum("19")] P256,
        /// <summary>20 — 384-bit NIST curve (P-384).</summary>
        [TikEnum("20")] P384,
        /// <summary>21 — 521-bit NIST curve (P-521).</summary>
        [TikEnum("21")] P521,
    }
}
