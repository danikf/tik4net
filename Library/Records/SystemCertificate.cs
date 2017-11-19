using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /system/certificate
    /// </summary>
    [RosRecord("/certificate")]
    public class SystemCertificate : SetRecordBase {
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
        public string Authority { get; private set; }

        [RosProperty("ca", IsReadOnly = true)]
        public string Ca { get; private set; }

        [RosProperty("ca-crl-host", IsReadOnly = true)]
        public string CaCrlHost { get; private set; }

        [RosProperty("ca-fingerprint", IsReadOnly = true)]
        public string CaFingerprint { get; private set; }

        [RosProperty("crl", IsReadOnly = true)]
        public string Crl { get; private set; }

        [RosProperty("dsa", IsReadOnly = true)]
        public bool Dsa { get; private set; }

        [RosProperty("expired", IsReadOnly = true)]
        public bool Expired { get; private set; }

        [RosProperty("fingerprint", IsReadOnly = true)]
        public string FingerPrint { get; private set; }

        /// <summary>
        /// The date after which certificate wil be invalid.
        /// </summary>
        [RosProperty("invalid-after", IsReadOnly = true)]
        public DateTime? InvalidAfter { get; private set; }

        /// <summary>
        ///  The date before which certificate is invalid.
        /// </summary>
        [RosProperty("invalid-before", IsReadOnly = true)]
        public DateTime? InvalidBefore { get; private set; }

        [RosProperty("issued", IsReadOnly = true)]
        public DateTime? Issued { get; private set; } 

        [RosProperty("issuer", IsReadOnly = true)]
        public string Issuer { get; private set; }

        [RosProperty("private-key", IsReadOnly = true)]
        public bool PrivateKey { get; private set; }

        [RosProperty("req-fingerprint", IsReadOnly = true)]
        public string ReqFingerprint { get; private set; }

        [RosProperty("revoked", IsReadOnly = true)]
        public string Revoked { get; private set; }

        [RosProperty("scep-url", IsReadOnly = true)]
        public string ScepUrl { get; private set; }

        [RosProperty("serial-number", IsReadOnly = true)]
        public string SerialNumber { get; private set; }

        [RosProperty("smart-card-key", IsReadOnly = true)]
        public string SmartCardKey { get; private set; }

        /// <summary>
        /// Shows current status of scep client
        /// </summary>
        [RosProperty("status", IsReadOnly = true)]
        public string Status { get; private set; }
    }
}
