using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace tik4net.Connection
{
    /// <summary>
    /// The two pin forms <see cref="TikConnectionSetup.CertificateFingerprint"/> and
    /// <see cref="TikConnectionSetup.SshHostKeyFingerprint"/> take: read, checked and compared in one canonical spelling.
    /// </summary>
    internal static class TikFingerprint
    {
        private const string SshPrefix = "SHA256:";

        /// <summary>
        /// A certificate pin as lowercase hex without separators: the SHA-256 of the DER certificate, 64 hex digits, colons
        /// allowed between them (<c>/certificate print</c> shows the bare lowercase form).
        /// </summary>
        /// <exception cref="ArgumentException">Not 32 bytes of hex.</exception>
        internal static string NormalizeCertificate(string text, string optionName)
        {
            Guard.ArgumentNotNull(text, nameof(text));
            string hex = text.Trim().Replace(":", string.Empty);
            if (hex.Length != 64 || !IsHex(hex))
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "{0} must be the SHA-256 of the router's certificate: 64 hex digits, colons optional, as "
                    + "/certificate print shows 'fingerprint'. '{1}' is not.", optionName, text), optionName);
            return hex.ToLowerInvariant();
        }

        /// <summary>
        /// An SSH host-key pin as <c>SHA256:</c> plus unpadded base64 of the 32-byte digest, the form <c>ssh-keygen -lf</c>
        /// and RouterOS <c>/ip ssh print</c> (<c>host-key-fingerprint</c>) show. The prefix may be left out, and a base64
        /// padding <c>=</c> is ignored.
        /// </summary>
        /// <exception cref="ArgumentException">Not the base64 of 32 bytes.</exception>
        internal static string NormalizeSshHostKey(string text, string optionName)
        {
            Guard.ArgumentNotNull(text, nameof(text));
            string b64 = text.Trim();
            if (b64.StartsWith(SshPrefix, StringComparison.OrdinalIgnoreCase))
                b64 = b64.Substring(SshPrefix.Length);
            b64 = b64.TrimEnd('=');
            byte[]? digest = null;
            try { digest = Convert.FromBase64String(b64 + new string('=', (4 - b64.Length % 4) % 4)); }
            catch (FormatException) { /* reported below */ }
            if (digest == null || digest.Length != 32)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "{0} must be the SHA-256 of the router's SSH host key, as 'SHA256:' and base64, the form ssh-keygen -lf "
                    + "and /ip ssh print ('host-key-fingerprint') show. '{1}' is not.", optionName, text), optionName);
            return SshPrefix + Convert.ToBase64String(digest).TrimEnd('=');
        }

        /// <summary>The SHA-256 of the certificate's DER encoding, lowercase hex: what <see cref="NormalizeCertificate"/> compares against.</summary>
        internal static string OfCertificate(X509Certificate certificate)
        {
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(certificate.GetRawCertData()));
        }

        /// <summary>The SHA-256 of an SSH host key blob, in the <see cref="NormalizeSshHostKey"/> form.</summary>
        internal static string OfSshHostKey(byte[] hostKeyBlob)
        {
            using (var sha = SHA256.Create())
                return SshPrefix + Convert.ToBase64String(sha.ComputeHash(hostKeyBlob)).TrimEnd('=');
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
