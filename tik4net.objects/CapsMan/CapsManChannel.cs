using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/channel
    ///
    /// CAPsMAN channel profile (legacy CAPsMAN, RouterOS 6.x).  A channel profile groups
    /// radio frequency channel settings — band, frequency, channel width, extension channel,
    /// TX power, DFS handling, and automatic channel re-selection — that can be referenced by
    /// name from a /caps-man/configuration profile (via the "channel" field) or overridden
    /// inline using dotted notation (e.g. channel.band, channel.frequency).
    /// </summary>
    [TikEntity("/caps-man/channel", IncludeDetails = true)]
    public class CapsManChannel
    {
        // ── Extension-channel position ────────────────────────────────────────

        /// <summary>Extension-channel position values for the <see cref="ExtensionChannel"/> property.</summary>
        /// <seealso cref="ExtensionChannel"/>
        public enum ExtensionChannelType
        {
            /// <summary>disabled — no secondary channel; operate in 20 MHz mode (router default).</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>Ce — secondary channel is above the primary (HT40+).</summary>
            [TikEnum("Ce")] Ce,
            /// <summary>Ceee — secondary channel 3 slots above (80 MHz, primary in lowest slot).</summary>
            [TikEnum("Ceee")] Ceee,
            /// <summary>Ceeeeeee — secondary channel 7 slots above (160 MHz, primary in lowest slot).</summary>
            [TikEnum("Ceeeeeee")] Ceeeeeee,
            /// <summary>XX — 80 MHz, primary channel in middle (2nd slot).</summary>
            [TikEnum("XX")] Xx,
            /// <summary>XXXX — 160 MHz, primary channel in 4th slot.</summary>
            [TikEnum("XXXX")] Xxxx,
            /// <summary>XXXXXXXX — 160 MHz, primary channel in 8th slot.</summary>
            [TikEnum("XXXXXXXX")] Xxxxxxxx,
            /// <summary>eC — secondary channel is below the primary (HT40-).</summary>
            [TikEnum("eC")] EC,
            /// <summary>eCee — 80 MHz, primary channel in 2nd slot from bottom.</summary>
            [TikEnum("eCee")] ECee,
            /// <summary>eCeeeeee — 160 MHz, primary channel in 2nd slot.</summary>
            [TikEnum("eCeeeeee")] ECeeeeee,
            /// <summary>eeCe — 80 MHz, primary channel in 3rd slot from bottom.</summary>
            [TikEnum("eeCe")] EeCe,
            /// <summary>eeCeeeee — 160 MHz, primary channel in 3rd slot.</summary>
            [TikEnum("eeCeeeee")] EeCeeeee,
            /// <summary>eeeC — 80 MHz, primary channel in top slot.</summary>
            [TikEnum("eeeC")] EeeC,
            /// <summary>eeeCeeee — 160 MHz, primary channel in 4th slot.</summary>
            [TikEnum("eeeCeeee")] EeeCeeee,
            /// <summary>eeeeCeee — 160 MHz, primary channel in 5th slot.</summary>
            [TikEnum("eeeeCeee")] EeeeceEee,
            /// <summary>eeeeeCee — 160 MHz, primary channel in 6th slot.</summary>
            [TikEnum("eeeeeCee")] EeeeeecEe,
            /// <summary>eeeeeeCe — 160 MHz, primary channel in 7th slot.</summary>
            [TikEnum("eeeeeeCe")] EeeeeeeCe,
            /// <summary>eeeeeeeC — 160 MHz, primary channel in top slot.</summary>
            [TikEnum("eeeeeeeC")] EeeeeeeeC,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this channel profile; referenced from /caps-man/configuration.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Frequency band ────────────────────────────────────────────────────

        /// <summary>
        /// band — radio frequency band and operational mode taken from the hardware capability of
        /// the wireless card.  Common values: 2ghz-b, 2ghz-b/g, 2ghz-b/g/n, 2ghz-g/n,
        /// 2ghz-onlyg, 2ghz-onlyn, 5ghz-a, 5ghz-a/n, 5ghz-a/n/ac, 5ghz-n/ac,
        /// 5ghz-onlyac, 5ghz-onlyn.
        /// Left as string because the available values depend on installed hardware and there is no
        /// fixed router default (blank = hardware capability).
        /// </summary>
        [TikProperty("band", DefaultValue = "")]
        public string Band { get; set; }

        // ── Frequency ─────────────────────────────────────────────────────────

        /// <summary>
        /// frequency — operating frequency in MHz; leave blank/empty for automatic channel selection.
        /// May be a comma-separated list of frequencies for scanning.
        /// Valid range: 0–4294967295 per field; left as string because comma-separated lists are valid.
        /// </summary>
        [TikProperty("frequency", DefaultValue = "")]
        public string/*MHz list*/ Frequency { get; set; }

        // ── Channel width ─────────────────────────────────────────────────────

        /// <summary>
        /// control-channel-width — sets the control channel width for legacy (non-802.11n) modes.
        /// Values: 5mhz, 10mhz, 20mhz, 40mhz-turbo.
        /// Left as string because there is no fixed router default (blank = hardware decides).
        /// </summary>
        [TikProperty("control-channel-width", DefaultValue = "")]
        public string ControlChannelWidth { get; set; }

        // ── Extension channel ─────────────────────────────────────────────────

        /// <summary>
        /// extension-channel — position of the secondary channel relative to the primary channel
        /// for 40/80/160 MHz 802.11n/ac operation.
        /// Default: disabled (20 MHz only, no extension channel).
        /// <seealso cref="ExtensionChannelType"/>
        /// </summary>
        [TikProperty("extension-channel", DefaultValue = "disabled")]
        public ExtensionChannelType ExtensionChannel { get; set; }

        // ── Transmit power ────────────────────────────────────────────────────

        /// <summary>
        /// tx-power — transmit power in dBm; limited by country regulatory domain and interface
        /// hardware capabilities.  Valid range: -30..40.
        /// DefaultValue="0" prevents sending 0 on add (0 is the CLR sentinel, not a valid override).
        /// </summary>
        [TikProperty("tx-power", DefaultValue = "0")]
        public int TxPower { get; set; }

        // ── Secondary frequency ───────────────────────────────────────────────

        /// <summary>
        /// secondary-frequency — secondary frequency in MHz for 80+80 MHz 802.11ac operation, or
        /// "disabled" to disable 80+80 mode.
        /// Default: disabled.
        /// </summary>
        [TikProperty("secondary-frequency", DefaultValue = "disabled")]
        public string/*MHz or "disabled"*/ SecondaryFrequency { get; set; }

        // ── Automatic channel re-selection ────────────────────────────────────

        /// <summary>
        /// reselect-interval — interval between automatic frequency re-optimisation scans
        /// (time value, e.g. "1h", "30m").  Empty = no automatic re-selection.
        /// </summary>
        [TikProperty("reselect-interval", DefaultValue = "")]
        public string/*time*/ ReselectInterval { get; set; }

        /// <summary>
        /// save-selected — when true, persists the automatically-selected frequency across
        /// CAP reconnections until the next re-optimisation cycle.
        /// Default: yes (true); DefaultValue="no" so a fresh (false) entity does not send "no"
        /// unnecessarily — the router applies its own default of yes.
        /// </summary>
        [TikProperty("save-selected", DefaultValue = "no")]
        public bool SaveSelected { get; set; }

        // ── DFS ───────────────────────────────────────────────────────────────

        /// <summary>
        /// skip-dfs-channels — when true, excludes DFS (Dynamic Frequency Selection) channels
        /// from automatic frequency selection to avoid mandatory radar-detection delays.
        /// Default: no (include DFS channels).
        /// </summary>
        [TikProperty("skip-dfs-channels", DefaultValue = "no")]
        public bool SkipDfsChannels { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// comment — short free-text description of this channel profile.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
