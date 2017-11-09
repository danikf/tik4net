using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /system/certificate
    /// </summary>
    [RosRecord("/certificate")]
    public class SystemCertificate : ISetRecord {
        [RosProperty(".id")]
        public string Id { get; set; }

        /// <summary>
        /// Name of the certificate.Name can be edited.
        /// </summary>
        [RosProperty("name")]
        public string Name { get; set; }
        
        [RosProperty("days-valid")] // Read-only after creation
        public int DaysValid { get; set; }
        
        [RosProperty("trusted")]
        public bool Trusted { get; set; }

        [RosProperty("common-name")] // Read-only after creation
        public string CommonName { get; set; }

        [RosProperty("copy-from")]
        public string CopyFrom { get; set; }

        [RosProperty("country")] // Read-only after creation
        public string Country { get; set; }

        [RosProperty("locality")] // Read-only after creation
        public string Locality { get; set; }

        [RosProperty("state")] // Read-only after creation
        public string State { get; set; }

        [RosProperty("organization")] // Read-only after creation
        public string Organization { get; set; }

        [RosProperty("unit")] // Read-only after creation
        public string Unit { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// 1024 | 1536 | 2048 | 4096 | 8192
        /// </remarks>
        [RosProperty("key-size")] // Read-only after creation
        public int KeySize { get; set; } = 2048;

        [RosProperty("subject-alt-name")] // Read-only after creation
        public string SubjectAltName { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// digital-signature | content-commitment | key-encipherment | data-encipherment | key-agreement | key-cert-sign | crl-sign | encipher-only | decipher-only
        /// </remarks>
        [RosProperty("key-usage")]
        public string KeyUsage { get; set; }

        [RosProperty("authority", IsReadOnly = true)]
        public string Authority { get; set; }

        [RosProperty("ca", IsReadOnly = true)]
        public string Ca { get; set; }

        [RosProperty("ca-crl-host", IsReadOnly = true)]
        public string CaCrlHost { get; set; }

        [RosProperty("ca-fingerprint", IsReadOnly = true)]
        public string CaFingerprint { get; set; }

        [RosProperty("crl", IsReadOnly = true)]
        public string Crl { get; set; }

        [RosProperty("dsa", IsReadOnly = true)]
        public bool Dsa { get; set; }

        [RosProperty("expired", IsReadOnly = true)]
        public bool Expired { get; set; }

        [RosProperty("fingerprint", IsReadOnly = true)]
        public string FingerPrint { get; set; }

        /// <summary>
        /// The date after which certificate wil be invalid.
        /// </summary>
        [RosProperty("invalid-after", IsReadOnly = true)]
        public string InvalidAfter { get; set; } // TODO: Make DateTime

        /// <summary>
        ///  The date before which certificate is invalid.
        /// </summary>
        [RosProperty("invalid-before", IsReadOnly = true)]
        public string InvalidBefore { get; set; } // TODO: Make DateTime

        [RosProperty("issued", IsReadOnly = true)]
        public string Issued { get; set; } // TODO: Make DateTime

        [RosProperty("issuer", IsReadOnly = true)]
        public string Issuer { get; set; }

        [RosProperty("private-key", IsReadOnly = true)]
        public bool PrivateKey { get; set; }

        [RosProperty("req-fingerprint", IsReadOnly = true)]
        public string ReqFingerprint { get; set; }

        [RosProperty("revoked", IsReadOnly = true)]
        public string Revoked { get; set; }

        [RosProperty("scep-url", IsReadOnly = true)]
        public string ScepUrl { get; set; }

        [RosProperty("serial-number", IsReadOnly = true)]
        public string SerialNumber { get; set; }

        [RosProperty("smart-card-key", IsReadOnly = true)]
        public string SmartCardKey { get; set; }

        /// <summary>
        /// Shows current status of scep client
        /// </summary>
        [RosProperty("status", IsReadOnly = true)]
        public string Status { get; set; }
    }
}
