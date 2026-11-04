namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>
    /// A WPA authentication type of the legacy wireless package, a member of
    /// <see cref="WirelessSecurityProfile.AuthenticationTypes"/> and of the CAPsMAN security
    /// <c>authentication-types</c>. The wifi package has its own, <see cref="Wifi.WifiAuthenticationType"/>.
    /// </summary>
    public enum WirelessAuthenticationType
    {
        /// <summary>wpa-psk — WPA with a pre-shared key.</summary>
        [TikEnum("wpa-psk")] WpaPsk,
        /// <summary>wpa2-psk — WPA2 with a pre-shared key.</summary>
        [TikEnum("wpa2-psk")] Wpa2Psk,
        /// <summary>wpa-eap — WPA with EAP (802.1X).</summary>
        [TikEnum("wpa-eap")] WpaEap,
        /// <summary>wpa2-eap — WPA2 with EAP (802.1X).</summary>
        [TikEnum("wpa2-eap")] Wpa2Eap,
    }
}
