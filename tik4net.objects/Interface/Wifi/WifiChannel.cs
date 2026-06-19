namespace tik4net.Objects.Interface.Wifi
{
    /// <summary>
    /// /interface/wifi/channel
    ///
    /// WiFi channel profile (ROS 7 wifi package).  A channel profile groups the radio
    /// channel settings (band, width, frequency list, DFS behaviour, periodic re-scan)
    /// that can be shared across multiple /interface/wifi entries or referenced from a
    /// /interface/wifi/configuration profile via the <c>channel</c> field.
    ///
    /// Frequency and width defaults are hardware-dependent ("newest supported" /
    /// "widest supported") — leaving the corresponding fields empty lets the router
    /// apply the best value for the radio hardware.
    /// </summary>
    [TikEntity("/interface/wifi/channel", IncludeDetails = true)]
    public class WifiChannel
    {
        // ── Skip-DFS mode ─────────────────────────────────────────────────────

        /// <summary>Channel-skip mode values for the <see cref="SkipDfsChannels"/> property.</summary>
        /// <seealso cref="SkipDfsChannels"/>
        public enum SkipDfsChannelsMode
        {
            /// <summary>disabled — DFS channels are not avoided (default).</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>10min-cac — avoid channels that require a 10-minute Channel Availability Check.</summary>
            [TikEnum("10min-cac")] TenMinCac,
            /// <summary>all — avoid all DFS/radar-detection-required channels.</summary>
            [TikEnum("all")] All,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this channel profile.
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Radio band and frequency ──────────────────────────────────────────

        /// <summary>
        /// band — frequency band and wireless standard to use.
        /// Accepted values: 2ghz-g, 2ghz-n, 2ghz-ax, 2ghz-be, 5ghz-a, 5ghz-n, 5ghz-ac, 5ghz-ax,
        /// 5ghz-be, s1ghz-ah.  Leave empty to let the router pick the newest supported band.
        /// WinBox: "Band"
        /// </summary>
        [TikProperty("band", DefaultValue = "")]
        public string Band { get; set; }

        /// <summary>
        /// frequency — comma-separated list of channel centre frequencies in MHz to be considered
        /// by the AP for channel selection or by the station for scanning.
        /// Supports individual values and ranges with optional width suffix, e.g. "5180-5240:20".
        /// Leave empty to allow all frequencies permitted by the regulatory domain.
        /// WinBox: "Frequency"
        /// </summary>
        [TikProperty("frequency", DefaultValue = "")]
        public string Frequency { get; set; }

        /// <summary>
        /// secondary-frequency — for split-channel (80+80 MHz or 320 MHz) configurations,
        /// specifies the permitted secondary segment centre frequencies.
        /// Leave empty when not using split channels.
        /// WinBox: "Secondary Frequency"
        /// </summary>
        [TikProperty("secondary-frequency", DefaultValue = "")]
        public string SecondaryFrequency { get; set; }

        /// <summary>
        /// width — channel width.
        /// Accepted values: 20mhz, 20/40mhz, 20/40mhz-Ce, 20/40mhz-eC, 20/40/80mhz,
        /// 20/40/80/160mhz, 20/40/80/160/320mhz, 20/40/80+80mhz, 1mhz, 1/2mhz, 1/2/4mhz, 1/2/4/8mhz.
        /// Leave empty to use the widest width supported by the radio hardware.
        /// WinBox: "Width"
        /// </summary>
        [TikProperty("width", DefaultValue = "")]
        public string Width { get; set; }

        // ── DFS / channel selection ───────────────────────────────────────────

        /// <summary>
        /// skip-dfs-channels — controls whether channels requiring radar detection (DFS)
        /// are avoided during channel selection.
        /// Default: disabled.
        /// <seealso cref="SkipDfsChannelsMode"/>
        /// WinBox: "Skip DFS Channels"
        /// </summary>
        [TikProperty("skip-dfs-channels", DefaultValue = "disabled")]
        public SkipDfsChannelsMode SkipDfsChannels { get; set; }

        /// <summary>
        /// deprioritize-unii-3-4 — when true, channels with control frequencies of 5720 or
        /// 5825-5885 MHz (UNII-3/UNII-4) are assigned lower priority during channel selection.
        /// Some client devices do not support these channels.
        /// Default: yes in ETSI regulatory domains, no elsewhere.  This mapping uses no (false)
        /// as the add-safe CLR default.
        /// WinBox: "Deprioritize UNII 3/4"
        /// </summary>
        [TikProperty("deprioritize-unii-3-4", DefaultValue = "no")]
        public bool DeprioritizeUnii34 { get; set; }

        /// <summary>
        /// preamble-puncturing — enables 802.11be preamble puncturing, which allows the AP to
        /// use part of a wider channel that overlaps a DFS segment instead of switching away.
        /// Default: no.
        /// WinBox: "Preamble Puncturing"
        /// </summary>
        [TikProperty("preamble-puncturing", DefaultValue = "no")]
        public bool PreamblePuncturing { get; set; }

        // ── Periodic channel rescanning ───────────────────────────────────────

        /// <summary>
        /// reselect-interval — interval between periodic channel rescanning attempts.
        /// A random offset is applied so that multiple APs do not rescan simultaneously.
        /// Set to "disabled" to turn off periodic rescanning (default).
        /// WinBox: "Reselect Interval"
        /// </summary>
        [TikProperty("reselect-interval", DefaultValue = "disabled")]
        public string/*time|disabled*/ ReselectInterval { get; set; }

        /// <summary>
        /// reselect-time — wall-clock time at which a periodic channel rescan is triggered.
        /// A random offset is applied to avoid simultaneous scanning across APs.
        /// Set to "disabled" to turn off time-based rescanning (default).
        /// WinBox: "Reselect Time"
        /// </summary>
        [TikProperty("reselect-time", DefaultValue = "disabled")]
        public string/*time|disabled*/ ReselectTime { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// disabled — when true this channel profile is administratively disabled.
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
