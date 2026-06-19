namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/provisioning
    ///
    /// WiFi provisioning rules (ROS 7 wifi package / CAPsMAN v2).  Rules are matched
    /// top-down when a Controlled Access Point (CAP) radio joins the controller.
    /// The first matching rule determines what interface(s) are created for that radio
    /// and which configuration profile(s) are applied to them.
    ///
    /// A rule can match on any combination of: radio MAC address, system identity
    /// (regexp), certificate common name (regexp), supported frequency bands, and
    /// IP address range (IP-joined CAPs only).  An empty or omitted matcher field
    /// matches any value.
    ///
    /// The list is ordered — use Move() / MoveToEnd() to reorder rules.
    /// </summary>
    [TikEntity("/interface/wifi/provisioning", IncludeDetails = true, IsOrdered = true)]
    public class WifiProvisioning
    {
        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>Action values for the <see cref="Action"/> property.</summary>
        /// <seealso cref="WifiProvisioningAction"/>
        public enum WifiProvisioningAction
        {
            /// <summary>
            /// none — do not create any interface for the matched radio (default).
            /// Use this to explicitly block a radio from being provisioned.
            /// </summary>
            [TikEnum("none")] None,

            /// <summary>
            /// create-disabled — create a static disabled interface using the master
            /// configuration profile.  The interface must be manually enabled.
            /// </summary>
            [TikEnum("create-disabled")] CreateDisabled,

            /// <summary>
            /// create-dynamic-enabled — create a dynamic (auto-updated) enabled interface
            /// using the master configuration profile.  The interface is removed when the
            /// CAP disconnects.
            /// </summary>
            [TikEnum("create-dynamic-enabled")] CreateDynamicEnabled,

            /// <summary>
            /// create-enabled — create a static enabled interface using the master
            /// configuration profile.
            /// </summary>
            [TikEnum("create-enabled")] CreateEnabled,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>
        /// action — specifies what happens when this rule matches a radio:
        /// none (default) — do not provision the radio;
        /// create-disabled — create a static disabled master interface;
        /// create-dynamic-enabled — create a dynamic enabled master interface (auto-removed when CAP leaves);
        /// create-enabled — create a static enabled master interface.
        /// WinBox: "Action"
        /// <seealso cref="WifiProvisioningAction"/>
        /// </summary>
        [TikProperty("action", IsMandatory = true, DefaultValue = "none")]
        public WifiProvisioningAction Action { get; set; }

        // ── Radio matchers ────────────────────────────────────────────────────

        /// <summary>
        /// radio-mac — matches only the radio whose MAC address equals this value.
        /// Set to "00:00:00:00:00:00" or leave empty to match any radio.
        /// WinBox: "Radio MAC"
        /// </summary>
        [TikProperty("radio-mac", DefaultValue = "")]
        public string/*MAC*/ RadioMac { get; set; }

        /// <summary>
        /// identity-regexp — regular expression matched against the CAP router's system
        /// identity (from /system/identity name).  Leave empty to match any identity.
        /// WinBox: "Identity Regexp"
        /// </summary>
        [TikProperty("identity-regexp", DefaultValue = "")]
        public string IdentityRegexp { get; set; }

        /// <summary>
        /// common-name-regexp — regular expression matched against the CAP certificate
        /// common name found in /interface/wifi/radio.  Leave empty to match any CN.
        /// WinBox: "Common Name Regexp"
        /// </summary>
        [TikProperty("common-name-regexp", DefaultValue = "")]
        public string CommonNameRegexp { get; set; }

        /// <summary>
        /// supported-bands — one or more comma-separated frequency bands the radio must
        /// support for this rule to match (e.g. "2ghz-ax" or "5ghz-ac,5ghz-ax").
        /// Accepted values: 2ghz-g, 2ghz-n, 2ghz-ax, 2ghz-be, 5ghz-a, 5ghz-n, 5ghz-ac,
        /// 5ghz-ax, 5ghz-be, 6ghz-ax, 6ghz-be, s1ghz-ah.
        /// Leave empty to match any band combination.
        /// WinBox: "Supported Bands"
        /// </summary>
        [TikProperty("supported-bands", DefaultValue = "")]
        public string SupportedBands { get; set; }

        /// <summary>
        /// address-ranges — comma-separated list of IP address ranges (in
        /// "from-to" notation, e.g. "192.168.1.1-192.168.1.254") that the CAP's
        /// management IP must fall within.  Only effective for IP-joined CAPs;
        /// MAC-joined CAPs are never matched by this field.
        /// Leave empty to match any address.
        /// WinBox: "Address Ranges"
        /// </summary>
        [TikProperty("address-ranges", DefaultValue = "")]
        public string AddressRanges { get; set; }

        // ── Configuration references ──────────────────────────────────────────

        /// <summary>
        /// master-configuration — name of the /interface/wifi/configuration profile
        /// applied to the master (primary) interface created for the matched radio.
        /// Required when action is create-disabled, create-enabled, or
        /// create-dynamic-enabled.
        /// WinBox: "Master Configuration"
        /// </summary>
        [TikProperty("master-configuration", DefaultValue = "")]
        public string MasterConfiguration { get; set; }

        /// <summary>
        /// slave-configurations — comma-separated list of /interface/wifi/configuration
        /// profile names applied to additional virtual (slave) interfaces created for
        /// the matched radio.  Leave empty for no slave interfaces.
        /// WinBox: "Slave Configurations"
        /// </summary>
        [TikProperty("slave-configurations", DefaultValue = "")]
        public string SlaveConfigurations { get; set; }

        // ── Naming ────────────────────────────────────────────────────────────

        /// <summary>
        /// name-format — base string used to build the master interface name.
        /// Supported placeholders: %I (system identity), %C (certificate CN),
        /// %R (radio MAC upper-case, colons removed), %r (radio MAC lower-case).
        /// Example: "cap%I" → "capMyRouter".
        /// Leave empty to use the router default ("cap-wifi").
        /// WinBox: "Name Format"
        /// </summary>
        [TikProperty("name-format", DefaultValue = "")]
        public string NameFormat { get; set; }

        /// <summary>
        /// slave-name-format — base string used to build slave interface names.
        /// Supported placeholders: %v (virtual index), %m (master interface name),
        /// %I (system identity).
        /// Leave empty to use the router default.
        /// WinBox: "Slave Name Format"
        /// </summary>
        [TikProperty("slave-name-format", DefaultValue = "")]
        public string SlaveNameFormat { get; set; }

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
