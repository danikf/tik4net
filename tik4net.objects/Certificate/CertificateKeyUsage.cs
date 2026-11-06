namespace tik4net.Objects.Certificate
{
    /// <summary>A key or extended key usage, a member of <see cref="Certificate.KeyUsage"/>.</summary>
    /// <remarks>
    /// The router keeps the list as a set and prints it in its own order (<c>tls-client,digital-signature,key-cert-sign</c> reads back <c>digital-signature,key-cert-sign,tls-client</c>), so a list merge compares it by members.
    /// The four marked RouterOS 6 are refused by RouterOS 7.
    /// </remarks>
    public enum CertificateKeyUsage
    {
        /// <summary>digital-signature — digital signatures.</summary>
        [TikEnum("digital-signature")] DigitalSignature,
        /// <summary>content-commitment — non-repudiation.</summary>
        [TikEnum("content-commitment")] ContentCommitment,
        /// <summary>key-encipherment — encrypting keys.</summary>
        [TikEnum("key-encipherment")] KeyEncipherment,
        /// <summary>data-encipherment — encrypting data.</summary>
        [TikEnum("data-encipherment")] DataEncipherment,
        /// <summary>key-agreement — key agreement.</summary>
        [TikEnum("key-agreement")] KeyAgreement,
        /// <summary>key-cert-sign — signing certificates (a CA).</summary>
        [TikEnum("key-cert-sign")] KeyCertSign,
        /// <summary>crl-sign — signing CRLs.</summary>
        [TikEnum("crl-sign")] CrlSign,
        /// <summary>encipher-only — key agreement, encrypting only.</summary>
        [TikEnum("encipher-only")] EncipherOnly,
        /// <summary>decipher-only — key agreement, decrypting only.</summary>
        [TikEnum("decipher-only")] DecipherOnly,
        /// <summary>tls-server — TLS server authentication.</summary>
        [TikEnum("tls-server")] TlsServer,
        /// <summary>tls-client — TLS client authentication.</summary>
        [TikEnum("tls-client")] TlsClient,
        /// <summary>code-sign — code signing.</summary>
        [TikEnum("code-sign")] CodeSign,
        /// <summary>email-protect — e-mail protection.</summary>
        [TikEnum("email-protect")] EmailProtect,
        /// <summary>timestamp — time stamping.</summary>
        [TikEnum("timestamp")] Timestamp,
        /// <summary>ocsp-sign — signing OCSP answers.</summary>
        [TikEnum("ocsp-sign")] OcspSign,
        /// <summary>dvcs — data validation and certification.</summary>
        [TikEnum("dvcs")] Dvcs,
        /// <summary>ipsec-end-system — IPsec end system (RouterOS 6).</summary>
        [TikEnum("ipsec-end-system")] IpsecEndSystem,
        /// <summary>ipsec-tunnel — IPsec tunnel (RouterOS 6).</summary>
        [TikEnum("ipsec-tunnel")] IpsecTunnel,
        /// <summary>ipsec-user — IPsec user (RouterOS 6).</summary>
        [TikEnum("ipsec-user")] IpsecUser,
        /// <summary>server-gated-crypto — server-gated cryptography (RouterOS 6).</summary>
        [TikEnum("server-gated-crypto")] ServerGatedCrypto,
    }
}
