using System.Net.Security;

namespace tik4net
{
    /// <summary>
    /// Implemented by connections that speak TLS to the router (API-SSL, REST-SSL), and therefore have to
    /// decide whether to trust its certificate.
    /// </summary>
    /// <remarks>
    /// It exists so <see cref="TikConnectionSetup"/> can apply its certificate options by <b>asking the
    /// connection whether they mean anything to it</b> rather than by knowing which concrete transports are
    /// TLS-capable — the failure this interface rules out is an option that is silently dropped on a
    /// transport nobody remembered to wire it to.
    /// <para>
    /// A plain-text transport does not implement it. The plain and TLS forms of one transport are the same
    /// class here (<c>ApiConnection</c> is API or API-SSL depending on how it was created), so implementing
    /// this interface means "this class can run over TLS", not "this instance does" — the properties are
    /// simply unused on the plain instance.
    /// </para>
    /// </remarks>
    public interface ITikTlsConnection
    {
        /// <summary>
        /// When <c>true</c>, a self-signed or otherwise invalid router certificate is accepted without
        /// validation. Ignored when <see cref="CertificateFingerprint"/> or <see cref="CertificateValidationCallback"/>
        /// is set. Must be set before the connection is opened.
        /// </summary>
        bool AllowInvalidCertificate { get; set; }

        /// <summary>
        /// The router's certificate pinned by its SHA-256 fingerprint, or <c>null</c> (the default) for no pin. Must be set
        /// before the connection is opened.
        /// </summary>
        /// <remarks>
        /// <para>The SHA-256 of the DER certificate as 64 hex digits, colons optional, any case — what RouterOS shows as
        /// <c>fingerprint</c> in <c>/certificate print</c> for the certificate on <c>/ip service</c> <c>api-ssl</c> or
        /// <c>www-ssl</c>.</para>
        /// <para>When set, the certificate the router presents must have this fingerprint, and chain and name errors are
        /// ignored: a self-signed certificate, or one issued to another name or to no IP, is accepted as long as it is
        /// this one. It overrides <see cref="AllowInvalidCertificate"/>; <see cref="CertificateValidationCallback"/> still
        /// wins over it. A certificate with another fingerprint fails <c>Open</c> with
        /// <see cref="TikConnectionHostKeyMismatchException"/>, and a value not in this form with
        /// <see cref="System.ArgumentException"/>. Renewing the router's certificate changes the fingerprint.</para>
        /// </remarks>
        string? CertificateFingerprint { get; set; }

        /// <summary>
        /// Optional custom certificate validation. When set it takes full control over accept/reject, and
        /// <see cref="CertificateFingerprint"/> and <see cref="AllowInvalidCertificate"/> are ignored — the place for
        /// trusting a private CA, or for a rule a fingerprint cannot state. Must be set before the connection is opened.
        /// </summary>
        RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }
    }
}
