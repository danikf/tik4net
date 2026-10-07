using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Certificate
{
    /// <summary>
    /// Represents a certificate or certificate template in the MikroTik certificate store (/certificate).
    /// Certificates are used for TLS/SSL, VPN (SSTP, OpenVPN, IPsec), RADIUS, and other services that
    /// require PKI-based authentication. A certificate template is prepared with writable fields and then
    /// signed; after signing most template fields become read-only on the resulting certificate.
    /// Only name, trusted, and trust-store remain writable on an already-signed certificate.
    /// </summary>
    [TikEntity("/certificate", IncludeDetails = true)]
    public class Certificate
    {
        /// <summary>.id — primary key of the certificate row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name — Certificate name (unique identifier in the certificate store).
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// common-name — Certificate Common Name (CN). Used as template field when creating a new
        /// certificate; becomes read-only after signing.
        /// WinBox: "Common Name"
        /// </summary>
        [TikProperty("common-name", WinboxLabel = "Common Name")]
        public TikField<string?> CommonName { get; set; }

        /// <summary>
        /// key-size — Certificate public key size in bits (or named EC curve).
        /// Default: 2048.
        /// WinBox: "Key Size"
        /// </summary>
        /// <seealso cref="KeySizeType"/>
        [TikProperty("key-size", DefaultValue = "2048", WinboxLabel = "Key Size")]
        public TikField<KeySizeType?> KeySize { get; set; }

        /// <summary>
        /// days-valid — Number of days the certificate remains valid after signing.
        /// Default: 365.
        /// WinBox: "Days Valid"
        /// </summary>
        [TikProperty("days-valid", DefaultValue = "365", WinboxLabel = "Days Valid")]
        public TikField<int?> DaysValid { get; set; }

        /// <summary>
        /// key-usage — Comma-separated list of certificate usage flags.
        /// Default: digital-signature,key-encipherment,data-encipherment,key-cert-sign,crl-sign,tls-server,tls-client.
        /// WinBox: "Key Usage"
        /// </summary>
        [TikProperty("key-usage", WinboxLabel = "Key Usage")]
        public TikField<string?> KeyUsage { get; set; }

        /// <summary>
        /// digest-algorithm — Hash algorithm used for signing the certificate.
        /// Default: sha256.
        /// WinBox: "Digest Algorithm"
        /// </summary>
        /// <seealso cref="DigestAlgorithmType"/>
        [TikProperty("digest-algorithm", DefaultValue = "sha256", WinboxLabel = "Digest Algorithm")]
        public TikField<DigestAlgorithmType?> DigestAlgorithm { get; set; }

        /// <summary>
        /// country — Certificate issuer country code (two-letter ISO 3166-1 alpha-2).
        /// WinBox: "Country"
        /// </summary>
        [TikProperty("country", WinboxLabel = "Country")]
        public TikField<string?> Country { get; set; }

        /// <summary>
        /// state — Certificate issuer state or province.
        /// WinBox: "State"
        /// </summary>
        [TikProperty("state", WinboxLabel = "State")]
        public TikField<string?> State { get; set; }

        /// <summary>
        /// locality — Certificate issuer locality (city).
        /// WinBox: "Locality"
        /// </summary>
        [TikProperty("locality", WinboxLabel = "Locality")]
        public TikField<string?> Locality { get; set; }

        /// <summary>
        /// organization — Certificate issuer organization name (O).
        /// WinBox: "Organization"
        /// </summary>
        [TikProperty("organization", WinboxLabel = "Organization")]
        public TikField<string?> Organization { get; set; }

        /// <summary>
        /// unit — Certificate issuer organizational unit (OU).
        /// WinBox: "Unit"
        /// </summary>
        [TikProperty("unit", WinboxLabel = "Unit")]
        public TikField<string?> Unit { get; set; }

        /// <summary>
        /// subject-alt-name — Certificate Subject Alternative Name (SAN).
        /// Format: DNS:name, IP:address, or email:address. Comma-separated for multiple values.
        /// WinBox: "Subject Alt. Name"
        /// </summary>
        [TikProperty("subject-alt-name", WinboxLabel = "Subject Alt. Name")]
        public TikField<TikValueList<string>?> SubjectAltName { get; set; }

        /// <summary>
        /// trusted — Whether this certificate is trusted for host certificate verification.
        /// Writable on both templates and signed certificates.
        /// WinBox: "Trusted"
        /// </summary>
        [TikProperty("trusted", WinboxLabel = "Trusted")]
        public TikField<bool?> Trusted { get; set; }

        /// <summary>
        /// trust-store — Comma-separated list of services that are permitted to use this certificate
        /// for verification. Possible values: all, capsman, dns, email, ipsec, mqtt, openflow, radius,
        /// sstp, userman, www, api, container, dot1x, fetch, lora, netwatch, ovpn, tr069, wpa-eap.
        /// Default: all.
        /// WinBox: "Trust Store"
        /// </summary>
        [TikProperty("trust-store", DefaultValue = "all", WinboxLabel = "Trust Store", MinRouterOs = "7")]
        public TikField<string?> TrustStore { get; set; }

        /// <summary>
        /// comment — Optional free-text comment for this certificate entry.
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        // ── Read-only fields (populated after signing / import) ──────────────────

        /// <summary>
        /// fingerprint — SHA1 fingerprint of the certificate (read-only, present on signed certs).
        /// WinBox: "Fingerprint"
        /// </summary>
        [TikProperty("fingerprint", IsReadOnly = true, WinboxLabel = "Fingerprint")]
        public TikField<string?> Fingerprint { get; private set; }

        /// <summary>
        /// serial-number — Certificate serial number assigned by the CA (read-only).
        /// WinBox: "Serial Number"
        /// </summary>
        [TikProperty("serial-number", IsReadOnly = true, WinboxLabel = "Serial Number")]
        public TikField<string?> SerialNumber { get; private set; }

        /// <summary>
        /// invalid-before — Date and time before which the certificate is not yet valid (read-only), in UTC.
        /// Read from either spelling (7.x <c>2026-07-25 10:24:52</c>, 6.x <c>jul/25/2026 10:24:52</c>); no time zone is applied
        /// (<see cref="DateTimeKind.Unspecified"/>).
        /// WinBox: "Invalid Before"
        /// </summary>
        [TikProperty("invalid-before", IsReadOnly = true, WinboxLabel = "Invalid Before")]
        public TikField<DateTime?> InvalidBefore { get; private set; }

        /// <summary>
        /// invalid-after — Date and time after which the certificate has expired (read-only), in UTC.
        /// Read from either spelling (7.x <c>2026-07-25 10:24:52</c>, 6.x <c>jul/25/2026 10:24:52</c>); no time zone is applied
        /// (<see cref="DateTimeKind.Unspecified"/>).
        /// WinBox: "Invalid After"
        /// </summary>
        [TikProperty("invalid-after", IsReadOnly = true, WinboxLabel = "Invalid After")]
        public TikField<DateTime?> InvalidAfter { get; private set; }

        /// <summary>
        /// expires-after — Human-readable time remaining before the certificate expires (read-only).
        /// WinBox: "Expires After"
        /// </summary>
        [TikProperty("expires-after", IsReadOnly = true, WinboxLabel = "Expires After")]
        public TikField<TikDuration?> ExpiresAfter { get; private set; }

        /// <summary>
        /// ca — Name of the CA certificate that signed this certificate (read-only, device-signed only).
        /// Removing the CA certificate cascades to delete all certificates it issued.
        /// WinBox: "CA"
        /// </summary>
        [TikProperty("ca", IsReadOnly = true, WinboxLabel = "CA")]
        public TikField<string?> Ca { get; private set; }

        /// <summary>
        /// issuer — Distinguished Name of the Certificate Authority that issued this certificate (read-only).
        /// WinBox: "Issuer"
        /// </summary>
        [TikProperty("issuer", IsReadOnly = true, WinboxLabel = "Issuer")]
        public TikField<string?> Issuer { get; private set; }

        /// <summary>
        /// akid — Authority Key Identifier: identifies the CA public key used to sign this certificate (read-only).
        /// WinBox: "AKID"
        /// </summary>
        [TikProperty("akid", IsReadOnly = true, WinboxLabel = "AKID")]
        public TikField<string?> Akid { get; private set; }

        /// <summary>
        /// skid — Subject Key Identifier: identifies the public key contained in this certificate (read-only).
        /// WinBox: "SKID"
        /// </summary>
        [TikProperty("skid", IsReadOnly = true, WinboxLabel = "SKID")]
        public TikField<string?> Skid { get; private set; }

        /// <summary>
        /// key-type — Private key algorithm type, e.g. RSA or EC (read-only).
        /// WinBox: "Key Type"
        /// </summary>
        [TikProperty("key-type", IsReadOnly = true, WinboxLabel = "Key Type")]
        public TikField<string?> KeyType { get; private set; }

        /// <summary>
        /// revoked — Timestamp when the certificate was revoked (read-only, device-specific revocation).
        /// WinBox: "Revoked"
        /// </summary>
        [TikProperty("revoked", IsReadOnly = true, WinboxLabel = "revoked")]
        public TikField<string?> Revoked { get; private set; }

        /// <summary>
        /// acme-status — Status reported by the ACME client for this certificate (read-only).
        /// WinBox: "ACME Status"
        /// </summary>
        [TikProperty("acme-status", IsReadOnly = true, WinboxLabel = "ACME Status", MinRouterOs = "7.22")]
        public TikField<string?> AcmeStatus { get; private set; }

        /// <summary>
        /// domain-names — Domain names managed by the ACME client for this certificate (read-only).
        /// WinBox: "Domain Names"
        /// </summary>
        [TikProperty("domain-names", IsReadOnly = true, MinRouterOs = "7.22")]
        public TikField<string?> DomainNames { get; private set; }

        /// <summary>
        /// directory-url — ACME directory URL used to obtain this certificate (read-only).
        /// WinBox: "Directory URL"
        /// </summary>
        [TikProperty("directory-url", IsReadOnly = true, WinboxLabel = "Directory URL", MinRouterOs = "7.22")]
        public TikField<string?> DirectoryUrl { get; private set; }

        // ── Enums ────────────────────────────────────────────────────────────────

        /// <summary>Certificate public key size or named elliptic-curve identifier.</summary>
        public enum KeySizeType
        {
            /// <summary>1024-bit RSA key.</summary>
            [TikEnum("1024")] Rsa1024,
            /// <summary>1536-bit RSA key.</summary>
            [TikEnum("1536")] Rsa1536,
            /// <summary>2048-bit RSA key (default).</summary>
            [TikEnum("2048")] Rsa2048,
            /// <summary>4096-bit RSA key.</summary>
            [TikEnum("4096")] Rsa4096,
            /// <summary>8192-bit RSA key.</summary>
            [TikEnum("8192")] Rsa8192,
            /// <summary>NIST P-256 elliptic curve (prime256v1 / secp256r1).</summary>
            [TikEnum("prime256v1")] Prime256v1,
            /// <summary>NIST P-384 elliptic curve (secp384r1).</summary>
            [TikEnum("secp384r1")] Secp384r1,
            /// <summary>NIST P-521 elliptic curve (secp521r1).</summary>
            [TikEnum("secp521r1")] Secp521r1,
        }

        /// <summary>Digest (hash) algorithm used when signing the certificate.</summary>
        public enum DigestAlgorithmType
        {
            /// <summary>MD5 — legacy, avoid on new certificates.</summary>
            [TikEnum("md5")] Md5,
            /// <summary>SHA-1 — legacy, avoid on new certificates.</summary>
            [TikEnum("sha1")] Sha1,
            /// <summary>SHA-256 (default).</summary>
            [TikEnum("sha256")] Sha256,
            /// <summary>SHA-384.</summary>
            [TikEnum("sha384")] Sha384,
            /// <summary>SHA-512.</summary>
            [TikEnum("sha512")] Sha512,
        }

        /// <summary>Returns the certificate name, suitable for display and logging.</summary>
        public override string? ToString() => Name.Value;
    }
}
