using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>
    /// Wireless security profiles
    /// </summary>
    [TikEntity("interface/wireless/security-profiles")]
    public class WirelessSecurityProfile
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// Mode for <see cref="Mode"/>.
        /// </summary>
        public enum SecurityMode
        {
            /// <summary>
            /// dynamic-keys - WPA mode.
            /// </summary>
            [TikEnum("dynamic-keys")]
            DynamicKeys,

            /// <summary>
            /// none - Encryption is not used. Encrypted frames are not accepted.
            /// </summary>
            [TikEnum("none")]
            None,

            /// <summary>
            /// static-keys-optional - WEP mode. Support encryption and decryption, but allow also to receive and send unencrypted frames. Device will send unencrypted frames if encryption algorithm is specified as none. 
            /// Station in static-keys-optional mode will not connect to an access point in static-keys-required mode.
            /// </summary>
            [TikEnum("static-keys-optional")]
            StaticKeysOptional,

            /// <summary>
            /// static-keys-required - WEP mode. Do not accept and do not send unencrypted frames. 
            /// </summary>
            [TikEnum("static-keys-required")]
            StaticKeysRequiered
        }

        /// <summary>
        /// mode
        /// </summary>
        [TikProperty("mode", IsMandatory = true)]
        public SecurityMode /* none, static-keys-optional, static-keys-required, dynamic-keys*/Mode { get; set; }

        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// management-protection
        /// </summary>
        [TikProperty("management-protection")]
        public bool ManagementProtection { get; set; }

        /// <summary>
        /// management-protection-key
        /// </summary>
        [TikProperty("management-protection-key")]
        public string ManagementProtectionKey { get; set; }

        /// <summary>
        /// wpa-pre-shared-key
        /// </summary>
        [TikProperty("wpa-pre-shared-key")]
        public string WpaPreSharedKey { get; set; }

        /// <summary>
        /// wpa2-pre-shared-key
        /// </summary>
        [TikProperty("wpa2-pre-shared-key")]
        public string Wpa2PreSharedKey { get; set; }

        /// <summary>
        /// authentication-types
        /// Comma seperated string
        /// </summary>
        [TikProperty("authentication-types")]
        public string AuthenticationTypes { get; set; }

        /// <summary>
        /// group-ciphers
        /// Comma seperated string
        /// </summary>
        [TikProperty("group-ciphers")]
        public string /*tkip, aes-ccm*/GroupCiphers { get; set; }

        /// <summary>
        /// unicast-ciphers
        /// Comma seperated string
        /// </summary>
        [TikProperty("unicast-ciphers")]
        public string /*tkip, aes-ccm*/UnicastCiphers { get; set; }

        /// <summary>
        /// supplicant-identity
        /// </summary>
        [TikProperty("supplicant-identity")]
        public string /*tkip, aes-ccm*/SupplicantIdentiy { get; set; }

        /// <summary>
        /// group-key-update - (time interval in the 30s..1h range; default value: 5m) : Controls how often access point updates group key. This key is used to encrypt all broadcast and multicast frames.
        /// </summary>
        [TikProperty("group-key-update")]
        public string GroupKeyUpdate { get; set; }
    }
}
