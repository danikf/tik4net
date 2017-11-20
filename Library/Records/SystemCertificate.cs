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

        [RosProperty("authority")] // Read-only
        public string Authority { get; private set; }

        [RosProperty("ca")] // Read-only
        public string Ca { get; private set; }

        [RosProperty("ca-crl-host")] // Read-only
        public string CaCrlHost { get; private set; }

        [RosProperty("ca-fingerprint")] // Read-only
        public string CaFingerprint { get; private set; }

        [RosProperty("crl")] // Read-only
        public string Crl { get; private set; }

        [RosProperty("dsa")] // Read-only
        public bool Dsa { get; private set; }

        [RosProperty("expired")] // Read-only
        public bool Expired { get; private set; }

        [RosProperty("fingerprint")] // Read-only
        public string FingerPrint { get; private set; }

        /// <summary>
        /// The date after which certificate wil be invalid.
        /// </summary>
        [RosProperty("invalid-after")] // Read-only
        public DateTime? InvalidAfter { get; private set; }

        /// <summary>
        ///  The date before which certificate is invalid.
        /// </summary>
        [RosProperty("invalid-before")] // Read-only
        public DateTime? InvalidBefore { get; private set; }

        [RosProperty("issued")] // Read-only
        public DateTime? Issued { get; private set; } 

        [RosProperty("issuer")] // Read-only
        public string Issuer { get; private set; }

        [RosProperty("private-key")] // Read-only
        public bool PrivateKey { get; private set; }

        [RosProperty("req-fingerprint")] // Read-only
        public string ReqFingerprint { get; private set; }

        [RosProperty("revoked")] // Read-only
        public string Revoked { get; private set; }

        [RosProperty("scep-url")] // Read-only
        public string ScepUrl { get; private set; }

        [RosProperty("serial-number")] // Read-only
        public string SerialNumber { get; private set; }

        [RosProperty("smart-card-key")] // Read-only
        public string SmartCardKey { get; private set; }

        /// <summary>
        /// Shows current status of scep client
        /// </summary>
        [RosProperty("status")] // Read-only
        public string Status { get; private set; }
    }
}
