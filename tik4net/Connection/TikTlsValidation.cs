using System.Net.Security;

namespace tik4net.Connection
{
    /// <summary>The certificate rule API-SSL and REST-SSL share (<see cref="ITikTlsConnection"/>).</summary>
    internal static class TikTlsValidation
    {
        /// <summary>The caller's callback, else accept-all when AllowInvalidCertificate; null = the platform's own validation.</summary>
        internal static RemoteCertificateValidationCallback? For(ITikTlsConnection tls)
            => tls.CertificateValidationCallback
               ?? (tls.AllowInvalidCertificate ? (s, c, ch, e) => true : (RemoteCertificateValidationCallback?)null);
    }
}
