using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>
    /// Wireless security profiles
    /// </summary>
    [TikEntity("/interface/wireless/security-profiles")]
    public class WirelessSecurityProfile
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string? Comment { get; set; }

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
        public string? Name { get; set; }

        /// <summary>
        /// management-protection
        /// </summary>
        [TikProperty("management-protection")]
        public bool? ManagementProtection { get; set; }

        /// <summary>
        /// management-protection-key
        /// <para>
        /// <b>Write-only over the CLI transports.</b> RouterOS's CLI omits secret fields from
        /// <c>print as-value</c> entirely (<c>detail</c> does not help), so over <c>Telnet</c>,
        /// <c>Ssh</c>, <c>MacTelnet</c>, <c>WinboxCli</c> and <c>WinboxCliMac</c> this reads back
        /// <c>null</c> — indistinguishable from a key the router holds empty. The binary API, REST and
        /// <c>WinboxNative</c> report it. Writing works on every transport.
        /// </para>
        /// </summary>
        [TikProperty("management-protection-key")]
        public string? ManagementProtectionKey { get; set; }

        /// <summary>
        /// wpa-pre-shared-key
        /// <para>
        /// <b>Write-only over the CLI transports.</b> RouterOS's CLI omits secret fields from
        /// <c>print as-value</c> entirely (<c>detail</c> does not help), so over <c>Telnet</c>,
        /// <c>Ssh</c>, <c>MacTelnet</c>, <c>WinboxCli</c> and <c>WinboxCliMac</c> this reads back
        /// <c>null</c> — indistinguishable from a key the router holds empty. The binary API, REST and
        /// <c>WinboxNative</c> report it. Writing works on every transport.
        /// </para>
        /// </summary>
        [TikProperty("wpa-pre-shared-key")]
        public string? WpaPreSharedKey { get; set; }

        /// <summary>
        /// wpa2-pre-shared-key
        /// <para>
        /// <b>Write-only over the CLI transports.</b> RouterOS's CLI omits secret fields from
        /// <c>print as-value</c> entirely (<c>detail</c> does not help), so over <c>Telnet</c>,
        /// <c>Ssh</c>, <c>MacTelnet</c>, <c>WinboxCli</c> and <c>WinboxCliMac</c> this reads back
        /// <c>null</c> — indistinguishable from a key the router holds empty. The binary API, REST and
        /// <c>WinboxNative</c> report it. Writing works on every transport.
        /// </para>
        /// </summary>
        [TikProperty("wpa2-pre-shared-key")]
        public string? Wpa2PreSharedKey { get; set; }

        /// <summary>
        /// authentication-types
        /// Comma seperated string
        /// </summary>
        [TikProperty("authentication-types")]
        public string? AuthenticationTypes { get; set; }

        /// <summary>
        /// group-ciphers
        /// Comma seperated string
        /// </summary>
        [TikProperty("group-ciphers")]
        public string? /*tkip, aes-ccm*/GroupCiphers { get; set; }

        /// <summary>
        /// unicast-ciphers
        /// Comma seperated string
        /// </summary>
        [TikProperty("unicast-ciphers")]
        public string? /*tkip, aes-ccm*/UnicastCiphers { get; set; }

        /// <summary>
        /// supplicant-identity
        /// </summary>
        [TikProperty("supplicant-identity")]
        public string? /*tkip, aes-ccm*/SupplicantIdentiy { get; set; }

        /// <summary>
        /// group-key-update - (time interval in the 30s..1h range; default value: 5m) : Controls how often access point updates group key. This key is used to encrypt all broadcast and multicast frames.
        /// </summary>
        [TikProperty("group-key-update")]
        public string? GroupKeyUpdate { get; set; }

        // ── RADIUS (WinBox: the profile's "RADIUS" tab) ───────────────────────────────────────────
        //
        // WinBox drops the 'radius-' prefix the API spells out on this tab and renames three of the
        // fields outright ('MAC Caching Time', 'Called ID Format'). Noted per property below, because a
        // user who knows the GUI will look for the WinBox name.

        /// <summary>
        /// radius-mac-authentication — when true the AP asks a RADIUS server whether a client's MAC is
        /// allowed to connect, before any other authentication. Default: no.
        /// <para>WinBox: "MAC Authentication" (RADIUS tab).</para>
        /// </summary>
        [TikProperty("radius-mac-authentication", DefaultValue = "no")]
        public bool? RadiusMacAuthentication { get; set; }

        /// <summary>
        /// radius-mac-accounting — send RADIUS accounting requests for MAC-authenticated clients.
        /// Default: no.
        /// <para>WinBox: "MAC Accounting" (RADIUS tab).</para>
        /// </summary>
        [TikProperty("radius-mac-accounting", DefaultValue = "no")]
        public bool? RadiusMacAccounting { get; set; }

        /// <summary>
        /// radius-eap-accounting — send RADIUS accounting requests for EAP-authenticated clients.
        /// Default: no.
        /// <para>WinBox: "EAP Accounting" (RADIUS tab).</para>
        /// </summary>
        [TikProperty("radius-eap-accounting", DefaultValue = "no")]
        public bool? RadiusEapAccounting { get; set; }

        /// <summary>
        /// interim-update — how often an interim RADIUS accounting update is sent; <c>0s</c> disables it.
        /// Note this one field is NOT spelled with the <c>radius-</c> prefix, unlike its neighbours.
        /// Default: 0s.
        /// <para>
        /// The binary API, REST and WinBox-native report a duration (<c>0s</c>, <c>5m</c>); the CLI
        /// transports report the same value in RouterOS's clock form (<c>00:00:00</c>, <c>00:05:00</c>).
        /// Both are accepted on write.
        /// </para>
        /// <para>WinBox: "Interim Update" (RADIUS tab).</para>
        /// </summary>
        [TikProperty("interim-update", DefaultValue = "0s")]
        public TikDuration? InterimUpdate { get; set; }

        /// <summary>
        /// radius-mac-format — how a client's MAC address is written in the RADIUS request.
        /// Default: XX:XX:XX:XX:XX:XX.
        /// <para>
        /// Kept as a string rather than an enum because <b>the case is part of the value</b>: RouterOS
        /// accepts the same seven layouts twice, upper- and lower-cased, and the choice decides which the
        /// server receives. The fourteen accepted values are
        /// <c>XX:XX:XX:XX:XX:XX</c>, <c>XXXX:XXXX:XXXX</c>, <c>XXXXXX:XXXXXX</c>,
        /// <c>XX-XX-XX-XX-XX-XX</c>, <c>XXXXXX-XXXXXX</c>, <c>XXXXXXXXXXXX</c>,
        /// <c>XX XX XX XX XX XX</c> and the lowercase form of each.
        /// </para>
        /// <para>WinBox: "MAC Format" (RADIUS tab).</para>
        /// </summary>
        [TikProperty("radius-mac-format", DefaultValue = "XX:XX:XX:XX:XX:XX")]
        public string? RadiusMacFormat { get; set; }

        /// <summary>
        /// Values of <see cref="RadiusMacMode"/> — what the MAC address is sent AS.
        /// </summary>
        public enum MacModeType
        {
            /// <summary>as-username - the MAC is the RADIUS user name, with no password.</summary>
            [TikEnum("as-username")]
            AsUsername,

            /// <summary>as-username-and-password - the MAC is sent as both the user name and the password.</summary>
            [TikEnum("as-username-and-password")]
            AsUsernameAndPassword,
        }

        /// <summary>
        /// radius-mac-mode — whether the client's MAC is sent as the RADIUS user name alone or as user
        /// name and password. Default: as-username.
        /// <para>WinBox: "MAC Mode" (RADIUS tab).</para>
        /// </summary>
        /// <seealso cref="MacModeType"/>
        [TikProperty("radius-mac-mode", DefaultValue = "as-username")]
        public MacModeType? RadiusMacMode { get; set; }

        /// <summary>
        /// Values of <see cref="RadiusCalledFormat"/> — what goes into the RADIUS Called-Station-Id.
        /// </summary>
        public enum CalledFormatType
        {
            /// <summary>mac:ssid - the AP's MAC and the SSID, joined by a colon.</summary>
            [TikEnum("mac:ssid")]
            MacSsid,

            /// <summary>mac - the AP's MAC only.</summary>
            [TikEnum("mac")]
            Mac,

            /// <summary>ssid - the SSID only.</summary>
            [TikEnum("ssid")]
            Ssid,
        }

        /// <summary>
        /// radius-called-format — the format of the RADIUS Called-Station-Id attribute.
        /// Default: mac:ssid.
        /// <para>WinBox: "Called ID Format" (RADIUS tab).</para>
        /// </summary>
        /// <seealso cref="CalledFormatType"/>
        [TikProperty("radius-called-format", DefaultValue = "mac:ssid")]
        public CalledFormatType? RadiusCalledFormat { get; set; }

        /// <summary>
        /// radius-mac-caching — how long a successful RADIUS MAC authentication is cached, so a
        /// reconnecting client is not re-authenticated against the server. <c>disabled</c> turns caching
        /// off. Default: disabled.
        /// <para>
        /// A string rather than a time, because the disabling value is a word: it is either
        /// <c>disabled</c> or a time interval.
        /// </para>
        /// <para>WinBox: "MAC Caching Time" (RADIUS tab).</para>
        /// </summary>
        [TikProperty("radius-mac-caching", DefaultValue = "disabled")]
        public string? /*disabled | time*/ RadiusMacCaching { get; set; }

        // ── Static (WEP) keys (WinBox: the profile's "Static Keys" tab) ───────────────────────────
        //
        // These apply in the static-keys-optional / static-keys-required modes. RouterOS splits each key
        // into an ALGORITHM and the key itself; WinBox shows the pair in one box per key.
        //
        // The router validates the pair: a key must be 10 hex characters for 40bit-wep and 26 for
        // 104bit-wep, and setting an algorithm without a matching key is refused with "too short key".

        /// <summary>
        /// Values of the static-key algorithm properties — the cipher a WEP key slot uses.
        /// </summary>
        public enum StaticAlgoType
        {
            /// <summary>none - the key slot is unused.</summary>
            [TikEnum("none")]
            None,

            /// <summary>40bit-wep - 40-bit WEP; the key is 10 hex characters.</summary>
            [TikEnum("40bit-wep")]
            Wep40Bit,

            /// <summary>104bit-wep - 104-bit WEP; the key is 26 hex characters.</summary>
            [TikEnum("104bit-wep")]
            Wep104Bit,

            /// <summary>aes-ccm</summary>
            [TikEnum("aes-ccm")]
            AesCcm,

            /// <summary>tkip</summary>
            [TikEnum("tkip")]
            Tkip,
        }

        /// <summary>
        /// Values of <see cref="StaticTransmitKey"/> — which of the four static keys is used to encrypt
        /// frames this device sends.
        /// </summary>
        public enum TransmitKeyType
        {
            /// <summary>key-0</summary>
            [TikEnum("key-0")]
            Key0,

            /// <summary>key-1</summary>
            [TikEnum("key-1")]
            Key1,

            /// <summary>key-2</summary>
            [TikEnum("key-2")]
            Key2,

            /// <summary>key-3</summary>
            [TikEnum("key-3")]
            Key3,
        }

        /// <summary>
        /// static-algo-0 — the cipher of static key slot 0. Default: none.
        /// <para>WinBox: the left half of "Key 0" (Static Keys tab).</para>
        /// </summary>
        /// <seealso cref="StaticAlgoType"/>
        [TikProperty("static-algo-0", DefaultValue = "none")]
        public StaticAlgoType? StaticAlgo0 { get; set; }

        /// <summary>
        /// static-key-0 — static key slot 0, as hex characters (10 for 40bit-wep, 26 for 104bit-wep).
        /// <para>WinBox: the right half of "Key 0" (Static Keys tab).</para>
        /// <para>
        /// <b>Write-only over the CLI transports.</b> RouterOS's CLI omits secret fields from
        /// <c>print as-value</c> entirely (<c>detail</c> does not help), so over <c>Telnet</c>,
        /// <c>Ssh</c>, <c>MacTelnet</c>, <c>WinboxCli</c> and <c>WinboxCliMac</c> this reads back
        /// <c>null</c> — indistinguishable from a key the router holds empty. The binary API, REST and
        /// <c>WinboxNative</c> report it. Writing works on every transport.
        /// </para>
        /// </summary>
        [TikProperty("static-key-0")]
        public string? StaticKey0 { get; set; }

        /// <summary>
        /// static-algo-1 — the cipher of static key slot 1. Default: none.
        /// <para>WinBox: the left half of "Key 1" (Static Keys tab).</para>
        /// </summary>
        /// <seealso cref="StaticAlgoType"/>
        [TikProperty("static-algo-1", DefaultValue = "none")]
        public StaticAlgoType? StaticAlgo1 { get; set; }

        /// <summary>
        /// static-key-1 — static key slot 1, as hex characters. Write-only over the CLI transports; see
        /// <see cref="StaticKey0"/>.
        /// <para>WinBox: the right half of "Key 1" (Static Keys tab).</para>
        /// </summary>
        [TikProperty("static-key-1")]
        public string? StaticKey1 { get; set; }

        /// <summary>
        /// static-algo-2 — the cipher of static key slot 2. Default: none.
        /// <para>WinBox: the left half of "Key 2" (Static Keys tab).</para>
        /// </summary>
        /// <seealso cref="StaticAlgoType"/>
        [TikProperty("static-algo-2", DefaultValue = "none")]
        public StaticAlgoType? StaticAlgo2 { get; set; }

        /// <summary>
        /// static-key-2 — static key slot 2, as hex characters. Write-only over the CLI transports; see
        /// <see cref="StaticKey0"/>.
        /// <para>WinBox: the right half of "Key 2" (Static Keys tab).</para>
        /// </summary>
        [TikProperty("static-key-2")]
        public string? StaticKey2 { get; set; }

        /// <summary>
        /// static-algo-3 — the cipher of static key slot 3. Default: none.
        /// <para>WinBox: the left half of "Key 3" (Static Keys tab).</para>
        /// </summary>
        /// <seealso cref="StaticAlgoType"/>
        [TikProperty("static-algo-3", DefaultValue = "none")]
        public StaticAlgoType? StaticAlgo3 { get; set; }

        /// <summary>
        /// static-key-3 — static key slot 3, as hex characters. Write-only over the CLI transports; see
        /// <see cref="StaticKey0"/>.
        /// <para>WinBox: the right half of "Key 3" (Static Keys tab).</para>
        /// </summary>
        [TikProperty("static-key-3")]
        public string? StaticKey3 { get; set; }

        /// <summary>
        /// static-transmit-key — which of the four static keys encrypts the frames this device sends.
        /// Default: key-0.
        /// <para>WinBox: "Transmit Key" (Static Keys tab).</para>
        /// </summary>
        /// <seealso cref="TransmitKeyType"/>
        [TikProperty("static-transmit-key", DefaultValue = "key-0")]
        public TransmitKeyType? StaticTransmitKey { get; set; }

        /// <summary>
        /// static-sta-private-algo — the cipher of the station's private key, used for unicast frames
        /// between this device and one specific peer. Default: none.
        /// <para>WinBox: the left half of "St. Private Key" (Static Keys tab).</para>
        /// </summary>
        /// <seealso cref="StaticAlgoType"/>
        [TikProperty("static-sta-private-algo", DefaultValue = "none")]
        public StaticAlgoType? StaticStaPrivateAlgo { get; set; }

        /// <summary>
        /// static-sta-private-key — the station's private key. Write-only over the CLI transports; see
        /// <see cref="StaticKey0"/>.
        /// <para>WinBox: the right half of "St. Private Key" (Static Keys tab).</para>
        /// </summary>
        [TikProperty("static-sta-private-key")]
        public string? StaticStaPrivateKey { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string? ToString() => Name;
    }
}
