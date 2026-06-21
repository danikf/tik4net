using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/provisioning
    ///
    /// CAPsMAN provisioning rules (legacy CAPsMAN, RouterOS 6.x).  Provisioning rules form
    /// an ordered list that is matched top-down when a Controlled Access Point (CAP) radio
    /// joins the CAPsMAN controller and no master interface binding exists for that radio's
    /// MAC address.  The first matching rule determines what interface(s) are created for
    /// the radio and which configuration profile(s) are applied to them.
    ///
    /// A rule can match on any combination of: radio MAC address, router system identity
    /// (regexp), certificate common name (regexp), supported hardware modes, and IP address
    /// ranges.  An empty or omitted matcher field matches any value.
    ///
    /// The list is ordered — use Move() / MoveToEnd() to reorder rules.
    /// </summary>
    [TikEntity("/caps-man/provisioning", IncludeDetails = true, IsOrdered = true)]
    public class CapsManProvisioning
    {
        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>Action values for the <see cref="Action"/> property.</summary>
        /// <seealso cref="CapsManProvisioningAction"/>
        public enum CapsManProvisioningAction
        {
            /// <summary>
            /// none — do not create any interface for the matched radio (default).
            /// Use this to explicitly block a radio from being provisioned.
            /// </summary>
            [TikEnum("none")] None,

            /// <summary>
            /// create-disabled — create a static disabled master interface using the
            /// master-configuration profile.  The interface must be manually enabled.
            /// </summary>
            [TikEnum("create-disabled")] CreateDisabled,

            /// <summary>
            /// create-dynamic-enabled — create a dynamic (auto-removed on CAP disconnect)
            /// enabled master interface using the master-configuration profile.
            /// </summary>
            [TikEnum("create-dynamic-enabled")] CreateDynamicEnabled,

            /// <summary>
            /// create-enabled — create a static enabled master interface using the
            /// master-configuration profile.
            /// </summary>
            [TikEnum("create-enabled")] CreateEnabled,
        }

        // ── Name format ───────────────────────────────────────────────────────

        /// <summary>Name format values for the <see cref="NameFormat"/> property.</summary>
        /// <seealso cref="CapsManProvisioningNameFormat"/>
        public enum CapsManProvisioningNameFormat
        {
            /// <summary>
            /// cap — name the created interface "cap" followed by an index (default).
            /// </summary>
            [TikEnum("cap")] Cap,

            /// <summary>
            /// identity — use the CAP router's system identity as the interface name.
            /// </summary>
            [TikEnum("identity")] Identity,

            /// <summary>
            /// prefix — use the name-prefix value as the interface name base.
            /// </summary>
            [TikEnum("prefix")] Prefix,

            /// <summary>
            /// prefix-identity — combine name-prefix with the CAP router identity.
            /// </summary>
            [TikEnum("prefix-identity")] PrefixIdentity,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>
        /// action — determines the response when this rule matches a radio:
        /// none (default) — do not provision the radio;
        /// create-disabled — create a static disabled master interface;
        /// create-dynamic-enabled — create a dynamic enabled master interface (auto-removed when CAP disconnects);
        /// create-enabled — create a static enabled master interface.
        /// WinBox: "Action"
        /// <seealso cref="CapsManProvisioningAction"/>
        /// </summary>
        [TikProperty("action", DefaultValue = "none")]
        public CapsManProvisioningAction Action { get; set; }

        // ── Radio matchers ────────────────────────────────────────────────────

        /// <summary>
        /// radio-mac — matches only the radio whose MAC address equals this value.
        /// Set to "00:00:00:00:00:00" to match any radio.
        /// WinBox: "Radio MAC"
        /// </summary>
        [TikProperty("radio-mac", DefaultValue = "00:00:00:00:00:00")]
        public string/*MAC*/ RadioMac { get; set; }

        /// <summary>
        /// hw-supported-modes — comma-separated list of wireless hardware modes the radio
        /// must support for this rule to match.
        /// Values: a, a-turbo, ac, an, b, g, g-turbo, gn.
        /// Leave empty to match any hardware mode.
        /// WinBox: "Hw. Supported Modes"
        /// </summary>
        [TikProperty("hw-supported-modes", DefaultValue = "")]
        public string HwSupportedModes { get; set; }

        /// <summary>
        /// identity-regexp — regular expression matched against the CAP router's system
        /// identity (from /system/identity name).  Leave empty to match any identity.
        /// WinBox: "Identity Regexp"
        /// </summary>
        [TikProperty("identity-regexp", DefaultValue = "")]
        public string IdentityRegexp { get; set; }

        /// <summary>
        /// common-name-regexp — regular expression matched against the CAP certificate
        /// common name.  Leave empty to match any common name.
        /// WinBox: "Common Name Regexp"
        /// </summary>
        [TikProperty("common-name-regexp", DefaultValue = "")]
        public string CommonNameRegexp { get; set; }

        /// <summary>
        /// ip-address-ranges — comma-separated list of IP address ranges (up to 100) that
        /// the CAP's management IP must fall within.  Only effective for IP-joined CAPs;
        /// MAC-joined CAPs are never matched by this field.
        /// Leave empty to match any address.
        /// WinBox: "IP Address Ranges"
        /// </summary>
        [TikProperty("ip-address-ranges", DefaultValue = "")]
        public string IpAddressRanges { get; set; }

        // ── Configuration references ──────────────────────────────────────────

        /// <summary>
        /// master-configuration — name of the /caps-man/configuration profile applied to
        /// the master interface created for the matched radio.
        /// Required when action is create-disabled, create-enabled, or create-dynamic-enabled.
        /// WinBox: "Master Configuration"
        /// </summary>
        [TikProperty("master-configuration", DefaultValue = "")]
        public string MasterConfiguration { get; set; }

        /// <summary>
        /// slave-configurations — comma-separated list of /caps-man/configuration profile
        /// names applied to additional slave interfaces created for the matched radio.
        /// Leave empty for no slave interfaces.
        /// WinBox: "Slave Configurations"
        /// </summary>
        [TikProperty("slave-configurations", DefaultValue = "")]
        public string SlaveConfigurations { get; set; }

        // ── Naming ────────────────────────────────────────────────────────────

        /// <summary>
        /// name-format — syntax pattern used to build the master interface name.
        /// Default: cap (interface is named "cap" followed by an index).
        /// WinBox: "Name Format"
        /// <seealso cref="CapsManProvisioningNameFormat"/>
        /// </summary>
        [TikProperty("name-format", DefaultValue = "cap")]
        public CapsManProvisioningNameFormat NameFormat { get; set; }

        /// <summary>
        /// name-prefix — custom prefix used when name-format is "prefix" or "prefix-identity".
        /// Leave empty when not used.
        /// WinBox: "Name Prefix"
        /// </summary>
        [TikProperty("name-prefix", DefaultValue = "")]
        public string NamePrefix { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disabled — when true this provisioning rule is skipped during matching.
        /// Default: no (rule is active).
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description of this provisioning rule.
        /// WinBox: "Comment"
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity — action and comment.</summary>
        public override string ToString() =>
            string.IsNullOrEmpty(Comment)
                ? Action.ToString()
                : string.Format("{0} ({1})", Action, Comment);
    }
}
