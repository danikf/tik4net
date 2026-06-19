using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/access-list
    ///
    /// CAPsMAN access-list rules (legacy CAPsMAN, RouterOS 6.x).  Rules form an
    /// ordered list that is evaluated top-down for every wireless client that
    /// tries to associate with any CAP interface managed by this CAPsMAN controller.
    /// The first matching rule determines whether the client is accepted, rejected,
    /// or sent to a RADIUS server for authorisation, and what per-client overrides
    /// (VLAN, passphrase, TX limits, etc.) are applied.
    ///
    /// Matching criteria: MAC address (with optional mask), interface, SSID regexp,
    /// receive-signal-strength range, and time-of-day / days-of-week.
    ///
    /// The list is ordered — use Move() / MoveToEnd() to reorder rules.
    /// </summary>
    [TikEntity("/caps-man/access-list", IncludeDetails = true, IsOrdered = true)]
    public class CapsManAccessList
    {
        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>Action values for the <see cref="Action"/> property.</summary>
        /// <seealso cref="CapsManAccessListAction"/>
        public enum CapsManAccessListAction
        {
            /// <summary>
            /// accept — allow the client to associate (default).
            /// Per-client overrides in this rule (VLAN, passphrase, TX limits, …) are applied.
            /// </summary>
            [TikEnum("accept")] Accept,

            /// <summary>
            /// reject — deny the client; the client will not be allowed to associate.
            /// </summary>
            [TikEnum("reject")] Reject,

            /// <summary>
            /// query-radius — forward the authorisation decision to a RADIUS server.
            /// </summary>
            [TikEnum("query-radius")] QueryRadius,
        }

        // ── VLAN mode ─────────────────────────────────────────────────────────

        /// <summary>VLAN tagging mode values for the <see cref="VlanMode"/> property.</summary>
        /// <seealso cref="CapsManAccessListVlanMode"/>
        public enum CapsManAccessListVlanMode
        {
            /// <summary>
            /// no-tag — do not add any VLAN tag to frames from matched clients (default).
            /// </summary>
            [TikEnum("no-tag")] NoTag,

            /// <summary>
            /// use-service-tag — add an 802.1ad (QinQ) outer service VLAN tag using
            /// the <see cref="VlanId"/> value.
            /// </summary>
            [TikEnum("use-service-tag")] UseServiceTag,

            /// <summary>
            /// use-tag — add an 802.1Q VLAN tag using the <see cref="VlanId"/> value.
            /// </summary>
            [TikEnum("use-tag")] UseTag,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Action ────────────────────────────────────────────────────────────

        /// <summary>
        /// action — what to do when this rule matches a client:
        /// accept (default) — allow the client, applying any per-client overrides;
        /// reject — deny the client;
        /// query-radius — defer the decision to a RADIUS server.
        /// WinBox: "Action"
        /// <seealso cref="CapsManAccessListAction"/>
        /// </summary>
        [TikProperty("action", DefaultValue = "accept")]
        public CapsManAccessListAction Action { get; set; }

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
        /// interface — name of the specific CAP interface on which the rule is active.
        /// Leave empty to match all CAP interfaces.
        /// WinBox: "Interface"
        /// </summary>
        [TikProperty("interface", DefaultValue = "")]
        public string Interface { get; set; }

        /// <summary>
        /// ssid-regexp — regular expression matched against the SSID of the wireless
        /// network the client is connecting to.  Leave empty to match any SSID.
        /// WinBox: "SSID Regexp"
        /// </summary>
        [TikProperty("ssid-regexp", DefaultValue = "")]
        public string SsidRegexp { get; set; }

        // ── Signal / time matchers ────────────────────────────────────────────

        /// <summary>
        /// signal-range — allowed receive-signal-strength range in dBm, expressed
        /// as "min..max" (e.g. "-80..-40").  Clients whose signal falls outside this
        /// range are not matched.
        /// Default: "-120..120" (any signal).
        /// WinBox: "Signal Range"
        /// </summary>
        [TikProperty("signal-range", DefaultValue = "-120..120")]
        public string SignalRange { get; set; }

        /// <summary>
        /// allow-signal-out-of-range — how long a connected client is tolerated when
        /// its signal falls outside <see cref="SignalRange"/> before being disconnected.
        /// Special value "always" (default) means the signal constraint is only used for
        /// initial association matching, never for disconnection.
        /// WinBox: "Allow Signal Out Of Range"
        /// </summary>
        [TikProperty("allow-signal-out-of-range", DefaultValue = "always")]
        public string/*time*/ AllowSignalOutOfRange { get; set; }

        /// <summary>
        /// time — time-of-day and days-of-week range during which the rule is active,
        /// in the form "HH:MM:SS-HH:MM:SS,sun,mon,tue,wed,thu,fri,sat".
        /// Leave empty to match at any time.
        /// WinBox: "Time"
        /// </summary>
        [TikProperty("time", DefaultValue = "")]
        public string/*time*/ Time { get; set; }

        // ── Per-client overrides ──────────────────────────────────────────────

        /// <summary>
        /// vlan-mode — VLAN tagging mode applied to frames from matched clients.
        /// no-tag (default) — no VLAN tag;
        /// use-tag — add 802.1Q VLAN tag using <see cref="VlanId"/>;
        /// use-service-tag — add 802.1ad outer service VLAN tag using <see cref="VlanId"/>.
        /// WinBox: "VLAN Mode"
        /// <seealso cref="CapsManAccessListVlanMode"/>
        /// </summary>
        [TikProperty("vlan-mode", DefaultValue = "no-tag")]
        public CapsManAccessListVlanMode VlanMode { get; set; }

        /// <summary>
        /// vlan-id — 802.1Q or 802.1ad VLAN ID (1–4095) assigned to matched clients.
        /// Only meaningful when <see cref="VlanMode"/> is use-tag or use-service-tag.
        /// Leave at 0 (unset) when no VLAN override is needed.
        /// DefaultValue="0" makes the mapper omit this field on add when unset
        /// (sending 0 would be rejected by the router as out of range).
        /// WinBox: "VLAN ID"
        /// </summary>
        [TikProperty("vlan-id", DefaultValue = "0")]
        public int VlanId { get; set; }

        /// <summary>
        /// private-passphrase — per-client WPA PSK passphrase override.  When non-empty
        /// the client must use this passphrase instead of the interface default.
        /// Leave empty to use the interface passphrase (default).
        /// WinBox: "Private Passphrase"
        /// </summary>
        [TikProperty("private-passphrase", DefaultValue = "")]
        public string PrivatePassphrase { get; set; }

        /// <summary>
        /// radius-accounting — whether to send RADIUS accounting messages for matched
        /// clients.  Overrides the interface-level RADIUS accounting setting.
        /// Default: no (false).
        /// WinBox: "RADIUS Accounting"
        /// </summary>
        [TikProperty("radius-accounting", DefaultValue = "false")]
        public bool RadiusAccounting { get; set; }

        /// <summary>
        /// client-to-client-forwarding — allow matched wireless clients to communicate
        /// with each other directly through the CAP (layer-2 forwarding between clients).
        /// Default: no (false).
        /// WinBox: "Client To Client Forwarding"
        /// </summary>
        [TikProperty("client-to-client-forwarding", DefaultValue = "false")]
        public bool ClientToClientForwarding { get; set; }

        /// <summary>
        /// ap-tx-limit — maximum TX data rate (bytes/s) from the AP toward matched clients.
        /// 0 (default) means unlimited.
        /// DefaultValue="0" makes the mapper omit this field on add when unset.
        /// WinBox: "AP TX Limit"
        /// </summary>
        [TikProperty("ap-tx-limit", DefaultValue = "0")]
        public int ApTxLimit { get; set; }

        /// <summary>
        /// client-tx-limit — maximum TX data rate (bytes/s) from matched clients toward the AP.
        /// Only effective for RouterOS-based wireless clients.
        /// 0 (default) means unlimited.
        /// DefaultValue="0" makes the mapper omit this field on add when unset.
        /// WinBox: "Client TX Limit"
        /// </summary>
        [TikProperty("client-tx-limit", DefaultValue = "0")]
        public int ClientTxLimit { get; set; }

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
