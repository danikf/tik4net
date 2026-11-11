using System;
using System.Net.Security;

namespace tik4net.Connection
{
    /// <summary>The certificate rule API-SSL and REST-SSL share (<see cref="ITikTlsConnection"/>).</summary>
    internal static class TikTlsValidation
    {
        /// <summary>
        /// The caller's callback; else, with a <see cref="ITikTlsConnection.CertificateFingerprint"/>, the pin (chain and
        /// name errors ignored); else accept-all when AllowInvalidCertificate; null = the platform's own validation.
        /// </summary>
        /// <param name="tls">The connection whose options decide.</param>
        /// <param name="onPinMismatch">Told the presented certificate's fingerprint when the pin refuses it, so the open
        /// can report the mismatch rather than the handshake's generic failure.</param>
        /// <exception cref="ArgumentException">The fingerprint is not in the documented form.</exception>
        internal static RemoteCertificateValidationCallback? For(ITikTlsConnection tls, Action<string>? onPinMismatch = null)
        {
            if (tls.CertificateValidationCallback != null)
                return tls.CertificateValidationCallback;
            if (tls.CertificateFingerprint != null)
            {
                string expected = PinnedFingerprint(tls)!;
                return (s, certificate, chain, errors) =>
                {
                    string actual = certificate == null ? "(no certificate)" : TikFingerprint.OfCertificate(certificate);
                    if (string.Equals(actual, expected, StringComparison.Ordinal))
                        return true;
                    onPinMismatch?.Invoke(actual);
                    return false;
                };
            }
            return tls.AllowInvalidCertificate ? (s, c, ch, e) => true : (RemoteCertificateValidationCallback?)null;
        }

        /// <summary>The pin in its canonical form, or <c>null</c> when none is set or a callback decides instead.</summary>
        /// <exception cref="ArgumentException">The fingerprint is not in the documented form.</exception>
        internal static string? PinnedFingerprint(ITikTlsConnection tls)
            => tls.CertificateValidationCallback != null || tls.CertificateFingerprint == null
                ? null
                : TikFingerprint.NormalizeCertificate(tls.CertificateFingerprint, nameof(ITikTlsConnection.CertificateFingerprint));
    }
}
