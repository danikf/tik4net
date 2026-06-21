using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Snmp
{
    /// <summary>
    /// /snmp/community — SNMP community access control list.
    /// Controls which hosts may query the router via SNMP and what authentication /
    /// encryption is used (SNMPv3). SNMPv1/v2c offer only a clear-text community string
    /// and optional source-address filtering; use SNMPv3 (security != none) for
    /// production environments.
    /// </summary>
    [TikEntity("/snmp/community", IncludeDetails = true)]
    public class SnmpCommunity
    {
        // ------------------------------------------------------------------ enums

        /// <summary>SNMPv3 security level for this community.</summary>
        public enum SecurityLevel
        {
            /// <summary>noAuthNoPriv — no authentication, no encryption (SNMPv1/v2c style)</summary>
            [TikEnum("none")] None,

            /// <summary>authNoPriv — authentication required, no encryption</summary>
            [TikEnum("authorized")] Authorized,

            /// <summary>authPriv — both authentication and encryption required</summary>
            [TikEnum("private")] Private,
        }

        /// <summary>SNMPv3 authentication hash algorithm.</summary>
        public enum AuthProtocol
        {
            /// <summary>HMAC-MD5 authentication (default)</summary>
            [TikEnum("MD5")] MD5,

            /// <summary>HMAC-SHA1 authentication</summary>
            [TikEnum("SHA1")] SHA1,
        }

        /// <summary>SNMPv3 encryption cipher.</summary>
        public enum EncryptProtocol
        {
            /// <summary>DES (56-bit) encryption (default)</summary>
            [TikEnum("DES")] DES,

            /// <summary>AES (128-bit) encryption (available since RouterOS v6.16)</summary>
            [TikEnum("AES")] AES,
        }

        // ------------------------------------------------------------------ properties

        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — community string identifier sent by the SNMP manager.
        /// This is the "username" equivalent for SNMPv1/v2c.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// addresses — IP/IPv6 address prefix(es) that are permitted to query
        /// this community. Default: ::/0 (all hosts). Separate multiple with commas.
        /// WinBox: "Addresses"
        /// </summary>
        [TikProperty("addresses", DefaultValue = "::/0")]
        public string Addresses { get; set; }

        /// <summary>
        /// security — SNMPv3 security level (noAuthNoPriv / authNoPriv / authPriv).
        /// Use <see cref="SecurityLevel.None"/> for SNMPv1/v2c (no auth/encryption).
        /// </summary>
        /// <seealso cref="SecurityLevel"/>
        [TikProperty("security", DefaultValue = "none")]
        public SecurityLevel Security { get; set; }

        /// <summary>
        /// read-access — allow SNMP GET / WALK queries from this community.
        /// Default: yes (true).
        /// WinBox: "Read Access"
        /// </summary>
        [TikProperty("read-access", DefaultValue = "yes")]
        public bool ReadAccess { get; set; }

        /// <summary>
        /// write-access — allow SNMP SET (configuration write) from this community.
        /// Default: no (false). Enable only when required — write access is a security risk.
        /// WinBox: "Write Access"
        /// </summary>
        [TikProperty("write-access", DefaultValue = "no")]
        public bool WriteAccess { get; set; }

        /// <summary>
        /// authentication-protocol — hash algorithm used for SNMPv3 authentication.
        /// Only relevant when <see cref="Security"/> != <see cref="SecurityLevel.None"/>.
        /// Default: MD5.
        /// </summary>
        /// <seealso cref="AuthProtocol"/>
        [TikProperty("authentication-protocol", DefaultValue = "MD5")]
        public AuthProtocol AuthenticationProtocol { get; set; }

        /// <summary>
        /// authentication-password — passphrase for SNMPv3 authentication (min. 8 chars).
        /// Only used when <see cref="Security"/> is <see cref="SecurityLevel.Authorized"/>
        /// or <see cref="SecurityLevel.Private"/>.
        /// </summary>
        [TikProperty("authentication-password", DefaultValue = "")]
        public string AuthenticationPassword { get; set; }

        /// <summary>
        /// encryption-protocol — cipher used for SNMPv3 privacy/encryption.
        /// Only relevant when <see cref="Security"/> == <see cref="SecurityLevel.Private"/>.
        /// AES available since RouterOS v6.16. Default: DES.
        /// </summary>
        /// <seealso cref="EncryptProtocol"/>
        [TikProperty("encryption-protocol", DefaultValue = "DES")]
        public EncryptProtocol EncryptionProtocol { get; set; }

        /// <summary>
        /// encryption-password — passphrase for SNMPv3 encryption (min. 8 chars).
        /// Only used when <see cref="Security"/> == <see cref="SecurityLevel.Private"/>.
        /// </summary>
        [TikProperty("encryption-password", DefaultValue = "")]
        public string EncryptionPassword { get; set; }

        /// <summary>
        /// default — marks this community as the factory default ("public") entry.
        /// Read-only; set by RouterOS itself.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public bool Default { get; private set; }

        /// <summary>
        /// disabled — when true the community is inactive and will not be matched
        /// against incoming SNMP requests.
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — free-text annotation</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
