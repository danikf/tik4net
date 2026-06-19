using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/configuration
    ///
    /// CAPsMAN configuration profile (legacy CAPsMAN, RouterOS 6.x).  A configuration profile is a
    /// reusable preset of wireless, radio, security, and datapath settings that can be assigned to
    /// CAP radios during provisioning via /caps-man/provisioning.
    ///
    /// Sub-profiles can be referenced by name (channel, datapath, security, rates) or their individual
    /// fields can be overridden inline via dotted notation (e.g. channel.band, datapath.local-forwarding).
    /// This entity maps the profile-reference string fields and the most common inline dotted overrides.
    /// </summary>
    [TikEntity("/caps-man/configuration", IncludeDetails = true)]
    public class CapsManConfiguration
    {
        // ── Operating mode ────────────────────────────────────────────────────

        /// <summary>Operating mode values for the <see cref="Mode"/> property.</summary>
        /// <seealso cref="Mode"/>
        public enum OperatingMode
        {
            /// <summary>ap — access-point mode (only mode supported by legacy CAPsMAN).</summary>
            [TikEnum("ap")] Ap,
        }

        // ── Installation environment ──────────────────────────────────────────

        /// <summary>Installation environment values for the <see cref="Installation"/> property.</summary>
        /// <seealso cref="Installation"/>
        public enum InstallationType
        {
            /// <summary>any — no environment restriction (default); allows all channels/power levels.</summary>
            [TikEnum("any")] Any,
            /// <summary>indoor — indoor installation; applies indoor regulatory limits.</summary>
            [TikEnum("indoor")] Indoor,
            /// <summary>outdoor — outdoor installation; applies outdoor regulatory limits.</summary>
            [TikEnum("outdoor")] Outdoor,
        }

        // ── Guard interval ────────────────────────────────────────────────────

        /// <summary>Guard interval values for the <see cref="GuardInterval"/> property.</summary>
        /// <seealso cref="GuardInterval"/>
        public enum GuardIntervalType
        {
            /// <summary>any — allow both short and long guard intervals (default).</summary>
            [TikEnum("any")] Any,
            /// <summary>long — force long guard interval only.</summary>
            [TikEnum("long")] Long,
        }

        // ── Multicast helper ──────────────────────────────────────────────────

        /// <summary>Multicast helper mode values for the <see cref="MulticastHelper"/> property.</summary>
        /// <seealso cref="MulticastHelper"/>
        public enum MulticastHelperMode
        {
            /// <summary>default — use router default multicast handling (default).</summary>
            [TikEnum("default")] Default,
            /// <summary>dhcp — convert multicast to unicast for DHCP-known clients.</summary>
            [TikEnum("dhcp")] Dhcp,
            /// <summary>disabled — disable multicast helper.</summary>
            [TikEnum("disabled")] Disabled,
            /// <summary>full — convert all multicast to unicast for all clients.</summary>
            [TikEnum("full")] Full,
        }

        // ── Keepalive frames ──────────────────────────────────────────────────

        /// <summary>Keepalive frames mode values for the <see cref="KeepaliveFrames"/> property.</summary>
        /// <seealso cref="KeepaliveFrames"/>
        public enum KeepaliveFramesMode
        {
            /// <summary>enabled — send keepalive frames to verify client presence (default).</summary>
            [TikEnum("enabled")] Enabled,
            /// <summary>disabled — disable keepalive frame transmission.</summary>
            [TikEnum("disabled")] Disabled,
        }

        // ── HW protection mode ────────────────────────────────────────────────

        /// <summary>Hardware protection mode values for the <see cref="HwProtectionMode"/> property.</summary>
        /// <seealso cref="HwProtectionMode"/>
        public enum HwProtectionModeType
        {
            /// <summary>none — no hardware frame protection (default).</summary>
            [TikEnum("none")] None,
            /// <summary>cts-to-self — send CTS-to-self frame before transmitting.</summary>
            [TikEnum("cts-to-self")] CtsToSelf,
            /// <summary>rts-cts — use RTS/CTS handshake before transmitting.</summary>
            [TikEnum("rts-cts")] RtsCts,
        }

        // ── Primary key ───────────────────────────────────────────────────────

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        // ── Identification ────────────────────────────────────────────────────

        /// <summary>
        /// name — unique name for this configuration profile.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        // ── Network identity ──────────────────────────────────────────────────

        /// <summary>
        /// ssid — the wireless network name (ESSID) broadcast in beacon frames (0–32 characters).
        /// </summary>
        [TikProperty("ssid")]
        public string Ssid { get; set; }

        /// <summary>
        /// mode — operational mode. Only "ap" (access point) is currently supported by legacy CAPsMAN.
        /// Default: ap.
        /// <seealso cref="OperatingMode"/>
        /// </summary>
        [TikProperty("mode", DefaultValue = "ap")]
        public OperatingMode Mode { get; set; }

        /// <summary>
        /// hide-ssid — when true the SSID is omitted from beacon frames and probe responses.
        /// Default: no (SSID visible).
        /// </summary>
        [TikProperty("hide-ssid", DefaultValue = "no")]
        public bool HideSsid { get; set; }

        // ── Sub-profile references ────────────────────────────────────────────
        // These string fields hold the name of a shared sub-profile (from the respective
        // /caps-man/channel, /caps-man/datapath, /caps-man/security, /caps-man/rates menus),
        // or empty for "none". Inline dotted overrides (e.g. channel.band, datapath.bridge) are
        // mapped below as individual properties.

        /// <summary>
        /// channel — name of the /caps-man/channel profile to apply, or empty for inline channel settings.
        /// </summary>
        [TikProperty("channel")]
        public string Channel { get; set; }

        /// <summary>
        /// datapath — name of the /caps-man/datapath profile to apply, or empty for inline datapath settings.
        /// </summary>
        [TikProperty("datapath")]
        public string Datapath { get; set; }

        /// <summary>
        /// security — name of the /caps-man/security profile to apply, or empty for inline security settings.
        /// Default: none (open network).
        /// </summary>
        [TikProperty("security")]
        public string Security { get; set; }

        /// <summary>
        /// rates — name of the /caps-man/rates profile to apply, or empty for inline rate settings.
        /// </summary>
        [TikProperty("rates")]
        public string Rates { get; set; }

        // ── Inline channel overrides (channel.*) ──────────────────────────────

        /// <summary>
        /// channel.band — radio frequency band and operational mode.
        /// Common values: 2ghz-b/g/n, 5ghz-a/n, 5ghz-a/n/ac.
        /// </summary>
        [TikProperty("channel.band")]
        public string ChannelBand { get; set; }

        /// <summary>
        /// channel.frequency — operating frequency in MHz; empty enables automatic channel selection.
        /// Valid range 0–4294967295; DefaultValue="0" prevents sending 0 on add (out of range).
        /// </summary>
        [TikProperty("channel.frequency", DefaultValue = "0")]
        public int ChannelFrequency { get; set; }

        /// <summary>
        /// channel.width — channel width in MHz (e.g. 20, 40).
        /// </summary>
        [TikProperty("channel.width")]
        public string ChannelWidth { get; set; }

        /// <summary>
        /// channel.extension-channel — secondary channel position for 40 MHz: Ce=above, eC=below, disabled, etc.
        /// </summary>
        [TikProperty("channel.extension-channel")]
        public string ChannelExtensionChannel { get; set; }

        /// <summary>
        /// channel.tx-power — transmit power override in dBm.
        /// Valid range -30..40; DefaultValue="0" prevents sending 0 on add.
        /// </summary>
        [TikProperty("channel.tx-power", DefaultValue = "0")]
        public int ChannelTxPower { get; set; }

        /// <summary>
        /// channel.reselect-interval — interval for automatic frequency re-optimisation (time value, e.g. "1h").
        /// </summary>
        [TikProperty("channel.reselect-interval")]
        public string/*time*/ ChannelReselectInterval { get; set; }

        /// <summary>
        /// channel.save-selected — persist the auto-selected frequency across CAP reconnections.
        /// Default: no.
        /// </summary>
        [TikProperty("channel.save-selected", DefaultValue = "no")]
        public bool ChannelSaveSelected { get; set; }

        /// <summary>
        /// channel.skip-dfs-channels — exclude DFS channels from automatic frequency selection.
        /// Default: no.
        /// </summary>
        [TikProperty("channel.skip-dfs-channels", DefaultValue = "no")]
        public bool ChannelSkipDfsChannels { get; set; }

        /// <summary>
        /// channel.secondary-frequency — secondary frequency for 80+80 MHz operation in MHz;
        /// "auto" or "0" to let the router decide.
        /// DefaultValue="0" prevents sending 0 on add.
        /// </summary>
        [TikProperty("channel.secondary-frequency", DefaultValue = "0")]
        public int ChannelSecondaryFrequency { get; set; }

        // ── Inline datapath overrides (datapath.*) ────────────────────────────

        /// <summary>
        /// datapath.local-forwarding — when true, CAP forwards packets locally without passing
        /// them through the CAPsMAN controller.
        /// Default: no.
        /// </summary>
        [TikProperty("datapath.local-forwarding", DefaultValue = "no")]
        public bool DatapathLocalForwarding { get; set; }

        /// <summary>
        /// datapath.bridge — bridge interface to which the virtual wireless interface will be added
        /// as a port automatically.
        /// </summary>
        [TikProperty("datapath.bridge")]
        public string DatapathBridge { get; set; }

        /// <summary>
        /// datapath.bridge-cost — spanning tree port cost for the bridge port.
        /// Valid range 1–200000000; DefaultValue="0" prevents sending 0 on add.
        /// </summary>
        [TikProperty("datapath.bridge-cost", DefaultValue = "0")]
        public int DatapathBridgeCost { get; set; }

        /// <summary>
        /// datapath.bridge-horizon — bridge horizon parameter for the port.
        /// Valid range 0–4294967295; DefaultValue="0" prevents sending 0 on add.
        /// </summary>
        [TikProperty("datapath.bridge-horizon", DefaultValue = "0")]
        public int DatapathBridgeHorizon { get; set; }

        /// <summary>
        /// datapath.client-to-client-forwarding — permit direct wireless-to-wireless client communication.
        /// Default: no.
        /// </summary>
        [TikProperty("datapath.client-to-client-forwarding", DefaultValue = "no")]
        public bool DatapathClientToClientForwarding { get; set; }

        /// <summary>
        /// datapath.vlan-mode — VLAN tagging type: use-service-tag (802.1ad) or use-tag (802.1q).
        /// </summary>
        [TikProperty("datapath.vlan-mode")]
        public string DatapathVlanMode { get; set; }

        /// <summary>
        /// datapath.vlan-id — VLAN identifier for tagged traffic (1–4095).
        /// DefaultValue="0" prevents sending 0 on add (out of range).
        /// </summary>
        [TikProperty("datapath.vlan-id", DefaultValue = "0")]
        public int DatapathVlanId { get; set; }

        /// <summary>
        /// datapath.mtu — IP layer maximum transmission unit for the virtual interface.
        /// DefaultValue="0" prevents sending 0 on add.
        /// </summary>
        [TikProperty("datapath.mtu", DefaultValue = "0")]
        public int DatapathMtu { get; set; }

        /// <summary>
        /// datapath.l2mtu — link-layer maximum transmission unit.
        /// DefaultValue="0" prevents sending 0 on add.
        /// </summary>
        [TikProperty("datapath.l2mtu", DefaultValue = "0")]
        public int DatapathL2Mtu { get; set; }

        // ── Inline security overrides (security.*) ────────────────────────────

        /// <summary>
        /// security.authentication-types — comma-separated list of accepted authentication protocols.
        /// Values: wpa-psk, wpa2-psk, wpa-eap, wpa2-eap. Empty = open (no authentication).
        /// </summary>
        [TikProperty("security.authentication-types")]
        public string SecurityAuthenticationTypes { get; set; }

        /// <summary>
        /// security.encryption — unicast frame cipher algorithm (aes-ccm or tkip).
        /// </summary>
        [TikProperty("security.encryption")]
        public string SecurityEncryption { get; set; }

        /// <summary>
        /// security.group-encryption — broadcast/multicast frame cipher; clients must support this cipher.
        /// Default: aes-ccm.
        /// </summary>
        [TikProperty("security.group-encryption", DefaultValue = "aes-ccm")]
        public string SecurityGroupEncryption { get; set; }

        /// <summary>
        /// security.group-key-update — interval for rotating the group cipher key (30s–1h).
        /// Default: 5m.
        /// </summary>
        [TikProperty("security.group-key-update", DefaultValue = "5m")]
        public string/*time*/ SecurityGroupKeyUpdate { get; set; }

        /// <summary>
        /// security.passphrase — WPA/WPA2 pre-shared key (PSK).
        /// </summary>
        [TikProperty("security.passphrase")]
        public string SecurityPassphrase { get; set; }

        /// <summary>
        /// security.eap-methods — EAP authentication method(s): eap-tls or passthrough (RADIUS relay).
        /// </summary>
        [TikProperty("security.eap-methods")]
        public string SecurityEapMethods { get; set; }

        /// <summary>
        /// security.tls-certificate — name of the certificate used for EAP-TLS server authentication.
        /// Use "none" to disable certificate-based auth.
        /// </summary>
        [TikProperty("security.tls-certificate")]
        public string SecurityTlsCertificate { get; set; }

        /// <summary>
        /// security.tls-mode — client certificate validation behaviour for EAP-TLS.
        /// Values: verify-certificate, dont-verify-certificate, no-certificates, verify-certificate-with-crl.
        /// </summary>
        [TikProperty("security.tls-mode")]
        public string SecurityTlsMode { get; set; }

        // ── Inline rates overrides (rates.*) ──────────────────────────────────

        /// <summary>
        /// rates.basic — comma-separated list of mandatory data rates all clients must support
        /// (e.g. "1Mbps,2Mbps,5.5Mbps,11Mbps").
        /// </summary>
        [TikProperty("rates.basic")]
        public string RatesBasic { get; set; }

        /// <summary>
        /// rates.supported — comma-separated list of optional advertised data rates
        /// (e.g. "6Mbps,9Mbps,12Mbps,18Mbps,24Mbps,36Mbps,48Mbps,54Mbps").
        /// </summary>
        [TikProperty("rates.supported")]
        public string RatesSupported { get; set; }

        /// <summary>
        /// rates.ht-basic-mcs — comma-separated list of required 802.11n MCS indices
        /// (e.g. "mcs-0,mcs-1,mcs-2,mcs-3,mcs-4,mcs-5,mcs-6,mcs-7").
        /// Default: mcs-0 through mcs-7.
        /// </summary>
        [TikProperty("rates.ht-basic-mcs")]
        public string RatesHtBasicMcs { get; set; }

        /// <summary>
        /// rates.ht-supported-mcs — comma-separated list of advertised 802.11n MCS indices.
        /// Default: mcs-0 through mcs-23.
        /// </summary>
        [TikProperty("rates.ht-supported-mcs")]
        public string RatesHtSupportedMcs { get; set; }

        /// <summary>
        /// rates.vht-basic-mcs — required 802.11ac MCS set per spatial stream
        /// (none, MCS 0-7, MCS 0-8, MCS 0-9). Default: none.
        /// </summary>
        [TikProperty("rates.vht-basic-mcs")]
        public string RatesVhtBasicMcs { get; set; }

        /// <summary>
        /// rates.vht-supported-mcs — advertised 802.11ac MCS set per spatial stream
        /// (none, MCS 0-7, MCS 0-8, MCS 0-9). Default: none.
        /// </summary>
        [TikProperty("rates.vht-supported-mcs")]
        public string RatesVhtSupportedMcs { get; set; }

        // ── Radio / PHY ───────────────────────────────────────────────────────

        /// <summary>
        /// rx-chains — receive antenna chain indices to use (e.g. "0" or "0,1,2,3").
        /// Default: all available chains.
        /// </summary>
        [TikProperty("rx-chains")]
        public string/*chain-list*/ RxChains { get; set; }

        /// <summary>
        /// tx-chains — transmit antenna chain indices to use (e.g. "0" or "0,1,2,3").
        /// Default: all available chains.
        /// </summary>
        [TikProperty("tx-chains")]
        public string/*chain-list*/ TxChains { get; set; }

        /// <summary>
        /// guard-interval — guard interval preference for 802.11n transmissions.
        /// Default: any.
        /// <seealso cref="GuardIntervalType"/>
        /// </summary>
        [TikProperty("guard-interval", DefaultValue = "any")]
        public GuardIntervalType GuardInterval { get; set; }

        /// <summary>
        /// hw-protection-mode — hardware collision-avoidance mechanism.
        /// Default: none.
        /// <seealso cref="HwProtectionModeType"/>
        /// </summary>
        [TikProperty("hw-protection-mode", DefaultValue = "none")]
        public HwProtectionModeType HwProtectionMode { get; set; }

        /// <summary>
        /// hw-retries — number of times to retry sending a frame at the hardware level (0..15).
        /// DefaultValue="0" prevents sending 0 on add when unset.
        /// </summary>
        [TikProperty("hw-retries", DefaultValue = "0")]
        public int HwRetries { get; set; }

        // ── Client management ─────────────────────────────────────────────────

        /// <summary>
        /// max-sta-count — maximum number of simultaneously associated client stations (1–2007).
        /// DefaultValue="0" prevents sending 0 on add (0 is out of range, 0 = unlimited/not-set sentinel).
        /// </summary>
        [TikProperty("max-sta-count", DefaultValue = "0")]
        public int MaxStaCount { get; set; }

        /// <summary>
        /// load-balancing-group — tag to group overlapping CAP interfaces for load balancing.
        /// </summary>
        [TikProperty("load-balancing-group")]
        public string LoadBalancingGroup { get; set; }

        /// <summary>
        /// keepalive-frames — client presence verification via keepalive frames.
        /// Default: enabled.
        /// <seealso cref="KeepaliveFramesMode"/>
        /// </summary>
        [TikProperty("keepalive-frames", DefaultValue = "enabled")]
        public KeepaliveFramesMode KeepaliveFrames { get; set; }

        // ── Regulatory / environment ──────────────────────────────────────────

        /// <summary>
        /// country — regulatory domain that restricts available channels and transmit power.
        /// Common values: "no_country_set", "latvia", "united states", etc.
        /// Default: no_country_set.
        /// </summary>
        [TikProperty("country", DefaultValue = "no_country_set")]
        public string Country { get; set; }

        /// <summary>
        /// installation — deployment environment that affects regulatory channel/power limits.
        /// Default: any.
        /// <seealso cref="InstallationType"/>
        /// </summary>
        [TikProperty("installation", DefaultValue = "any")]
        public InstallationType Installation { get; set; }

        /// <summary>
        /// distance — link distance hint: "indoors" or "dynamic" (auto ACK timeout).
        /// Leave empty for default behaviour.
        /// </summary>
        [TikProperty("distance")]
        public string Distance { get; set; }

        // ── Frame / timing parameters ─────────────────────────────────────────

        /// <summary>
        /// frame-lifetime — maximum age of a queued frame before it is discarded (time value, e.g. "1ms").
        /// Empty = no limit.
        /// </summary>
        [TikProperty("frame-lifetime")]
        public string/*time*/ FrameLifetime { get; set; }

        /// <summary>
        /// disconnect-timeout — how long to wait after the last keepalive failure before
        /// de-authenticating the client (time value, e.g. "3s").
        /// </summary>
        [TikProperty("disconnect-timeout")]
        public string/*time*/ DisconnectTimeout { get; set; }

        // ── Multicast ─────────────────────────────────────────────────────────

        /// <summary>
        /// multicast-helper — strategy for converting multicast traffic to unicast.
        /// Default: default.
        /// <seealso cref="MulticastHelperMode"/>
        /// </summary>
        [TikProperty("multicast-helper", DefaultValue = "default")]
        public MulticastHelperMode MulticastHelper { get; set; }

        // ── Administrative ────────────────────────────────────────────────────

        /// <summary>
        /// comment — short free-text description of this configuration profile.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
