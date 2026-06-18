using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// /tool/e-mail — SMTP e-mail client configuration singleton. Allows RouterOS to send e-mail
    /// notifications via an SMTP server. All settings can be overridden per-message by the
    /// <c>/tool/e-mail send</c> command. Only plain authentication and TLS encryption are supported.
    /// </summary>
    [TikEntity("/tool/e-mail", IsSingleton = true)]
    public class ToolEmail
    {
        /// <summary>
        /// server — SMTP server IP address or hostname.
        /// WinBox: "Server"
        /// </summary>
        [TikProperty("server", DefaultValue = "0.0.0.0")]
        public string Server { get; set; }

        /// <summary>
        /// port — SMTP server's TCP port number [0..65535].
        /// WinBox: "Port"
        /// </summary>
        [TikProperty("port", DefaultValue = "25")]
        public int Port { get; set; }

        /// <summary>
        /// tls — TLS encryption mode for the SMTP connection.
        /// <c>no</c>: plain text; <c>yes</c>: require TLS or drop; <c>starttls</c>: upgrade if available, continue without if not.
        /// WinBox: "TLS"
        /// </summary>
        /// <seealso cref="EmailTls"/>
        [TikProperty("tls", DefaultValue = "no")]
        public EmailTls Tls { get; set; }

        /// <summary>
        /// certificate-verification — TLS certificate trust chain validation mode.
        /// <c>no</c>: no verification; <c>yes</c>: full chain + CRL; <c>yes-without-crl</c>: chain only, skip CRL.
        /// WinBox: "Certificate Verification"
        /// </summary>
        /// <seealso cref="EmailCertificateVerification"/>
        [TikProperty("certificate-verification", DefaultValue = "no")]
        public EmailCertificateVerification CertificateVerification { get; set; }

        /// <summary>
        /// from — Name or e-mail address shown as the sender in outgoing messages.
        /// WinBox: "From"
        /// </summary>
        [TikProperty("from", DefaultValue = "<>")]
        public string From { get; set; }

        /// <summary>
        /// user — Username for SMTP server authentication.
        /// WinBox: "User"
        /// </summary>
        [TikProperty("user", DefaultValue = "")]
        public string User { get; set; }

        /// <summary>
        /// password — Password for SMTP server authentication (sensitive).
        /// WinBox: "Password"
        /// </summary>
        [TikProperty("password", DefaultValue = "")]
        public string Password { get; set; }

        /// <summary>
        /// vrf — VRF instance on which outgoing SMTP connections are created.
        /// WinBox: "VRF"
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        /// <summary>TLS encryption mode for <see cref="ToolEmail"/>.</summary>
        public enum EmailTls
        {
            /// <summary>no — plain-text connection, no TLS.</summary>
            [TikEnum("no")] No,
            /// <summary>yes — require TLS; drop connection if server does not offer it.</summary>
            [TikEnum("yes")] Yes,
            /// <summary>starttls — upgrade to TLS via STARTTLS if offered; continue in plain text otherwise.</summary>
            [TikEnum("starttls")] Starttls,
        }

        /// <summary>Certificate trust chain verification mode for <see cref="ToolEmail"/>.</summary>
        public enum EmailCertificateVerification
        {
            /// <summary>no — do not verify the server's certificate.</summary>
            [TikEnum("no")] No,
            /// <summary>yes — verify the full trust chain including CRL checks.</summary>
            [TikEnum("yes")] Yes,
            /// <summary>yes-without-crl — verify trust chain but skip CRL checks.</summary>
            [TikEnum("yes-without-crl")] YesWithoutCrl,
        }
    }
}
