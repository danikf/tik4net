namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/configuration
    ///
    /// WiFi configuration profile (ROS 7 wifi package).  A configuration profile is a reusable
    /// preset that can be assigned to one or more /interface/wifi entries via the
    /// <c>configuration</c> field, avoiding repetition of the same SSID / country / mode settings
    /// across multiple interfaces.
    ///
    /// The profile can reference other shared sub-profile objects (channel, security, datapath,
    /// interworking, steering, aaa) by name.  Dotted inline overrides for each sub-profile
    /// (e.g. channel...., security....) are available on the router but are not mapped here —
    /// use the dedicated profile entities instead.
    /// </summary>
    [TikEntity("/interface/wifi/configuration", IncludeDetails = true)]
    public class WifiConfiguration
    {
        // ── Operating mode ────────────────────────────────────────────────────

        /// <summary>Operating mode values for the <see cref="Mode"/> property.</summary>
        /// <seealso cref="Mode"/>
        public enum OperatingMode
        {
            /// <summary>ap — access point mode (default).</summary>
            [TikEnum("ap")] Ap,
            /// <summary>station — client / station mode.</summary>
            [TikEnum("station")] Station,
            /// <summary>station-bridge — client mode with transparent bridging.</summary>
            [TikEnum("station-bridge")] StationBridge,
            /// <summary>station-pseudobridge — client mode with pseudobridge (address substitution).</summary>
            [TikEnum("station-pseudobridge")] StationPseudobridge,
        }

        // ── Installation type ─────────────────────────────────────────────────

        /// <summary>Installation environment values for the <see cref="Installation"/> property.</summary>
        /// <seealso cref="Installation"/>
        public enum InstallationType
        {
            /// <summary>indoor — indoor installation; applies indoor power/channel limits (default).</summary>
            [TikEnum("indoor")] Indoor,
            /// <summary>outdoor — outdoor installation; applies outdoor power/channel limits.</summary>
            [TikEnum("outdoor")] Outdoor,
        }

        // ── Manager ───────────────────────────────────────────────────────────

        /// <summary>Configuration manager values for the <see cref="Manager"/> property.</summary>
        /// <seealso cref="Manager"/>
        public enum ManagerType
        {
            /// <summary>local — configuration is managed locally on the router (default).</summary>
            [TikEnum("local")] Local,
            /// <summary>capsman — configuration is managed by a CAPsMAN controller; local config is ignored.</summary>
            [TikEnum("capsman")] Capsman,
            /// <summary>capsman-or-local — use CAPsMAN when available, fall back to local.</summary>
            [TikEnum("capsman-or-local")] CapsmanOrLocal,
        }

        // ── Multicast enhance ─────────────────────────────────────────────────

        /// <summary>Multicast-to-unicast conversion values for the <see cref="MulticastEnhance"/> property.</summary>
        /// <seealso cref="MulticastEnhance"/>
        public enum MulticastEnhanceMode
        {
            /// <summary>disabled — no multicast-to-unicast conversion (default).</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>enabled — convert multicast frames to unicast transmissions per-client.</summary>
            [TikEnum("enabled")] Enabled,
        }

        // ── QoS classifier ────────────────────────────────────────────────────

        /// <summary>WMM QoS classification mode values for the <see cref="QosClassifier"/> property.</summary>
        /// <seealso cref="QosClassifier"/>
        public enum QosClassifierMode
        {
            /// <summary>priority — classify by 802.1p/VLAN priority bits (default).</summary>
            [TikEnum("priority")] Priority,
            /// <summary>dscp-high-3-bits — classify by the three highest DSCP bits.</summary>
            [TikEnum("dscp-high-3-bits")] DscpHigh3Bits,
        }

        // ── HW protection mode ────────────────────────────────────────────────

        /// <summary>Hardware frame-collision protection mode values for the <see cref="HwProtectionMode"/> property.</summary>
        /// <seealso cref="HwProtectionMode"/>
        public enum HwProtectionModeType
        {
            /// <summary>rts-cts — use RTS/CTS handshake before transmitting (default).</summary>
            [TikEnum("rts-cts")] RtsCts,
            /// <summary>cts-to-self — send CTS-to-self frame to reserve the medium.</summary>
            [TikEnum("cts-to-self")] CtsToSelf,
            /// <summary>none — no hardware protection (disables RTS/CTS and CTS-to-self).</summary>
            [TikEnum("none")] None,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this configuration profile.
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Network identity ──────────────────────────────────────────────────

        /// <summary>
        /// ssid — the wireless network name (ESSID) broadcast in beacon frames.
        /// WinBox: "SSID"
        /// </summary>
        [TikProperty("ssid")]
        public string Ssid { get; set; }

        /// <summary>
        /// mode — interface operating mode.
        /// Default: ap
        /// <seealso cref="OperatingMode"/>
        /// WinBox: "Mode"
        /// </summary>
        [TikProperty("mode", DefaultValue = "ap")]
        public OperatingMode Mode { get; set; }

        /// <summary>
        /// country — regulatory domain that determines allowed channels and maximum transmit power.
        /// Default: Latvia
        /// WinBox: "Country"
        /// </summary>
        [TikProperty("country", DefaultValue = "Latvia")]
        public string Country { get; set; }

        // ── Profile references ────────────────────────────────────────────────
        // These string fields hold the name of a shared sub-profile object (or empty for "none").
        // Dotted inline overrides (channel...., security...., etc.) are not mapped here.

        /// <summary>
        /// channel — name of the /interface/wifi/channel profile to apply.
        /// WinBox: "Channel"
        /// </summary>
        [TikProperty("channel")]
        public string Channel { get; set; }

        /// <summary>
        /// security — name of the /interface/wifi/security profile to apply.
        /// WinBox: "Security"
        /// </summary>
        [TikProperty("security")]
        public string Security { get; set; }

        /// <summary>
        /// datapath — name of the /interface/wifi/datapath profile to apply.
        /// WinBox: "Datapath"
        /// </summary>
        [TikProperty("datapath")]
        public string Datapath { get; set; }

        /// <summary>
        /// interworking — name of the /interface/wifi/interworking (Hotspot 2.0) profile.
        /// WinBox: "Interworking"
        /// </summary>
        [TikProperty("interworking")]
        public string Interworking { get; set; }

        /// <summary>
        /// steering — name of the /interface/wifi/steering profile for 802.11k/v band-steering.
        /// WinBox: "Steering"
        /// </summary>
        [TikProperty("steering")]
        public string Steering { get; set; }

        // ── AAA (RADIUS) settings ─────────────────────────────────────────────

        /// <summary>
        /// aaa — name of the /interface/wifi/aaa profile for RADIUS authentication/accounting,
        /// or inline override root.
        /// WinBox: "AAA"
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
        /// aaa.mac-caching — how long to cache RADIUS MAC-auth results; "disabled" to skip caching.
        /// WinBox: "MAC Caching"
        /// </summary>
        [TikProperty("aaa.mac-caching")]
        public string/*time|disabled*/ AaaMacCaching { get; set; }

        /// <summary>
        /// aaa.nas-identifier — value sent in the RADIUS NAS-Identifier attribute.
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

        // ── Radio / PHY settings ──────────────────────────────────────────────

        /// <summary>
        /// chains — receive radio chains to use (comma-separated chain indices, e.g. "0" or "0,1").
        /// Default: all chains.
        /// WinBox: "Chains"
        /// </summary>
        [TikProperty("chains")]
        public string/*chain-list*/ Chains { get; set; }

        /// <summary>
        /// tx-chains — transmit radio chains to use (comma-separated chain indices).
        /// Default: all chains.
        /// WinBox: "TX Chains"
        /// </summary>
        [TikProperty("tx-chains")]
        public string/*chain-list*/ TxChains { get; set; }

        /// <summary>
        /// tx-power — transmit power override in dBm (1..40). Set to 0 here to let the router
        /// use the radio's default power (field is not sent on add when 0).
        /// WinBox: "TX Power"
        /// </summary>
        [TikProperty("tx-power", DefaultValue = "0")]
        public int TxPower { get; set; }

        /// <summary>
        /// antenna-gain — override the default antenna gain in dBi (0..30).
        /// WinBox: "Antenna Gain"
        /// </summary>
        [TikProperty("antenna-gain", DefaultValue = "0")]
        public int AntennaGain { get; set; }

        // ── Beacon / client settings ──────────────────────────────────────────

        /// <summary>
        /// beacon-interval — interval between beacon frames (100ms..1s), e.g. "200ms".
        /// Default: 100ms.
        /// WinBox: "Beacon Interval"
        /// </summary>
        [TikProperty("beacon-interval", DefaultValue = "100ms")]
        public string/*time*/ BeaconInterval { get; set; }

        /// <summary>
        /// dtim-period — number of beacon intervals between DTIM frames (1..255).
        /// Clients in power-save mode wake at each DTIM to receive buffered multicast.
        /// Default: 1.  Set to 0 here to let the router use its default.
        /// WinBox: "DTIM Period"
        /// </summary>
        [TikProperty("dtim-period", DefaultValue = "0")]
        public int DtimPeriod { get; set; }

        /// <summary>
        /// hide-ssid — when true the SSID is omitted from beacon frames (hidden network).
        /// Default: no.
        /// WinBox: "Hide SSID"
        /// </summary>
        [TikProperty("hide-ssid", DefaultValue = "no")]
        public bool HideSsid { get; set; }

        /// <summary>
        /// max-clients — maximum number of simultaneously associated stations (1..1000).
        /// Default: 1000.  Set to 0 here to let the router use its default.
        /// WinBox: "Max Clients"
        /// </summary>
        [TikProperty("max-clients", DefaultValue = "0")]
        public int MaxClients { get; set; }

        /// <summary>
        /// station-roaming — when true, the station periodically scans for a better AP.
        /// Default: no.
        /// WinBox: "Station Roaming"
        /// </summary>
        [TikProperty("station-roaming", DefaultValue = "no")]
        public bool StationRoaming { get; set; }

        // ── Environment / regulatory settings ────────────────────────────────

        /// <summary>
        /// installation — deployment environment, affects regulatory channel/power limits.
        /// Default: indoor.
        /// <seealso cref="InstallationType"/>
        /// WinBox: "Installation"
        /// </summary>
        [TikProperty("installation", DefaultValue = "indoor")]
        public InstallationType Installation { get; set; }

        /// <summary>
        /// distance — maximum link distance in kilometres for outdoor long-range links.
        /// Used to calculate ACK timeout.  Leave empty for indoor/default.
        /// WinBox: "Distance"
        /// </summary>
        [TikProperty("distance")]
        public string/*integer km*/ Distance { get; set; }

        // ── Management / CAPsMAN ──────────────────────────────────────────────

        /// <summary>
        /// manager — who controls this interface's configuration.
        /// Default: local.
        /// <seealso cref="ManagerType"/>
        /// WinBox: "Manager"
        /// </summary>
        [TikProperty("manager", DefaultValue = "local")]
        public ManagerType Manager { get; set; }

        // ── Traffic / QoS settings ────────────────────────────────────────────

        /// <summary>
        /// multicast-enhance — convert multicast to unicast per connected client.
        /// Default: disabled.
        /// <seealso cref="MulticastEnhanceMode"/>
        /// WinBox: "Multicast Enhance"
        /// </summary>
        [TikProperty("multicast-enhance", DefaultValue = "disabled")]
        public MulticastEnhanceMode MulticastEnhance { get; set; }

        /// <summary>
        /// qos-classifier — WMM traffic classification source.
        /// Default: priority.
        /// <seealso cref="QosClassifierMode"/>
        /// WinBox: "QoS Classifier"
        /// </summary>
        [TikProperty("qos-classifier", DefaultValue = "priority")]
        public QosClassifierMode QosClassifier { get; set; }

        /// <summary>
        /// hw-protection-mode — hardware collision-avoidance mechanism.
        /// Default: rts-cts.
        /// <seealso cref="HwProtectionModeType"/>
        /// WinBox: "HW Protection Mode"
        /// </summary>
        [TikProperty("hw-protection-mode", DefaultValue = "rts-cts")]
        public HwProtectionModeType HwProtectionMode { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disabled — when true this configuration profile is administratively disabled.
        /// Default: no.
        /// WinBox: "Disabled"
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment — short free-text description.
        /// WinBox: "Comment"
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
