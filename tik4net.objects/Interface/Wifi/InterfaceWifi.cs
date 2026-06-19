namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi
    ///
    /// RouterOS WiFi interface (Wi-Fi 5 / Wi-Fi 6 / Wi-Fi 7), introduced in ROS 7.13.
    /// Requires compatible radios with the 'wifi-qcom-ac' (802.11ac) or 'wifi-qcom' (802.11ax+)
    /// driver packages.  Each physical radio appears as a master interface; virtual BSSIDs are
    /// added as slave interfaces bound to a master via <see cref="MasterInterface"/>.
    ///
    /// Configuration is either inlined on the interface or delegated to shared profile objects
    /// (/interface/wifi/configuration, /security, /datapath, /channel) referenced by name.
    /// The dotted 'aaa.*' fields are always inline per-interface AAA overrides.
    /// </summary>
    [TikEntity("/interface/wifi", IncludeDetails = true)]
    public class InterfaceWifi
    {
        // ── ARP mode ──────────────────────────────────────────────────────────

        /// <summary>ARP mode values for the <see cref="Arp"/> property.</summary>
        /// <seealso cref="Arp"/>
        public enum ArpMode
        {
            /// <summary>enabled — the interface uses ARP (default).</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — the interface will not use ARP.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>proxy-arp — the interface acts as an ARP proxy.</summary>
            [TikEnum("proxy-arp")] ProxyArp,
            /// <summary>reply-only — only replies to static ARP entries; no dynamic learning.</summary>
            [TikEnum("reply-only")] ReplyOnly,
            /// <summary>local-proxy-arp — proxy ARP only between clients on the same interface.</summary>
            [TikEnum("local-proxy-arp")] LocalProxyArp,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — Interface name.
        /// Default: wifiN (automatically assigned).
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Hardware binding ──────────────────────────────────────────────────

        /// <summary>
        /// radio-mac — MAC address of the radio to bind this interface to (identifies the
        /// physical radio for the master interface).  Specify either this OR
        /// <see cref="MasterInterface"/> when creating an interface, not both.
        /// WinBox: "Radio MAC"
        /// </summary>
        [TikProperty("radio-mac")]
        public string/*MAC*/ RadioMac { get; set; }

        /// <summary>
        /// master-interface — name of the master (physical) wifi interface.  Set when creating
        /// a virtual BSSID (slave interface) instead of a physical one.
        /// WinBox: "Master Interface"
        /// </summary>
        [TikProperty("master-interface")]
        public string MasterInterface { get; set; }

        // ── Profile references ────────────────────────────────────────────────
        // These fields can hold either the name of a shared profile object or "none"/"default".
        // Inline overrides for each sub-profile are available via the dotted fields on the router
        // (e.g. channel....) but are not mapped here — use the dedicated profile entities instead.

        /// <summary>
        /// configuration — name of the /interface/wifi/configuration profile to apply.
        /// Leave empty to use inline configuration or router defaults.
        /// WinBox: "Configuration"
        /// </summary>
        [TikProperty("configuration")]
        public string Configuration { get; set; }

        /// <summary>
        /// security — name of the /interface/wifi/security profile to apply.
        /// Leave empty to use inline security settings or open authentication.
        /// WinBox: "Security"
        /// </summary>
        [TikProperty("security")]
        public string Security { get; set; }

        /// <summary>
        /// datapath — name of the /interface/wifi/datapath profile to apply.
        /// Controls bridging, VLAN, and traffic-processing settings.
        /// WinBox: "Datapath"
        /// </summary>
        [TikProperty("datapath")]
        public string Datapath { get; set; }

        /// <summary>
        /// channel — name of the /interface/wifi/channel profile to apply.
        /// Controls frequency band, width and channel selection.
        /// WinBox: "Channel"
        /// </summary>
        [TikProperty("channel")]
        public string Channel { get; set; }

        /// <summary>
        /// interworking — name of the /interface/wifi/interworking (Hotspot 2.0) profile.
        /// WinBox: "Interworking"
        /// </summary>
        [TikProperty("interworking")]
        public string Interworking { get; set; }

        /// <summary>
        /// steering — name of the /interface/wifi/steering profile for 802.11k/v roaming hints.
        /// WinBox: "Steering"
        /// </summary>
        [TikProperty("steering")]
        public string Steering { get; set; }

        // ── Inline AAA overrides ──────────────────────────────────────────────
        // aaa.* are per-interface RADIUS/accounting overrides.  The whole group can also
        // reference a named AAA profile via the 'aaa' field.

        /// <summary>
        /// aaa — name of the AAA profile, OR inline override root; corresponds to the
        /// /interface/wifi aaa field which selects a named RADIUS profile.
        /// </summary>
        [TikProperty("aaa")]
        public string Aaa { get; set; }

        /// <summary>
        /// aaa.called-format — format string for RADIUS Called-Station-Id attribute.
        /// Common values: "mac", "mac:ssid", "ssid".
        /// WinBox: "Called Format"
        /// </summary>
        [TikProperty("aaa.called-format")]
        public string AaaCalledFormat { get; set; }

        /// <summary>
        /// aaa.calling-format — format string for RADIUS Calling-Station-Id attribute.
        /// WinBox: "Calling Format"
        /// </summary>
        [TikProperty("aaa.calling-format")]
        public string AaaCallingFormat { get; set; }

        /// <summary>
        /// aaa.interim-update — interval for RADIUS interim accounting updates; "disabled" to turn off.
        /// WinBox: "Interim Update"
        /// </summary>
        [TikProperty("aaa.interim-update")]
        public string/*time|disabled*/ AaaInterimUpdate { get; set; }

        /// <summary>
        /// aaa.mac-caching — time to cache RADIUS MAC-auth results; "disabled" to skip caching.
        /// WinBox: "MAC Caching"
        /// </summary>
        [TikProperty("aaa.mac-caching")]
        public string/*time|disabled*/ AaaMacCaching { get; set; }

        /// <summary>
        /// aaa.nas-identifier — value sent in RADIUS NAS-Identifier attribute.
        /// WinBox: "NAS Identifier"
        /// </summary>
        [TikProperty("aaa.nas-identifier")]
        public string AaaNasIdentifier { get; set; }

        /// <summary>
        /// aaa.password-format — format of the RADIUS password field used during MAC authentication.
        /// WinBox: "Password Format"
        /// </summary>
        [TikProperty("aaa.password-format")]
        public string AaaPasswordFormat { get; set; }

        /// <summary>
        /// aaa.username-format — format of the RADIUS User-Name attribute during MAC authentication.
        /// WinBox: "Username Format"
        /// </summary>
        [TikProperty("aaa.username-format")]
        public string AaaUsernameFormat { get; set; }

        // ── Interface / network settings ──────────────────────────────────────

        /// <summary>
        /// arp — Address Resolution Protocol mode.
        /// Default: enabled.
        /// <seealso cref="ArpMode"/>
        /// </summary>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>
        /// arp-timeout — time an ARP entry is kept in the table; "auto" to use the system default.
        /// Default: auto
        /// WinBox: "ARP Timeout"
        /// </summary>
        [TikProperty("arp-timeout", DefaultValue = "auto")]
        public string/*time|auto*/ ArpTimeout { get; set; }

        /// <summary>
        /// mac-address — override the interface MAC address (BSSID).
        /// Leave empty to use the radio's default MAC.
        /// WinBox: "MAC Address"
        /// </summary>
        [TikProperty("mac-address")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// mtu — Layer-3 maximum transmission unit in bytes. Range: 32..2290.
        /// Default: 1500
        /// WinBox: "MTU"
        /// </summary>
        [TikProperty("mtu", DefaultValue = "1500")]
        public int Mtu { get; set; }

        /// <summary>
        /// l2mtu — Layer-2 maximum transmission unit in bytes. Range: 32..2290.
        /// Default: 2290
        /// WinBox: "L2 MTU"
        /// </summary>
        [TikProperty("l2mtu", DefaultValue = "2290")]
        public int L2Mtu { get; set; }

        /// <summary>
        /// disable-running-check — when true, the interface is always reported as running
        /// even when no client is associated (useful for monitoring).
        /// Default: no
        /// WinBox: "Disable Running Check"
        /// </summary>
        [TikProperty("disable-running-check", DefaultValue = "no")]
        public bool DisableRunningCheck { get; set; }

        /// <summary>
        /// disabled — when true the interface is administratively disabled.
        /// Default: yes (new interfaces are created disabled).
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description.
        /// WinBox: "Comment"
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        // ── Read-only properties ──────────────────────────────────────────────

        /// <summary>
        /// default-name — factory/default name of the interface (e.g. "wifi1").
        /// </summary>
        [TikProperty("default-name", IsReadOnly = true)]
        public string DefaultName { get; private set; }

        /// <summary>
        /// running — true when the interface has an active link (unless disable-running-check is set).
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// bound — true when the interface is operational (bound to its radio and active).
        /// </summary>
        [TikProperty("bound", IsReadOnly = true)]
        public bool Bound { get; private set; }

        /// <summary>
        /// inactive — false when the interface is fully configured and operational.
        /// </summary>
        [TikProperty("inactive", IsReadOnly = true)]
        public bool Inactive { get; private set; }

        /// <summary>
        /// master — true for physical (radio-backed) interfaces; false for virtual BSSIDs.
        /// </summary>
        [TikProperty("master", IsReadOnly = true)]
        public bool Master { get; private set; }

        /// <summary>
        /// cap — CAPsMAN controller info if this interface is controlled by a CAPsMAN manager.
        /// </summary>
        [TikProperty("cap", IsReadOnly = true)]
        public string Cap { get; private set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
