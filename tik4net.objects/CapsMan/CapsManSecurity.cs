using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/security
    ///
    /// CAPsMAN security profile (legacy CAPsMAN, RouterOS 6.x).  A security profile defines the
    /// authentication, encryption, and key-management settings applied to client associations on a
    /// CAP radio.  Profiles are referenced by name from /caps-man/configuration (security field) or
    /// used inline via the security.* dotted-notation overrides on a configuration profile.
    ///
    /// Supported authentication types: open (empty), wpa-psk, wpa2-psk, wpa-eap, wpa2-eap.
    /// Supported unicast ciphers: aes-ccm, tkip (comma-separated multi-select).
    /// </summary>
    [TikEntity("/caps-man/security", IncludeDetails = true)]
    public class CapsManSecurity
    {
        // ── TLS mode ──────────────────────────────────────────────────────────

        /// <summary>TLS mode values for the <see cref="TlsMode"/> property.</summary>
        /// <seealso cref="TlsMode"/>
        public enum TlsModeType
        {
            /// <summary>no-certificates — EAP-TLS without any certificate verification (least secure).</summary>
            [TikEnum("no-certificates")] NoCertificates,
            /// <summary>dont-verify-certificate — AP presents a certificate but the client does not verify it.</summary>
            [TikEnum("dont-verify-certificate")] DontVerifyCertificate,
            /// <summary>verify-certificate — require and verify the client certificate against the CA.</summary>
            [TikEnum("verify-certificate")] VerifyCertificate,
            /// <summary>verify-certificate-with-crl — verify certificate and check the CRL for revocation.</summary>
            [TikEnum("verify-certificate-with-crl")] VerifyCertificateWithCrl,
        }

        // ── Group encryption ──────────────────────────────────────────────────

        /// <summary>Group (broadcast/multicast) cipher values for the <see cref="GroupEncryption"/> property.</summary>
        /// <seealso cref="GroupEncryption"/>
        public enum GroupEncryptionType
        {
            /// <summary>aes-ccm — AES-CCMP cipher for group keys (default; stronger, recommended).</summary>
            [TikEnum("aes-ccm")] AesCcm,
            /// <summary>tkip — TKIP cipher for group keys (legacy; required for WPA-only clients).</summary>
            [TikEnum("tkip")] Tkip,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this security profile; referenced by /caps-man/configuration.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Authentication ────────────────────────────────────────────────────

        /// <summary>
        /// authentication-types — comma-separated list of accepted authentication protocols.
        /// Possible values: wpa-psk, wpa2-psk, wpa-eap, wpa2-eap.
        /// Empty string means open (no authentication).
        /// </summary>
        [TikProperty("authentication-types")]
        public string AuthenticationTypes { get; set; }

        // ── Unicast encryption ────────────────────────────────────────────────

        /// <summary>
        /// encryption — comma-separated list of accepted unicast frame cipher algorithms.
        /// Possible values: aes-ccm, tkip. Empty means no explicit override.
        /// </summary>
        [TikProperty("encryption")]
        public string Encryption { get; set; }

        // ── Group (broadcast/multicast) encryption ────────────────────────────

        /// <summary>
        /// group-encryption — cipher algorithm used for broadcast and multicast frames;
        /// all associated clients must support this cipher.
        /// Default: aes-ccm.
        /// <seealso cref="GroupEncryptionType"/>
        /// </summary>
        [TikProperty("group-encryption", DefaultValue = "aes-ccm")]
        public GroupEncryptionType GroupEncryption { get; set; }

        // ── Key management ────────────────────────────────────────────────────

        /// <summary>
        /// group-key-update — interval at which the group cipher key is rotated (30s..1h).
        /// Default: 5m.
        /// </summary>
        [TikProperty("group-key-update", DefaultValue = "5m")]
        public string/*time*/ GroupKeyUpdate { get; set; }

        // ── PSK ───────────────────────────────────────────────────────────────

        /// <summary>
        /// passphrase — WPA/WPA2 pre-shared key (PSK) used with wpa-psk / wpa2-psk authentication.
        /// 8–63 ASCII characters, or 64 hex digits for a raw PMK.
        /// </summary>
        [TikProperty("passphrase")]
        public string Passphrase { get; set; }

        // ── EAP ───────────────────────────────────────────────────────────────

        /// <summary>
        /// eap-methods — comma-separated list of EAP methods accepted by the AP.
        /// Possible values: eap-tls, passthrough (relay to RADIUS).
        /// Empty means no EAP (PSK only).
        /// </summary>
        [TikProperty("eap-methods")]
        public string EapMethods { get; set; }

        /// <summary>
        /// eap-radius-accounting — when true, RADIUS accounting messages are sent for EAP-authenticated clients.
        /// Default: no.
        /// </summary>
        [TikProperty("eap-radius-accounting", DefaultValue = "no")]
        public bool EapRadiusAccounting { get; set; }

        // ── TLS / certificates ────────────────────────────────────────────────

        /// <summary>
        /// tls-mode — client certificate verification behaviour used during EAP-TLS authentication.
        /// Possible values: no-certificates, dont-verify-certificate, verify-certificate,
        /// verify-certificate-with-crl.
        /// Empty string means the field is not configured (use only when EAP-TLS is active).
        /// <seealso cref="TlsModeType"/>
        /// </summary>
        [TikProperty("tls-mode")]
        public string TlsMode { get; set; }

        /// <summary>
        /// tls-certificate — name of the certificate (from /certificate) presented by the AP
        /// during EAP-TLS authentication. Use "none" to disable certificate use.
        /// </summary>
        [TikProperty("tls-certificate")]
        public string TlsCertificate { get; set; }

        // ── PMKID ─────────────────────────────────────────────────────────────

        /// <summary>
        /// disable-pmkid — when true, the PMKID element is omitted from EAPOL frames, which
        /// prevents certain brute-force attacks against the 4-way handshake.
        /// Default: no.
        /// </summary>
        [TikProperty("disable-pmkid", DefaultValue = "no")]
        public bool DisablePmkid { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// comment — short free-text description of this security profile.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
