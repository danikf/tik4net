namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/security
    ///
    /// WiFi security profile (ROS 7 wifi package).  A security profile groups all authentication
    /// and encryption settings (WPA2/WPA3-PSK, WPA-EAP, OWE, 802.11r fast roaming, 802.11w
    /// management frame protection, WPS) into a reusable object that can be referenced by name
    /// from /interface/wifi/configuration or /interface/wifi entries.
    /// </summary>
    [TikEntity("/interface/wifi/security", IncludeDetails = true)]
    public class WifiSecurity
    {
        // ── Authentication type list ──────────────────────────────────────────
        // (multi-value list field; no dedicated enum — kept as string)

        // ── Beacon protection ─────────────────────────────────────────────────

        /// <summary>Beacon protection mode values for the <see cref="BeaconProtection"/> property.</summary>
        /// <seealso cref="BeaconProtection"/>
        public enum BeaconProtectionMode
        {
            /// <summary>disabled — beacon integrity protection is disabled (default).</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>enabled — beacon integrity protection is enabled (mandatory for 802.11be).</summary>
            [TikEnum("enabled")] Enabled,
        }

        // ── EAP certificate mode ──────────────────────────────────────────────

        /// <summary>EAP TLS certificate handling mode values for the <see cref="EapCertificateMode"/> property.</summary>
        /// <seealso cref="EapCertificateMode"/>
        public enum EapCertificateModeType
        {
            /// <summary>dont-verify-certificate — accept any server certificate without validation (default).</summary>
            [TikEnum("dont-verify-certificate")] DontVerifyCertificate,
            /// <summary>no-certificates — no TLS certificates are used.</summary>
            [TikEnum("no-certificates")] NoCertificates,
            /// <summary>verify-certificate — verify the server certificate against the trusted CA store.</summary>
            [TikEnum("verify-certificate")] VerifyCertificate,
            /// <summary>verify-certificate-with-crl — verify the server certificate and check CRL.</summary>
            [TikEnum("verify-certificate-with-crl")] VerifyCertificateWithCrl,
        }

        // ── Group encryption ──────────────────────────────────────────────────

        /// <summary>Multicast/group traffic encryption cipher values for the <see cref="GroupEncryption"/> property.</summary>
        /// <seealso cref="GroupEncryption"/>
        public enum GroupEncryptionCipher
        {
            /// <summary>ccmp — AES-CCMP 128-bit (default).</summary>
            [TikEnum("ccmp")] Ccmp,
            /// <summary>ccmp-256 — AES-CCMP 256-bit.</summary>
            [TikEnum("ccmp-256")] Ccmp256,
            /// <summary>gcmp — AES-GCMP 128-bit.</summary>
            [TikEnum("gcmp")] Gcmp,
            /// <summary>gcmp-256 — AES-GCMP 256-bit.</summary>
            [TikEnum("gcmp-256")] Gcmp256,
            /// <summary>tkip — TKIP (legacy; WPA only).</summary>
            [TikEnum("tkip")] Tkip,
        }

        // ── Management frame protection ───────────────────────────────────────

        /// <summary>802.11w management frame protection mode values for the <see cref="ManagementProtection"/> property.</summary>
        /// <seealso cref="ManagementProtection"/>
        public enum ManagementProtectionMode
        {
            /// <summary>allowed — management frame protection is optional; peers with or without MFP are accepted (default).</summary>
            [TikEnum("allowed")] Allowed,
            /// <summary>disabled — management frame protection is not used.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>required — only peers supporting management frame protection are accepted.</summary>
            [TikEnum("required")] Required,
        }

        // ── Management frame encryption ───────────────────────────────────────

        /// <summary>Protected management frame encryption cipher values for the <see cref="ManagementEncryption"/> property.</summary>
        /// <seealso cref="ManagementEncryption"/>
        public enum ManagementEncryptionCipher
        {
            /// <summary>cmac — AES-CMAC 128-bit (default).</summary>
            [TikEnum("cmac")] Cmac,
            /// <summary>cmac-256 — AES-CMAC 256-bit.</summary>
            [TikEnum("cmac-256")] Cmac256,
            /// <summary>gmac — AES-GMAC 128-bit.</summary>
            [TikEnum("gmac")] Gmac,
            /// <summary>gmac-256 — AES-GMAC 256-bit.</summary>
            [TikEnum("gmac-256")] Gmac256,
        }

        // ── SAE password element derivation ───────────────────────────────────

        /// <summary>SAE password element (PWE) derivation method values for the <see cref="SaePwe"/> property.</summary>
        /// <seealso cref="SaePwe"/>
        public enum SaePweMethod
        {
            /// <summary>both — support both hunting-and-pecking and hash-to-element (default).</summary>
            [TikEnum("both")] Both,
            /// <summary>hash-to-element — use the hash-to-element method only (RFC 9494).</summary>
            [TikEnum("hash-to-element")] HashToElement,
            /// <summary>hunting-and-pecking — use the original hunting-and-pecking method only.</summary>
            [TikEnum("hunting-and-pecking")] HuntingAndPecking,
        }

        // ── WPS mode ──────────────────────────────────────────────────────────

        /// <summary>Wi-Fi Protected Setup (WPS) acceptance mode values for the <see cref="Wps"/> property.</summary>
        /// <seealso cref="Wps"/>
        public enum WpsMode
        {
            /// <summary>disable — WPS is disabled (default).</summary>
            [TikEnum("disable")] Disable,
            /// <summary>push-button — accept WPS push-button requests for 2 minutes after a wps-push-button command.</summary>
            [TikEnum("push-button")] PushButton,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this security profile.
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Authentication ────────────────────────────────────────────────────

        /// <summary>
        /// authentication-types — comma-separated list of enabled authentication methods.
        /// Valid values: owe, wpa-eap, wpa-psk, wpa2-eap, wpa2-psk, wpa2-psk-sha2,
        /// wpa3-eap, wpa3-eap-192, wpa3-psk.
        /// Default: empty (open network / no authentication).
        /// WinBox: "Authentication Types"
        /// </summary>
        [TikProperty("authentication-types")]
        public string AuthenticationTypes { get; set; }

        /// <summary>
        /// passphrase — PSK passphrase for WPA2-PSK / WPA3-PSK authentication.
        /// 8–63 characters for WPA2; no minimum for WPA3-SAE.
        /// WinBox: "Passphrase"
        /// </summary>
        [TikProperty("passphrase")]
        public string Passphrase { get; set; }

        // ── WPS ───────────────────────────────────────────────────────────────

        /// <summary>
        /// wps — Wi-Fi Protected Setup acceptance mode.
        /// Default: disable.
        /// <seealso cref="WpsMode"/>
        /// WinBox: "WPS"
        /// </summary>
        [TikProperty("wps", DefaultValue = "disable")]
        public WpsMode Wps { get; set; }

        // ── Encryption ciphers ────────────────────────────────────────────────

        /// <summary>
        /// encryption — comma-separated list of unicast traffic ciphers to accept.
        /// Valid values: ccmp, ccmp-256, gcmp, gcmp-256, tkip.
        /// Default: ccmp.
        /// WinBox: "Encryption"
        /// </summary>
        [TikProperty("encryption", DefaultValue = "ccmp")]
        public string Encryption { get; set; }

        /// <summary>
        /// group-encryption — cipher used for multicast/broadcast traffic.
        /// Default: ccmp.
        /// <seealso cref="GroupEncryptionCipher"/>
        /// WinBox: "Group Encryption"
        /// </summary>
        [TikProperty("group-encryption", DefaultValue = "ccmp")]
        public GroupEncryptionCipher GroupEncryption { get; set; }

        /// <summary>
        /// group-key-update — interval for renewing the group temporal key (GTK).
        /// Default: 24h.
        /// WinBox: "Group Key Update"
        /// </summary>
        [TikProperty("group-key-update", DefaultValue = "24h")]
        public string/*time*/ GroupKeyUpdate { get; set; }

        // ── Management frame protection (802.11w) ─────────────────────────────

        /// <summary>
        /// management-protection — 802.11w protected management frame (PMF) mode.
        /// Default: allowed.
        /// <seealso cref="ManagementProtectionMode"/>
        /// WinBox: "Management Protection"
        /// </summary>
        [TikProperty("management-protection", DefaultValue = "allowed")]
        public ManagementProtectionMode ManagementProtection { get; set; }

        /// <summary>
        /// management-encryption — cipher used to encrypt protected management frames.
        /// Default: cmac.
        /// <seealso cref="ManagementEncryptionCipher"/>
        /// WinBox: "Management Encryption"
        /// </summary>
        [TikProperty("management-encryption", DefaultValue = "cmac")]
        public ManagementEncryptionCipher ManagementEncryption { get; set; }

        // ── Beacon protection ─────────────────────────────────────────────────

        /// <summary>
        /// beacon-protection — enable 802.11 beacon integrity protection.
        /// Enabled by default for 802.11be; otherwise disabled.
        /// Default: disabled.
        /// <seealso cref="BeaconProtectionMode"/>
        /// WinBox: "Beacon Protection"
        /// </summary>
        [TikProperty("beacon-protection", DefaultValue = "disabled")]
        public BeaconProtectionMode BeaconProtection { get; set; }

        // ── PMKID ─────────────────────────────────────────────────────────────

        /// <summary>
        /// disable-pmkid — when true, suppress PMKID from EAPOL frames (mitigates certain offline attacks).
        /// Default: no.
        /// WinBox: "Disable PMKID"
        /// </summary>
        [TikProperty("disable-pmkid", DefaultValue = "no")]
        public bool DisablePmkid { get; set; }

        // ── SAE (WPA3-PSK) settings ───────────────────────────────────────────

        /// <summary>
        /// sae-pwe — SAE password element derivation method(s) to support.
        /// Default: both.
        /// <seealso cref="SaePweMethod"/>
        /// WinBox: "SAE PWE"
        /// </summary>
        [TikProperty("sae-pwe", DefaultValue = "both")]
        public SaePweMethod SaePwe { get; set; }

        /// <summary>
        /// sae-anti-clogging-threshold — number of in-progress SAE authentications before
        /// the AP starts requiring an anti-clogging token (cookie). Set to 0 to use the router default (5).
        /// "disabled" disables the threshold.
        /// WinBox: "SAE Anti-Clogging Threshold"
        /// </summary>
        [TikProperty("sae-anti-clogging-threshold", DefaultValue = "0")]
        public int SaeAntiCloggingThreshold { get; set; }

        /// <summary>
        /// sae-max-failure-rate — maximum failed SAE associations per minute before new SAE
        /// requests are rejected. Set to 0 to use the router default (40).
        /// WinBox: "SAE Max Failure Rate"
        /// </summary>
        [TikProperty("sae-max-failure-rate", DefaultValue = "0")]
        public int SaeMaxFailureRate { get; set; }

        // ── DH groups (SAE/EAP) ───────────────────────────────────────────────

        /// <summary>
        /// dh-groups — comma-separated list of elliptic-curve DH group identifiers for SAE/EAP-PWD.
        /// Valid values: 19 (P-256), 20 (P-384), 21 (P-521).
        /// WinBox: "DH Groups"
        /// </summary>
        [TikProperty("dh-groups")]
        public string DhGroups { get; set; }

        // ── EAP settings ──────────────────────────────────────────────────────

        /// <summary>
        /// eap-methods — comma-separated list of supported EAP methods.
        /// Valid values: peap, tls, ttls.
        /// Default: all methods (peap,tls,ttls).
        /// WinBox: "EAP Methods"
        /// </summary>
        [TikProperty("eap-methods")]
        public string EapMethods { get; set; }

        /// <summary>
        /// eap-certificate-mode — how the TLS certificate is handled during EAP authentication.
        /// Default: dont-verify-certificate.
        /// <seealso cref="EapCertificateModeType"/>
        /// WinBox: "EAP Certificate Mode"
        /// </summary>
        [TikProperty("eap-certificate-mode", DefaultValue = "dont-verify-certificate")]
        public EapCertificateModeType EapCertificateMode { get; set; }

        /// <summary>
        /// eap-username — username sent during EAP authentication (PEAP/TTLS outer identity).
        /// WinBox: "EAP Username"
        /// </summary>
        [TikProperty("eap-username")]
        public string EapUsername { get; set; }

        /// <summary>
        /// eap-password — password used for PEAP/TTLS EAP authentication.
        /// WinBox: "EAP Password"
        /// </summary>
        [TikProperty("eap-password")]
        public string EapPassword { get; set; }

        /// <summary>
        /// eap-anonymous-identity — outer/anonymous identity sent in EAP identity response
        /// before the tunnelled authentication. Hides the real username from passive observers.
        /// WinBox: "EAP Anonymous Identity"
        /// </summary>
        [TikProperty("eap-anonymous-identity")]
        public string EapAnonymousIdentity { get; set; }

        /// <summary>
        /// eap-tls-certificate — name of the client certificate from the system certificate
        /// store to use for EAP-TLS authentication.
        /// WinBox: "EAP TLS Certificate"
        /// </summary>
        [TikProperty("eap-tls-certificate")]
        public string EapTlsCertificate { get; set; }

        /// <summary>
        /// eap-accounting — when true, send RADIUS accounting data for EAP-authenticated peers.
        /// Default: no.
        /// WinBox: "EAP Accounting"
        /// </summary>
        [TikProperty("eap-accounting", DefaultValue = "no")]
        public bool EapAccounting { get; set; }

        // ── 802.11r Fast BSS Transition (FT) ─────────────────────────────────

        /// <summary>
        /// ft — enable 802.11r Fast BSS Transition (fast roaming).
        /// Default: no.
        /// WinBox: "FT"
        /// </summary>
        [TikProperty("ft", DefaultValue = "no")]
        public bool Ft { get; set; }

        /// <summary>
        /// ft-over-ds — enable FT over the Distribution System (DS) rather than the air.
        /// Default: no.
        /// WinBox: "FT Over DS"
        /// </summary>
        [TikProperty("ft-over-ds", DefaultValue = "no")]
        public bool FtOverDs { get; set; }

        /// <summary>
        /// ft-mobility-domain — 802.11r mobility domain identifier (0..65535).
        /// Default: 44484. Set to 0 here to let the router use its default.
        /// WinBox: "FT Mobility Domain"
        /// </summary>
        [TikProperty("ft-mobility-domain", DefaultValue = "0")]
        public int FtMobilityDomain { get; set; }

        /// <summary>
        /// ft-nas-identifier — PMK-R0 key holder identifier (2–96 hex characters).
        /// Default: derived from router MAC address.
        /// WinBox: "FT NAS Identifier"
        /// </summary>
        [TikProperty("ft-nas-identifier")]
        public string FtNasIdentifier { get; set; }

        /// <summary>
        /// ft-r0-key-lifetime — lifetime of the PMK-R0 encryption key (up to ~7 days).
        /// Default: 600000s.
        /// WinBox: "FT R0 Key Lifetime"
        /// </summary>
        [TikProperty("ft-r0-key-lifetime", DefaultValue = "600000s")]
        public string/*time*/ FtR0KeyLifetime { get; set; }

        /// <summary>
        /// ft-reassociation-deadline — time window (0..70s) within which a client must
        /// complete fast-roaming reassociation. Default: 20s.
        /// Set to 0 here to let the router use its default.
        /// WinBox: "FT Reassociation Deadline"
        /// </summary>
        [TikProperty("ft-reassociation-deadline", DefaultValue = "0")]
        public int FtReassociationDeadline { get; set; }

        /// <summary>
        /// ft-preserve-vlanid — when true, preserve the VLAN ID during 802.11r transition.
        /// Default: yes.
        /// WinBox: "FT Preserve VLAN ID"
        /// </summary>
        [TikProperty("ft-preserve-vlanid", DefaultValue = "yes")]
        public bool FtPreserveVlanid { get; set; }

        // ── OWE ───────────────────────────────────────────────────────────────

        /// <summary>
        /// owe-transition-interface — interface name (or "auto") used in OWE transition mode
        /// to pair an open network with its OWE-protected counterpart.
        /// WinBox: "OWE Transition Interface"
        /// </summary>
        [TikProperty("owe-transition-interface")]
        public string OweTransitionInterface { get; set; }

        // ── Connection control ────────────────────────────────────────────────

        /// <summary>
        /// connect-group — name of the connect-group used to prevent a single client MAC
        /// from associating to multiple APs in the same group simultaneously.
        /// Default: default.
        /// WinBox: "Connect Group"
        /// </summary>
        [TikProperty("connect-group", DefaultValue = "default")]
        public string ConnectGroup { get; set; }

        /// <summary>
        /// connect-priority — space-separated accept-priority and hold-priority values that
        /// determine connection-handling order when duplicate MAC addresses appear.
        /// WinBox: "Connect Priority"
        /// </summary>
        [TikProperty("connect-priority")]
        public string ConnectPriority { get; set; }

        // ── Multi-passphrase ──────────────────────────────────────────────────

        /// <summary>
        /// multi-passphrase-group — name of a /interface/wifi/security/multi-passphrase group
        /// for Per-PSK (PPSK) authentication where different clients use different passphrases.
        /// WinBox: "Multi-Passphrase Group"
        /// </summary>
        [TikProperty("multi-passphrase-group")]
        public string MultiPassphraseGroup { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disabled — when true this security profile is administratively disabled.
        /// Default: no.
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description.
        /// WinBox: "Comment"
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
