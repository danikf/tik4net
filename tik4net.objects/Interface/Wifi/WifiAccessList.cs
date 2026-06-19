namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/access-list
    ///
    /// WiFi access-list rules (ROS 7 wifi package).  Rules are evaluated top-down
    /// for every client that tries to associate with a managed WiFi interface.
    /// The first matching rule determines whether the client is accepted, rejected,
    /// or sent to RADIUS for authorisation, and what per-client overrides (VLAN,
    /// passphrase, signal policy, etc.) are applied.
    ///
    /// Matching criteria: MAC address (with optional mask), interface, SSID regexp,
    /// signal-strength range, time-of-day / days-of-week.
    ///
    /// The list is ordered — use Move() / MoveToEnd() to reorder rules.
    /// </summary>
    [TikEntity("/interface/wifi/access-list", IncludeDetails = true, IsOrdered = true)]
    public class WifiAccessList
    {
        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>Action values for the <see cref="Action"/> property.</summary>
        /// <seealso cref="WifiAccessListAction"/>
        public enum WifiAccessListAction
        {
            /// <summary>
            /// accept — allow the client to associate (default).
            /// Per-client overrides in this rule (VLAN, passphrase, …) are applied.
            /// </summary>
            [TikEnum("accept")] Accept,

            /// <summary>
            /// reject — deny the client and send a deauthentication frame.
            /// </summary>
            [TikEnum("reject")] Reject,

            /// <summary>
            /// query-radius — forward the authorisation decision to a RADIUS server.
            /// </summary>
            [TikEnum("query-radius")] QueryRadius,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>
        /// action — what to do when this rule matches a client:
        /// accept (default) — allow the client, applying any per-client overrides;
        /// reject — send a deauthentication frame;
        /// query-radius — defer the decision to a RADIUS server.
        /// WinBox: "Action"
        /// <seealso cref="WifiAccessListAction"/>
        /// </summary>
        [TikProperty("action", DefaultValue = "accept")]
        public WifiAccessListAction Action { get; set; }

        // ── MAC matchers ──────────────────────────────────────────────────────

        /// <summary>
        /// mac-address — match clients whose MAC address, after bitwise AND with
        /// <see cref="MacAddressMask"/>, equals this value.
        /// Set to "00:00:00:00:00:00" to match any client (default).
        /// WinBox: "MAC Address"
        /// </summary>
        [TikProperty("mac-address", DefaultValue = "00:00:00:00:00:00")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// mac-address-mask — bitmask applied to the client MAC before comparison
        /// with <see cref="MacAddress"/>.  "FF:FF:FF:FF:FF:FF" (default) means an
        /// exact match; shorter masks match entire vendor OUIs or subnets.
        /// WinBox: "MAC Address Mask"
        /// </summary>
        [TikProperty("mac-address-mask", DefaultValue = "FF:FF:FF:FF:FF:FF")]
        public string/*MAC*/ MacAddressMask { get; set; }

        // ── Interface / SSID matchers ─────────────────────────────────────────

        /// <summary>
        /// interface — name of the specific WiFi interface (or interface list) on
        /// which the rule is active.  Leave empty / "any" to match all interfaces.
        /// WinBox: "Interface"
        /// </summary>
        [TikProperty("interface", DefaultValue = "")]
        public string Interface { get; set; }

        /// <summary>
        /// ssid-regexp — regular expression matched against the SSID of the WiFi
        /// network the client is connecting to.  Leave empty to match any SSID.
        /// WinBox: "SSID Regexp"
        /// </summary>
        [TikProperty("ssid-regexp", DefaultValue = "")]
        public string SsidRegexp { get; set; }

        // ── Signal / time matchers ────────────────────────────────────────────

        /// <summary>
        /// signal-range — allowed receive-signal-strength range in dBm, expressed
        /// as "min..max" (e.g. "-80..-40").  Clients whose signal falls outside this
        /// range are not matched (or are disconnected, see
        /// <see cref="AllowSignalOutOfRange"/>).
        /// Default: "-120..120" (any signal).
        /// WinBox: "Signal Range"
        /// </summary>
        [TikProperty("signal-range", DefaultValue = "-120..120")]
        public string SignalRange { get; set; }

        /// <summary>
        /// allow-signal-out-of-range — how long a connected client is tolerated
        /// when its signal falls outside <see cref="SignalRange"/> before being
        /// disconnected.  Special value "always" (stored when set to 0s) means the
        /// signal constraint is only used for initial association matching, never
        /// for disconnection.
        /// WinBox: "Allow Signal Out Of Range"
        /// </summary>
        [TikProperty("allow-signal-out-of-range", DefaultValue = "always")]
        public string/*time*/ AllowSignalOutOfRange { get; set; }

        /// <summary>
        /// time — time-of-day range during which the rule is active, in the form
        /// "HH:MM:SS-HH:MM:SS" (e.g. "08:00:00-18:00:00").  Use together with
        /// <see cref="Days"/> to restrict access to business hours.
        /// Leave empty to match at any time.
        /// WinBox: "Time"
        /// </summary>
        [TikProperty("time", DefaultValue = "")]
        public string/*time*/ Time { get; set; }

        /// <summary>
        /// days — comma-separated list of day abbreviations on which the rule is
        /// active: sun, mon, tue, wed, thu, fri, sat.
        /// Leave empty to match every day.
        /// WinBox: "Days"
        /// </summary>
        [TikProperty("days", DefaultValue = "")]
        public string Days { get; set; }

        // ── Per-client overrides ──────────────────────────────────────────────

        /// <summary>
        /// vlan-id — assign this 802.1Q VLAN ID (1–4095) to matched clients.
        /// Leave at 0 (unset) to inherit the interface VLAN configuration.
        /// Valid range: 1..4095.
        /// DefaultValue="0" makes the mapper omit this field on add when unset
        /// (sending 0 would be rejected by the router as out of range).
        /// WinBox: "VLAN ID"
        /// </summary>
        [TikProperty("vlan-id", DefaultValue = "0")]
        public int VlanId { get; set; }

        /// <summary>
        /// passphrase — per-client WPA passphrase override.  When non-empty the
        /// client must use this passphrase instead of the interface default.
        /// Leave empty to use the interface passphrase (default).
        /// WinBox: "Passphrase"
        /// </summary>
        [TikProperty("passphrase", DefaultValue = "")]
        public string Passphrase { get; set; }

        /// <summary>
        /// multi-passphrase-group — name of the multi-passphrase group to use for
        /// this client.  Leave empty for no group override.
        /// WinBox: "Multi Passphrase Group"
        /// </summary>
        [TikProperty("multi-passphrase-group", DefaultValue = "")]
        public string MultiPassphraseGroup { get; set; }

        /// <summary>
        /// radius-accounting — whether to send RADIUS accounting messages for matched
        /// clients.  Overrides the interface-level RADIUS accounting setting.
        /// Default: no (false).
        /// WinBox: "RADIUS Accounting"
        /// </summary>
        [TikProperty("radius-accounting", DefaultValue = "false")]
        public bool RadiusAccounting { get; set; }

        /// <summary>
        /// client-isolation — prevent matched clients from communicating with each
        /// other on the same interface (layer-2 isolation).
        /// Default: no (false).
        /// WinBox: "Client Isolation"
        /// </summary>
        [TikProperty("client-isolation", DefaultValue = "false")]
        public bool ClientIsolation { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disabled — when true this access-list rule is skipped during matching.
        /// Default: no (rule is active).
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled", DefaultValue = "false")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description of this access-list rule.
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
