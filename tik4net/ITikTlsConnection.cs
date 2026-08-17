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
    /// transport nobody remembered to wire it to (which is exactly what happened to
    /// <see cref="AllowInvalidCertificate"/> on API-SSL before 4.0).
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
        /// validation. Ignored when <see cref="CertificateValidationCallback"/> is set. Must be set before
        /// the connection is opened.
        /// </summary>
        bool AllowInvalidCertificate { get; set; }

        /// <summary>
        /// Optional custom certificate validation. When set it takes full control over accept/reject and
        /// <see cref="AllowInvalidCertificate"/> is ignored — the place for certificate pinning or for
        /// trusting a private CA. Must be set before the connection is opened.
        /// </summary>
        RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }
    }
}
