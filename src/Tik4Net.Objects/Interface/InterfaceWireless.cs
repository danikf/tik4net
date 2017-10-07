using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{

    /// <summary>
    /// interface/wireless
    /// RouterOS wireless comply with IEEE 802.11 standards, it provides complete support for 802.11a, 802.11b, 802.11g, 802.11n and 802.11ac as long as additional features like WPA, WEP, AES encryption, Wireless Distribution System (WDS), Dynamic Frequency selection (DFS), Virtual Access Point, Nstreme and NV2 proprietary protocols and many more. Wireless features compatibility table for different wireless protocols.
    /// 
    /// Wireless can operate in several modes: client (station), access point, wireless bridge etc. Client/station also can operate in different modes, complete list of supported modes can be found here. 
    /// </summary>
    [TikEntity("interface/wireless")]
    public class InterfaceWireless
    {
        #region Submenu classes - Obsolete

        /// <summary>
        /// Obsolete: use Wireless.WirelessSecurityProfile class.
        /// </summary>
        [Obsolete("use Wireless.WirelessSecurityProfile class.", true)]
        public abstract class WirelessSecurityProfile
        {

        }

        /// <summary>
        /// Obsolete: use Wireless.WirelessAccessList class.
        /// </summary>
        [Obsolete("use Wireless.WirelessAccessList class.", true)]
        public abstract class WirelessAccessList
        {

        }

        /// <summary>
        /// Obsolete: use Wireless.WirelessRegistrationTable class.
        /// </summary>
        [Obsolete("use Wireless.WirelessRegistrationTable class.", true)]
        public abstract class WirelessRegistrationTable
        {

        }

        #endregion

        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// adaptive-noise-immunity: This property is only effective for cards based on Atheros chipset.
        /// 
        /// ap-and-client-mode | client-mode | none
        /// </summary>
        [TikProperty("adaptive-noise-immunity", DefaultValue = "none")]
        public string/*ap-and-client-mode | client-mode | none*/ AdaptiveNoiseImmunity { get; set; }

        /// <summary>
        /// allow-sharedkey: Allow WEP Shared Key cilents to connect. Note that no authentication is done for these clients (WEP Shared keys are not compared to anything) - they are just accepted at once (if access list allows that)
        /// </summary>
        [TikProperty("allow-sharedkey", DefaultValue = "no")]
        public bool AllowSharedkey { get; set; }

        /// <summary>
        /// antenna-gain: Antenna gain in dBi, used to calculate maximum transmit power according to country regulations.
        /// </summary>
        [TikProperty("antenna-gain", DefaultValue = "0")]
        public long/*integer [0..4294967295]*/ AntennaGain { get; set; }

        /// <summary>
        /// antenna-mode
        /// Select antenna to use for transmitting and for receiving
        ///  ant-a - use only 'a' antenna
        ///  ant-b - use only 'b' antenna
        ///  txa-rxb - use antenna 'a' for transmitting, antenna 'b' for receiving
        ///  rxa-txb - use antenna 'b' for transmitting, antenna 'a' for receiving
        /// </summary>
        [TikProperty("antenna-mode")]
        public string/*ant-a | ant-b | rxa-txb | txa-rxb*/ AntennaMode { get; set; }

        /// <summary>
        /// area
        /// Identifies group of wireless networks. This value is announced by AP, and can be matched in  connect-list by area-prefix. 
        /// This is a proprietary extension.
        /// </summary>
        [TikProperty("area")]
        public string Area { get; set; }

        /// <summary>
        /// arp:  Read more &gt;&gt;
        /// disabled | enabled | proxy-arp | reply-only
        /// </summary>
        [TikProperty("arp", DefaultValue = "enabled")]
        public string/*disabled | enabled | proxy-arp | reply-only*/ Arp { get; set; }

        /// <summary>
        /// band: Defines set of used data rates, channel frequencies and widths.
        /// 2ghz-b | 2ghz-b/g | 2ghz-b/g/n | 2ghz-onlyg | 2ghz-onlyn | 5ghz-a | 5ghz-a/n | 5ghz-onlyn | 5ghz-a/n/ac | 5ghz-only-AC
        /// </summary>
        [TikProperty("band")]
        public string/*2ghz-b | 2ghz-b/g | 2ghz-b/g/n | 2ghz-onlyg | 2ghz-onlyn | 5ghz-a | 5ghz-a/n | 5ghz-onlyn | 5ghz-a/n/ac | 5ghz-only-AC*/ Band { get; set; }

        /// <summary>
        /// Unknown: Similar to the basic-rates-b property, but used for 5ghz, 5ghz-10mhz, 5ghz-5mhz, 5ghz-turbo, 2.4ghz-b/g, 2.4ghz-onlyg, 2ghz-10mhz, 2ghz-5mhz and 2.4ghz-g-turbo bands.
        /// 12Mbps | 18Mbps | 24Mbps | 36Mbps | 48Mbps | 54Mbps | 6Mbps | 9Mbps; Default: 6Mbps
        /// </summary>
        [TikProperty("basic-rates-a/g")]
        public string /*basic-rates-a/g (12Mbps | 18Mbps | 24Mbps | 36Mbps | 48Mbps | 54Mbps | 6Mbps | 9Mbps; Default: 6Mbps)*/ BasicRatesAG { get; set; }

        /// <summary>
        /// basic-rates-b
        /// List of basic rates, used for 2.4ghz-b, 2.4ghz-b/g and 2.4ghz-onlyg bands.
        /// Client will connect to AP only if it supports all basic rates announced by the AP.
        /// AP will establish WDS link only if it supports all basic rates of the other AP.
        /// This property has effect only in AP modes, and when value of rate-set is configured.
        /// 
        /// 11Mbps | 1Mbps | 2Mbps | 5.5Mbps
        /// </summary>
        [TikProperty("basic-rates-b", DefaultValue = "1Mbps")]
        public string/*11Mbps | 1Mbps | 2Mbps | 5.5Mbps*/ BasicRatesB { get; set; }

        /// <summary>
        /// bridge-mode: Allows to use station-bridge mode.  Read more &gt;&gt;
        /// 
        /// disabled | enabled
        /// </summary>
        [TikProperty("bridge-mode", DefaultValue = "enabled")]
        public string/*disabled | enabled*/ BridgeMode { get; set; }

        /// <summary>
        /// burst-time: Time in microseconds which will be used to send data without stopping. Note that no other wireless cards in that network will be able to transmit data during burst-time microseconds. This setting is available only for AR5000, AR5001X, and AR5001X+ chipset based cards.
        /// 
        /// integer | disabled
        /// </summary>
        [TikProperty("burst-time", DefaultValue = "disabled")]
        public string/*integer | disabled*/ BurstTime { get; set; }

        /// <summary>
        /// channel-width: ht above and ht below allows to use additional 20MHz extension channel and if it should be located below or above control (main) channel. Extension channel allows 11n device to use 40MHz of spectrum in total thus increasing max throughput.
        /// 
        /// 10mhz | 20/40mhz-ht-above | 20/40mhz-ht-below | 20mhz | 40mhz-turbo | 5mhz
        /// </summary>
        [TikProperty("channel-width", DefaultValue = "20mhz")]
        public string/*10mhz | 20/40mhz-ht-above | 20/40mhz-ht-below | 20mhz | 40mhz-turbo | 5mhz*/ ChannelWidth { get; set; }

        /// <summary>
        /// comment: Short description of the interface
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// compression: Setting this property to yes will allow use of the hardware compression. Wireless interface must have support for hardware compression. Connections with devices that do not use compression will still work.
        /// </summary>
        [TikProperty("compression", DefaultValue = "no")]
        public bool Compression { get; set; }

        /// <summary>
        /// country: Limits available bands, frequencies and maximum transmit power for each frequency. Also specifies default value of scan-list. Value no_country_set is an FCC compliant set of channels.
        /// 
        /// name of the country | no_country_set
        /// </summary>
        [TikProperty("country", DefaultValue = "no_country_set")]
        public string/*name of the country | no_country_set*/ Country { get; set; }

        /// <summary>
        /// default-ap-tx-limit: This is the value of ap-tx-limit for clients that do not match any entry in the  access-list. 0 means no limit.
        /// integer [0..4294967295]
        /// </summary>
        [TikProperty("default-ap-tx-limit", DefaultValue = "0")]
        public long/*integer [0..4294967295]*/ DefaultApTxLimit { get; set; }

        /// <summary>
        /// default-authentication: For AP mode, this is the value of authentication for clients that do not match any entry in the  access-list. For station mode, this is the value of connect for APs that do not match any entry in the  connect-list
        /// </summary>
        [TikProperty("default-authentication", DefaultValue = "yes")]
        public bool DefaultAuthentication { get; set; }

        /// <summary>
        /// default-client-tx-limit: This is the value of client-tx-limit for clients that do not match any entry in the  access-list. 0 means no limit
        /// 
        /// integer [0..4294967295]
        /// </summary>
        [TikProperty("default-client-tx-limit", DefaultValue = "0")]
        public long/*integer [0..4294967295]*/ DefaultClientTxLimit { get; set; }

        /// <summary>
        /// default-forwarding: This is the value of forwarding for clients that do not match any entry in the  access-list
        /// </summary>
        [TikProperty("default-forwarding", DefaultValue = "yes")]
        public bool DefaultForwarding { get; set; }

        /// <summary>
        /// dfs-mode
        /// Controls DFS (Dynamic Frequency Selection).
        ///  none - disables DFS.
        ///  no-radar-detect - Select channel from scan-list with the lowest number of detected networks. In 'wds-slave' mode this setting has no effect.
        ///  radar-detect - Select channel with the lowest number of detected networks and use it if no radar is detected on it for 60 seconds. Otherwise, select different channel. This setting may be required by the country regulations.
        /// This property has effect only in AP mode.
        /// 
        /// no-radar-detect | none | radar-detec
        /// </summary>
        [TikProperty("dfs-mode", DefaultValue = "none")]
        public string/*no-radar-detect | none | radar-detec*/ DfsMode { get; set; }

        /// <summary>
        /// disable-running-check: When set to yes interface will always have running flag.  If value is set to no', the router determines whether the card is up and running - for AP one or more clients have to be registered to it, for station, it should be connected to an AP.
        /// </summary>
        [TikProperty("disable-running-check", DefaultValue = "no")]
        public bool DisableRunningCheck { get; set; }

        /// <summary>
        /// disabled: Whether interface is disabled
        /// </summary>
        [TikProperty("disabled", DefaultValue = "yes")]
        public bool Disabled { get; set; }

        /// <summary>
        /// disconnect-timeout
        /// This interval is measured from third sending failure on the lowest data rate. At this point 3 * (hw-retries + 1) frame transmits on the lowest data rate had failed.
        /// During disconnect-timeout packet transmission will be retried with on-fail-retry-time interval. If no frame can be transmitted successfully during diconnect-timeout, connection is closed, and this event is logged as "extensive data loss". Successful frame transmission resets this timer.
        /// 
        /// time [0s..15s]
        /// </summary>
        [TikProperty("disconnect-timeout", DefaultValue = "3s")]
        public string/*time [0s..15s]*/ DisconnectTimeout { get; set; }

        /// <summary>
        /// distance
        /// How long to wait for confirmation of unicast frames before considering transmission unsuccessful. Value 'dynamic' causes AP to detect and use smallest timeout that works with all connected clients.
        /// Acknowledgements are not used in Nstreme protocol.
        /// 
        /// integer | dynamic | indoors
        /// </summary>
        [TikProperty("distance", DefaultValue = "dynamic")]
        public string/*integer | dynamic | indoors*/ Distance { get; set; }

        /// <summary>
        /// frame-lifetime: Discard frames that have been queued for sending longer than frame-lifetime. By default, when value of this property is 0, frames are discarded only after connection is closed.
        /// 
        /// integer [0..4294967295]
        /// </summary>
        [TikProperty("frame-lifetime", DefaultValue = "0")]
        public long/*integer [0..4294967295]*/ FrameLifetime { get; set; }

        /// <summary>
        /// frequency
        /// Channel frequency value in MHz on which AP will operate.
        /// Allowed values depend on selected band, and are restricted by country setting and wireless card capabilities.
        /// This setting has no effect if interface is in any of station modes, or in wds-slave mode, or if DFS is active. 
        /// Note: If using mode "superchannel", any frequency supported by the card will be accepted, but on the RouterOS client, any non-standard frequency must be configured in the  scan-list, otherwise it will not be scanning in non-standard range. In Winbox, scanlist frequencies are in bold, any other frequency means the clients will need scan-list configured.
        /// 
        /// integer [0..4294967295]
        /// </summary>
        [TikProperty("frequency")]
        public String/*integer [0..4294967295], string "auto"*/ Frequency { get; set; }

        /// <summary>
        /// frequency-mode
        /// Three frequency modes are available:
        ///  regulatory-domain - Limit available channels and maximum transmit power for each channel according to the value of country
        ///  manual-txpower - Same as above, but do not limit maximum transmit power.
        ///  superchannel - Conformance Testing Mode. Allow all channels supported by the card.
        /// List of available channels for each band can be seen in /wireless info print. This mode allows you to test wireless channels outside the default scan-list and/or regulatory domain. This mode should only be used in controlled environments, or if you have a special permission to use it in your region. Before v4.3 this was called Custom Frequency Upgrade, or Superchannel. Since RouterOS v4.3 this mode is available without special key upgrades to all installations.
        /// 
        /// manual-txpower | regulatory-domain | superchannel
        /// </summary>
        [TikProperty("frequency-mode", DefaultValue = "manual-txpower")]
        public string/*manual-txpower | regulatory-domain | superchannel*/ FrequencyMode { get; set; }

        /// <summary>
        /// frequency-offset: Allows to specify offset if the used wireless card operates at a different frequency than is shown in RouterOS, in case a frequency converter is used in the card. So if your card works at 4000MHz but RouterOS shows 5000MHz, set offset to 1000MHz and it will be displayed correctly. The value is in MHz and can be positive or negative.
        /// 
        /// integer [-2147483648..2147483647]
        /// </summary>
        [TikProperty("frequency-offset", DefaultValue = "0")]
        public int/*integer [-2147483648..2147483647]*/ FrequencyOffset { get; set; }

        /// <summary>
        /// hide-ssid
        /// .
        ///  yes - AP does not include SSID in the beacon frames, and does not reply to probe requests that have broadcast SSID.
        ///  no - AP includes SSID in the beacon frames, and replies to probe requests that have broadcast SSID.
        /// This property has effect only in AP mode. Setting it to yes can remove this network from the list of wireless networks that are shown by some client software. Changing this setting does not improve security of the wireless network, because SSID is included in other frames sent by the AP.
        /// </summary>
        [TikProperty("hide-ssid", DefaultValue = "no")]
        public bool HideSsid { get; set; }

        /// <summary>
        /// ht-ampdu-priorities: Frame priorities for which AMPDU sending (aggregating frames and sending using block acknowledgement) should get negotiated and used. Using AMPDUs will increase throughput, but may increase latency therefore may not be desirable for real-time traffic (voice, video). Due to this, by default AMPDUs are enabled only for best-effort traffic.
        /// 
        /// list of integer [0..7]
        /// </summary>
        [TikProperty("ht-ampdu-priorities", DefaultValue = "0")]
        public string/*list of integer [0..7]*/ HtAmpduPriorities { get; set; }

        /// <summary>
        /// ht-amsdu-limit: Max AMSDU that device is allowed to prepare when negotiated. AMSDU aggregation may significantly increase throughput especially for small frames, but may increase latency in case of packet loss due to retransmission of aggregated frame. Sending and receiving AMSDUs will also increase CPU usage.
        /// 
        /// integer [0..8192]
        /// </summary>
        [TikProperty("ht-amsdu-limit", DefaultValue = "8192")]
        public string/*integer [0..8192]*/ HtAmsduLimit { get; set; }

        /// <summary>
        /// ht-amsdu-threshold: Max frame size to allow including in AMSDU.
        /// 
        /// integer [0..8192]
        /// </summary>
        [TikProperty("ht-amsdu-threshold", DefaultValue = "8192")]
        public string/*integer [0..8192]*/ HtAmsduThreshold { get; set; }

        /// <summary>
        /// ht-basic-mcs: Modulation and Coding Schemes that every connecting client must support. Refer to 802.11n for MCS specification.
        /// 
        /// list of (mcs-0 | mcs-1 | mcs-2 | mcs-3 | mcs-4 | mcs-5 | mcs-6 | mcs-7 | mcs-8 | mcs-9 | mcs-10 | mcs-11 | mcs-12 | mcs-13 | mcs-14 | mcs-15 | mcs-16 | mcs-17 | mcs-18 | mcs-19 | mcs-20 | mcs-21 | mcs-22 | mcs-23)
        /// </summary>
        [TikProperty("ht-basic-mcs", DefaultValue = "mcs-0; mcs-1; mcs-2; mcs-3; mcs-4; mcs-5; mcs-6; mcs-7")]
        public string/*list of (mcs-0 | mcs-1 | mcs-2 | mcs-3 | mcs-4 | mcs-5 | mcs-6 | mcs-7 | mcs-8 | mcs-9 | mcs-10 | mcs-11 | mcs-12 | mcs-13 | mcs-14 | mcs-15 | mcs-16 | mcs-17 | mcs-18 | mcs-19 | mcs-20 | mcs-21 | mcs-22 | mcs-23)*/ HtBasicMcs { get; set; }

        /// <summary>
        /// ht-guard-interval: Whether to  allow use of short guard interval (refer to 802.11n MCS specification to see how this may affect throughput). "any" will use either short or long, depending on data rate, "long" will use long.
        /// 
        /// any | long
        /// </summary>
        [TikProperty("ht-guard-interval", DefaultValue = "any")]
        public string/*any | long*/ HtGuardInterval { get; set; }

        /// <summary>
        /// ht-rxchains: Which antennas to use for receive.
        /// 
        /// list of integer [0..2]
        /// </summary>
        [TikProperty("ht-rxchains", DefaultValue = "0")]
        public string/*list of integer [0..2]*/ HtRxchains { get; set; }

        /// <summary>
        /// ht-supported-mcs: Modulation and Coding Schemes that this device advertises as supported. Refer to 802.11n for MCS specification.
        /// 
        /// list of (mcs-0 | mcs-1 | mcs-2 | mcs-3 | mcs-4 | mcs-5 | mcs-6 | mcs-7 | mcs-8 | mcs-9 | mcs-10 | mcs-11 | mcs-12 | mcs-13 | mcs-14 | mcs-15 | mcs-16 | mcs-17 | mcs-18 | mcs-19 | mcs-20 | mcs-21 | mcs-22 | mcs-23)
        /// </summary>
        [TikProperty("ht-supported-mcs", DefaultValue = "")]
        public string/*list of (mcs-0 | mcs-1 | mcs-2 | mcs-3 | mcs-4 | mcs-5 | mcs-6 | mcs-7 | mcs-8 | mcs-9 | mcs-10 | mcs-11 | mcs-12 | mcs-13 | mcs-14 | mcs-15 | mcs-16 | mcs-17 | mcs-18 | mcs-19 | mcs-20 | mcs-21 | mcs-22 | mcs-23)*/ HtSupportedMcs { get; set; }

        /// <summary>
        /// ht-txchains: Which antetnnas to use for transmit.
        /// 
        /// list of integer [0..2]
        /// </summary>
        [TikProperty("ht-txchains", DefaultValue = "0")]
        public string/*list of integer [0..2]*/ HtTxchains { get; set; }

        /// <summary>
        /// hw-fragmentation-threshold: Specifies maximum fragment size in bytes when transmitted over wireless medium. 802.11 standard packet (MSDU in 802.11 terminology) fragmentation allows packets to be fragmented before transmiting over wireless medium to increase probability of successful transmission (only fragments that did not transmit correctly are retransmitted). Note that transmission of fragmented packet is less efficient than transmitting unfragmented packet because of protocol overhead and increased resource usage at both - transmitting and receiving party.
        /// 
        /// integer[256..3000] | disabled
        /// </summary>
        [TikProperty("hw-fragmentation-threshold", DefaultValue = "0")]
        public string/*integer[256..3000] | disabled*/ HwFragmentationThreshold { get; set; }

        /// <summary>
        /// hw-protection-mode: Frame protection support property  read more &gt;&gt;
        /// 
        /// cts-to-self | none | rts-cts
        /// </summary>
        [TikProperty("hw-protection-mode", DefaultValue = "none")]
        public string/*cts-to-self | none | rts-cts*/ HwProtectionMode { get; set; }

        /// <summary>
        /// hw-protection-threshold: Frame protection support property read more &gt;&gt;
        /// 
        /// integer [0..65535]
        /// </summary>
        [TikProperty("hw-protection-threshold", DefaultValue = "0")]
        public int/*integer [0..65535]*/ HwProtectionThreshold { get; set; }

        /// <summary>
        /// hw-retries
        /// Number of times sending frame is retried without considering it a transmission failure.
        /// Data rate is decreased upon failure and frame is sent again. Three sequential failures on lowest supported rate suspend transmission to this destination for the duration of on-fail-retry-time. After that, frame is sent again. The frame is being retransmitted until transmission success, or until client is disconnected after disconnect-timeout. Frame can be discarded during this time if frame-lifetime is exceeded.
        /// 
        /// integer [0..15]
        /// </summary>
        [TikProperty("hw-retries", DefaultValue = "7")]
        public int/*integer [0..15]*/ HwRetries { get; set; }

        /// <summary>
        /// l2mtu: integer [0..65536]
        /// </summary>
        [TikProperty("l2mtu", DefaultValue = "1600")]
        public int/*integer [0..65536]*/ L2mtu { get; set; }

        /// <summary>
        /// mac-address: 
        /// </summary>
        [TikProperty("mac-address")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// master-interface: Name of wireless interface that has virtual-ap capability. Virtual AP interface will only work if master interface is in ap-bridge, bridge or wds-slave mode. This property is only for virtual AP interfaces.
        /// </summary>
        [TikProperty("master-interface")]
        public string MasterInterface { get; set; }

        /// <summary>
        /// max-station-count: Maximum number of associated clients. WDS links also count toward this limit.
        /// 
        /// integer [1..2007]
        /// </summary>
        [TikProperty("max-station-count", DefaultValue = "2007")]
        public int/*integer [1..2007]*/ MaxStationCount { get; set; }

        /// <summary>
        /// Mode for <see cref="Mode"/>.
        /// </summary>
        public enum WirelessMode
        {
            /// <summary>
            /// station - Basic station mode. Find and connect to acceptable AP
            /// </summary>
            [TikEnum("station")]
            Station,
            
            /// <summary>
            /// station-wds - Same as station, but create WDS link with AP, using proprietary extension. AP configuration has to allow WDS links with this device. Note that this mode does not use entries in wds.
            /// </summary>
            [TikEnum("station-wds")]
            StationWds,

            /// <summary>
            /// ap-bridge - Basic access point mode.
            /// </summary>
            [TikEnum("ap-bridge")]
            ApBridge,

            /// <summary>
            /// bridge - Same as ap-bridge, but limited to one associated client.
            /// </summary>
            [TikEnum("bridge")]
            Bridge,

            /// <summary>
            /// alignment-only - Put interface in a continuous transmit mode that is used for aiming remote antenna.
            /// </summary>
            [TikEnum("alignment-only")]
            AlignmentOnly,

            /// <summary>
            /// nstreme-dual-slave - allow this interface to be used in nstreme-dual setup.
            /// </summary>
            [TikEnum("nstreme-dual-slave")]
            NstremeDualSlave,

            /// <summary>
            /// wds-slave - Same as ap-bridge, but scan for AP with the same ssid and establishes WDS link. If this link is lost or cannot be established, then continue scanning. If dfs-mode is radar-detect, then APs with enabled hide-ssid will not be found during scanning.
            /// </summary>
            [TikEnum("wds-slave")]
            WdsSlave,

            /// <summary>
            /// station-pseudobridge - Same as station, but additionally perform MAC address translation of all traffic. Allows interface to be bridged.
            /// </summary>
            [TikEnum("station-pseudobridge")]
            StationPseudobridge,

            /// <summary>
            /// station-pseudobridge-clone - Same as station-pseudobridge, but use station-bridge-clone-mac address to connect to AP. 
            /// </summary>
            [TikEnum("station-pseudobridge-clone")]
            StationPseudobridgeClone,            

            /// <summary>
            /// ?
            /// </summary>
            [TikEnum("station-bridge")]
            StationBridge
        }

        /// <summary>
        /// mode
        /// Selection between different station and access point (AP) modes.
        /// Station modes:
        ///  station - Basic station mode. Find and connect to acceptable AP.
        ///  station-wds - Same as station, but create WDS link with AP, using proprietary extension. AP configuration has to allow WDS links with this device. Note that this mode does not use entries in wds.
        ///  station-pseudobridge - Same as station, but additionally perform MAC address translation of all traffic. Allows interface to be bridged.
        ///  station-pseudobridge-clone - Same as station-pseudobridge, but use station-bridge-clone-mac address to connect to AP. 
        /// AP modes:
        ///  ap-bridge - Basic access point mode.
        ///  bridge - Same as ap-bridge, but limited to one associated client.
        ///  wds-slave - Same as ap-bridge, but scan for AP with the same ssid and establishes WDS link. If this link is lost or cannot be established, then continue scanning. If dfs-mode is radar-detect, then APs with enabled hide-ssid will not be found during scanning.
        /// Special modes:
        ///  alignment-only - Put interface in a continuous transmit mode that is used for aiming remote antenna.
        ///  nstreme-dual-slave - allow this interface to be used in nstreme-dual setup.
        /// MAC address translation in pseudobridge modes works by inspecting packets and building table of corresponding IP and MAC addresses. All packets are sent to AP with the MAC address used by pseudobridge, and MAC addresses of received packets are restored from the address translation table. There is single entry in address translation table for all non-IP packets, hence more than one host in the bridged network cannot reliably use non-IP protocols. Note: Currently IPv6 doesn't work over Pseudobridge
        /// Virtual AP interfaces do not have this property, they follow the mode of their master interface.
        /// 
        /// station | station-wds | ap-bridge | bridge | alignment-only | nstreme-dual-slave | wds-slave | station-pseudobridge | station-pseudobridge-clone | station-bridge
        /// </summary>
        [TikProperty("mode", DefaultValue = "station")]
        public WirelessMode/*station | station-wds | ap-bridge | bridge | alignment-only | nstreme-dual-slave | wds-slave | station-pseudobridge | station-pseudobridge-clone | station-bridge*/ Mode { get; set; }

        /// <summary>
        /// mtu: [0..65536]
        /// </summary>
        [TikProperty("mtu", DefaultValue = "1500")]
        public int/*integer [0..65536]*/ Mtu { get; set; }

        /// <summary>
        /// multicast-helper
        /// When set to full multicast packets will be sent with unicast destination MAC address, resolving  multicast problem on wireless link. This option should be enabled only on access point, clients should be configured in station-bridge mode. Available starting from v5.15. 
        /// disabled - disables the helper and sends multicast packets with multicast destination MAC addresses
        /// full - all multicast packet mac address are changed to unicast mac addresses prior sending them out
        /// default - default choice that currently is set to disabled. Value can be changed in future releases.
        /// </summary>
        [TikProperty("multicast-helper", DefaultValue = "default")]
        public string/*default | disabled | full*/ MulticastHelper { get; set; }

        /// <summary>
        /// name: name of the interface
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// noise-floor-threshold: This property is only effective for cards based on AR5211 chipset.
        /// 
        /// default | integer [-128..127]
        /// </summary>
        [TikProperty("noise-floor-threshold", DefaultValue = "default")]
        public string/*default | integer [-128..127]*/ NoiseFloorThreshold { get; set; }

        /// <summary>
        /// nv2-cell-radius
        /// Setting affects the size of contention time slot that AP allocates for clients to initiate connection and also size of time slots used for estimating distance to client. When setting is too small, clients that are farther away may have trouble connecting and/or disconnect with "ranging timeout" error. Although during normal operation the effect of this setting should be negligible, in order to maintain maximum performance, it is advised to not increase this setting if not necessary, so AP is not reserving time that is actually never used, but instead allocates it for actual data transfer.
        ///  on AP: distance to farthest client in km
        ///  on station: no effect
        /// 
        /// integer [10..200]
        /// </summary>
        [TikProperty("nv2-cell-radius", DefaultValue = "30")]
        public int/*integer [10..200]*/ Nv2CellRadius { get; set; }

        /// <summary>
        /// nv2-noise-floor-offset: default | integer [0..20]
        /// </summary>
        [TikProperty("nv2-noise-floor-offset", DefaultValue = "default")]
        public string/*default | integer [0..20]*/ Nv2NoiseFloorOffset { get; set; }

        /// <summary>
        /// nv2-preshared-key: 
        /// </summary>
        [TikProperty("nv2-preshared-key")]
        public string Nv2PresharedKey { get; set; }

        /// <summary>
        /// nv2-qos
        /// Sets the packet priority mechanism, firstly data from high priority queue is sent, then lower queue priority data until 0 queue priority is reached. When link is full with high priority queue data, lower priority data is not sent. Use it very carefully, setting works on AP 
        ///  frame-priority - manual setting that can be tuned with Mangle rules. 
        ///  default - default setting where small packets receive priority for best latency
        /// </summary>
        [TikProperty("nv2-qos", DefaultValue = "default")]
        public string/*default | frame-priority*/ Nv2Qos { get; set; }

        /// <summary>
        /// nv2-queue-count: 
        /// </summary>
        [TikProperty("nv2-queue-count", DefaultValue = "2")]
        public string/*integer [2..8]*/ Nv2QueueCount { get; set; }

        /// <summary>
        /// nv2-security: disabled | enabled
        /// </summary>
        [TikProperty("nv2-security", DefaultValue = "disabled")]
        public string/*disabled | enabled*/ Nv2Security { get; set; }

        /// <summary>
        /// on-fail-retry-time: After third sending failure on the lowest data rate, wait for specified time interval before retrying.
        /// 
        /// time [100ms..1s]
        /// </summary>
        [TikProperty("on-fail-retry-time", DefaultValue = "100ms")]
        public string/*time [100ms..1s]*/ OnFailRetryTime { get; set; }

        /// <summary>
        /// periodic-calibration
        /// Setting default enables periodic calibration if  info default-periodic-calibration property is enabled. Value of that property depends on the type of wireless card.
        /// This property is only effective for cards based on Atheros chipset.
        /// 
        /// default | disabled | enabled
        /// </summary>
        [TikProperty("periodic-calibration", DefaultValue = "default")]
        public string/*default | disabled | enabled*/ PeriodicCalibration { get; set; }

        /// <summary>
        /// periodic-calibration-interval: This property is only effective for cards based on Atheros chipset.
        /// 
        /// [1..10000]
        /// </summary>
        [TikProperty("periodic-calibration-interval", DefaultValue = "60")]
        public int/*integer [1..10000]*/ PeriodicCalibrationInterval { get; set; }

        /// <summary>
        /// Mode for <see cref="PreambleMode"/>.
        /// </summary>
        public enum WirelessPreambleMode
        {
            /// <summary>
            /// both - Use short preamble if AP supports it.
            /// </summary>
            [TikEnum("both")]
            Both,

            /// <summary>
            /// long - do not use short preamble.
            /// </summary>
            [TikEnum("long")]
            Long,

            /// <summary>
            /// short - do not connect to AP if it does not support short preamble.
            /// </summary>
            [TikEnum("short")]
            Short,
        }

        /// <summary>
        /// preamble-mode
        /// Short preamble mode is an option of 802.11b standard that reduces per-frame overhead.
        ///  On AP:
        ///  long - Do not use short preamble.
        ///  short - Announce short preamble capability. Do not accept connections from clients that do not have this capability.
        ///  both - Announce short preamble capability.
        ///  On station:
        ///  long - do not use short preamble.
        ///  short - do not connect to AP if it does not support short preamble.
        ///  both - Use short preamble if AP supports it.
        /// </summary>
        [TikProperty("preamble-mode", DefaultValue = "both")]
        public WirelessPreambleMode/*both | long | short*/ PreambleMode { get; set; }

        /// <summary>
        /// prism-cardtype: Specify type of the installed Prism wireless card.
        /// 
        /// 100mW | 200mW | 30mW
        /// </summary>
        [TikProperty("prism-cardtype")]
        public string/*100mW | 200mW | 30mW*/ PrismCardtype { get; set; }

        /// <summary>
        /// proprietary-extension
        /// RouterOS includes proprietary information in an information element of management frames. This parameter controls how this information is included.
        ///  pre-2.9.25 - This is older method. It can interoperate with newer versions of RouterOS. This method is incompatible with some clients, for example, Centrino based ones.
        ///  post-2.9.25 - This uses standardized way of including vendor specific information, that is compatible with newer wireless clients.
        /// </summary>
        [TikProperty("proprietary-extension", DefaultValue = "post-2.9.25")]
        public string/*post-2.9.25 | pre-2.9.25*/ ProprietaryExtension { get; set; }

        /// <summary>
        /// radio-name
        /// Descriptive name of the device, that is shown in registration table entries on the remote devices.
        /// This is a proprietary extension.
        /// </summary>
        [TikProperty("radio-name", DefaultValue = "MAC address of an interface")]
        public string RadioName { get; set; }

        /// <summary>
        /// rate-selection: Starting from v5.9 default value is advanced since legacy mode was inefficient.
        /// 
        /// advanced | legacy
        /// </summary>
        [TikProperty("rate-selection", DefaultValue = "advanced")]
        public string/*advanced | legacy*/ RateSelection { get; set; }

        /// <summary>
        /// rate-set
        /// Two options are available:
        ///  default - default basic and supported rate sets are used. Values from basic-rates and supported-rates parameters have no effect.
        ///  configured - use values from basic-rates, supported-rates, basic-mcs, mcs.  Read more &gt;&gt;.
        /// </summary>
        [TikProperty("rate-set", DefaultValue = "default")]
        public string/*configured | default*/ RateSet { get; set; }

        /// <summary>
        /// scan-list
        /// The default value is all channels from selected band that are supported by card and allowed by the country and frequency-mode settings (this list can be seen in  info). For default scan list in 5ghz band channels are taken with 20MHz step, in 5ghz-turbo band - with 40MHz step, for all other bands - with 5MHz step. If scan-list is specified manually, then all matching channels are taken. (Example: scan-list=default,5200-5245,2412-2427 - This will use the default value of scan list for current band, and add to it supported frequencies from 5200-5245 or 2412-2427 range.) 
        /// Since RouterOS v6.0 with Winbox or Webfig, for inputting of multiple frequencies, add each frequency or range of frequencies into separate multiple scan-lists. Using a comma to separate frequencies is no longer supported in Winbox/Webfig since v6.0.
        /// 
        /// Comma separated list of frequencies and frequency ranges | default
        /// </summary>
        [TikProperty("scan-list", DefaultValue = "default")]
        public string/*Comma separated list of frequencies and frequency ranges | default*/ ScanList { get; set; }

        /// <summary>
        /// security-profile: Name of profile from  security-profiles
        /// </summary>
        [TikProperty("security-profile", DefaultValue = "default")]
        public string SecurityProfile { get; set; }

        /// <summary>
        /// ssid: SSID (service set identifier) is a name that identifies wireless network.
        /// </summary>
        [TikProperty("ssid", DefaultValue = "value of system/identity")]
        public string/*string (0..32 chars)*/ Ssid { get; set; }

        /// <summary>
        /// station-bridge-clone-mac
        /// This property has effect only in the station-pseudobridge-clone mode.
        /// Use this MAC address when connection to AP. If this value is 00:00:00:00:00:00, station will initially use MAC address of the wireless interface.
        /// As soon as packet with MAC address of another device needs to be transmitted, station will reconnect to AP using that address.
        /// </summary>
        [TikProperty("station-bridge-clone-mac")]
        public string/*MAC*/ StationBridgeCloneMac { get; set; }

        /// <summary>
        /// supported-rates-a/g: List of supported rates, used for all bands except  2ghz-b.
        /// 
        /// (list of rates [12Mbps | 18Mbps | 24Mbps | 36Mbps | 48Mbps | 54Mbps | 6Mbps | 9Mbps]; Default: 6Mbps; 9Mbps; 12Mbps; 18Mbps; 24Mbps; 36Mbps; 48Mbps; 54Mbps)
        /// </summary>
        [TikProperty("supported-rates-a/g")]
        public string /*supported-rates-a/g (list of rates [12Mbps | 18Mbps | 24Mbps | 36Mbps | 48Mbps | 54Mbps | 6Mbps | 9Mbps]; Default: 6Mbps; 9Mbps; 12Mbps; 18Mbps; 24Mbps; 36Mbps; 48Mbps; 54Mbps)*/ SupportedRatesAG { get; set; }

        /// <summary>
        /// supported-rates-b: List of supported rates, used for 2ghz-b, 2ghz-b/g and 2ghz-b/g/n bands. Two devices will communicate only using rates that are supported by both devices. This property has effect only when value of rate-set is configured.
        /// 
        /// list of rates [11Mbps | 1Mbps | 2Mbps | 5.5Mbps]
        /// </summary>
        [TikProperty("supported-rates-b", DefaultValue = "1Mbps; 2Mbps; 5.5Mbps; 11Mbps")]
        public string/*list of rates [11Mbps | 1Mbps | 2Mbps | 5.5Mbps]*/ SupportedRatesB { get; set; }

        /// <summary>
        /// tdma-debug: [0..4294967295]
        /// </summary>
        [TikProperty("tdma-debug", DefaultValue = "0")]
        public long/*integer [0..4294967295]*/ TdmaDebug { get; set; }

        /// <summary>
        /// tdma-hw-test-mode: integer [0..4294967295]
        /// </summary>
        [TikProperty("tdma-hw-test-mode")]
        public long/*integer [0..4294967295]*/ TdmaHwTestMode { get; set; }

        /// <summary>
        /// tdma-override-rate: 12mbps | 18mbps | 24mbps | 36mbps | 48mbps | 54mbps | 6mbps | 9mbps | disabled | ht20-mcs... | ht40-mcs...
        /// </summary>
        [TikProperty("tdma-override-rate", DefaultValue = "disabled")]
        public string/*12mbps | 18mbps | 24mbps | 36mbps | 48mbps | 54mbps | 6mbps | 9mbps | disabled | ht20-mcs... | ht40-mcs...*/ TdmaOverrideRate { get; set; }

        /// <summary>
        /// tdma-override-size: integer [0..4294967295]
        /// </summary>
        [TikProperty("tdma-override-size")]
        public long/*integer [0..4294967295]*/ TdmaOverrideSize { get; set; }

        /// <summary>
        /// tdma-period-size: Specifies TDMA period in milliseconds. It could help on the longer distance links, it could slightly increase bandwidth, while latency is increased too.
        /// 
        /// integer [1..10]
        /// </summary>
        [TikProperty("tdma-period-size", DefaultValue = "2")]
        public int/*integer [1..10]*/ TdmaPeriodSize { get; set; }

        /// <summary>
        /// tdma-test-mode: integer [0..4294967295]
        /// </summary>
        [TikProperty("tdma-test-mode", DefaultValue = "0")]
        public long/*integer [0..4294967295]*/ TdmaTestMode { get; set; }

        /// <summary>
        /// tx-power: For 802.11ac wireless interface it's total power but for 802.11a/b/g/n it's power per chain.
        /// 
        ///  [-30..30]
        /// </summary>
        [TikProperty("tx-power")]
        public int/*integer [-30..30]*/ TxPower { get; set; }

        /// <summary>
        /// Power mode for <see cref="TxPowerMode"/>
        /// </summary>
        public enum WirelessTxPowerMode
        {
            /// <summary>
            /// default - use values stored in the card
            /// </summary>
            [TikEnum("default")]
            Default,

            /// <summary>
            /// card-rates - use transmit power as defined by tx-power setting 
            /// </summary>
            [TikEnum("card-rates")]
            CardRates,

            /// <summary>
            /// all-rated-fixed - use same transmit power for all data rates. Can damage the card if transmit power is set above rated value of the card for used rate
            /// </summary>
            [TikEnum("all-rated-fixed")]
            AllRatesFixed,

            /// <summary>
            /// manual-table - define transmit power for each rate separately. Can damage the card if transmit power is set above rated value of the card for used rate.
            /// </summary>
            [TikEnum("manual-table")]
            ManualTable,                     
        }

        /// <summary>
        /// tx-power-mode
        /// sets up tx-power mode for wireless card
        ///  default - use values stored in the card
        ///  card-rates - use transmit power as defined by tx-power setting 
        ///  all-rated-fixed - use same transmit power for all data rates. Can damage the card if transmit power is set above rated value of the card for used rate
        ///  manual-table - define transmit power for each rate separately. Can damage the card if transmit power is set above rated value of the card for used rate.
        /// </summary>
        [TikProperty("tx-power-mode", DefaultValue = "default")]
        public WirelessTxPowerMode/*default, card-rates, all-rated-fixed, manual-table*/ TxPowerMode { get; set; }

        /// <summary>
        /// update-stats-interval
        /// How often to request update of signals strength and ccq values from clients.
        /// Access to  registration-table also triggers update of these values.
        /// This is proprietary extension.
        /// </summary>
        [TikProperty("update-stats-interval")]
        public string UpdateStatsInterval { get; set; }

        /// <summary>
        /// vht-basic-mcs
        /// Modulation and Coding Schemes that every connecting client must support. Refer to 802.11ac for MCS specification.
        /// You can set MCS interval for each of Spatial Stream
        ///  none - will not use selected Spatial Stream
        ///  MCS 0-7 - client must support MCS-0 to MCS-7
        ///  MCS 0-8 - client must support MCS-0 to MCS-8 
        ///  MCS 0-9 - client must support MCS-0 to MCS-9
        /// </summary>
        [TikProperty("vht-basic-mcs", DefaultValue = "MCS 0-7")]
        public string/*none | MCS 0-7 | MCS 0-8 | MCS 0-9*/ VhtBasicMcs { get; set; }

        /// <summary>
        /// vht-supported-mcs
        /// Modulation and Coding Schemes that this device advertises as supported. Refer to 802.11ac for MCS specification.
        /// You can set MCS interval for each of Spatial Stream
        ///  none - will not use selected Spatial Stream
        ///  MCS 0-7 - devices will advertise as supported MCS-0 to MCS-7
        ///  MCS 0-8 - devices will advertise as supported MCS-0 to MCS-8 
        ///  MCS 0-9 - devices will advertise as supported MCS-0 to MCS-9
        /// </summary>
        [TikProperty("vht-supported-mcs", DefaultValue = "MCS 0-9")]
        public string/*none | MCS 0-7 | MCS 0-8 | MCS 0-9*/ VhtSupportedMcs { get; set; }

        /// <summary>
        /// wds-cost-range
        /// Bridge port cost of WDS links are automatically adjusted, depending on measured link throughput. Port cost is recalculated and adjusted every 5 seconds if it has changed by more than 10%, or if more than 20 seconds have passed since the last adjustment.
        /// Setting this property to 0 disables  automatic cost adjustment.
        /// Automatic adjustment does not work for WDS links that are manually configured as a bridge port.
        /// </summary>
        [TikProperty("wds-cost-range", DefaultValue = "50-150")]
        public string/*start [-end] integer[0..4294967295]*/ WdsCostRange { get; set; }

        /// <summary>
        /// wds-default-bridge: When WDS link is established and status of the wds interface becomes running, it will be added as a bridge port to the bridge interface specified by this property. When WDS link is lost, wds interface is removed from the bridge. If wds interface is already included in a bridge setup when WDS link becomes active, it will not be added to bridge specified by , and will (needs editing)
        /// </summary>
        [TikProperty("wds-default-bridge", DefaultValue = "none")]
        public string/*string | none*/ WdsDefaultBridge { get; set; }

        /// <summary>
        /// wds-default-cost: Initial bridge port cost of the WDS links.
        /// </summary>
        [TikProperty("wds-default-cost", DefaultValue = "100")]
        public string/*integer [0..4294967295]*/ WdsDefaultCost { get; set; }

        /// <summary>
        /// wds-ignore-ssid: By default, WDS link between two APs can be created only when they work on the same frequency and have the same SSID value. If this property is set to yes, then SSID of the remote AP will not be checked. This property has no effect on connections from clients in station-wds mode. It also does not work if wds-mode is static-mesh or dynamic-mesh.
        /// </summary>
        [TikProperty("wds-ignore-ssid", DefaultValue = "no")]
        public bool WdsIgnoreSsid { get; set; }

        /// <summary>
        /// wds-mode
        /// Controls how WDS links with other devices (APs and clients in station-wds mode) are established.
        ///  disabled does not allow WDS links.
        ///  static only allows WDS links that are manually configured in wds
        ///  dynamic also allows WDS links with devices that are not configured in wds, by creating required entries dynamically. Such dynamic WDS entries are removed automatically after the connection with the other AP is lost.
        /// -mesh modes use different (better) method for establishing link between AP, that is not compatible with APs in non-mesh mode. This method avoids one-sided WDS links that are created only by one of the two APs. Such links cannot pass any data.
        /// When AP or station is establishing WDS connection with another AP, it uses  connect-list to check whether this connection is allowed. If station in station-wds mode is establishing connection with AP, AP uses  access-list to check whether this connection is allowed.
        /// If mode is station-wds, then this property has no effect.
        /// 
        /// disabled  | dynamic | dynamic-mesh | static | static-mesh
        /// </summary>
        [TikProperty("wds-mode", DefaultValue = "disabled")]
        public string/*disabled  | dynamic | dynamic-mesh | static | static-mesh*/ WdsMode { get; set; }

        /// <summary>
        /// Options for <see cref="WirelessProtocol"/>.
        /// </summary>
        public enum WirelessWirelessProtocol
        {
            /// <summary>
            /// unspecified - protocol mode used on previous RouterOS versions (v3.x, v4.x). Nstreme is enabled by old enable-nstreme setting, Nv2 configuration is not possible.
            /// </summary>
            [TikEnum("unspecified")]
            Unspecified,

            /// <summary>
            /// any on AP - regular 802.11 Access Point or Nstreme Access Point; on station - selects Access Point without specific sequence, it could be changed by connect-list rules.
            /// </summary>
            [TikEnum("any")]
            Any,

            /// <summary>
            /// nstreme - enables Nstreme protocol (the same as old enable-nstreme setting).
            /// </summary>
            [TikEnum("nstreme")]
            NStreme,

            /// <summary>
            ///  nv2 - enables Nv2 protocol.
            /// </summary>
            [TikEnum("nv2")]
            NV2,


            /// <summary>
            /// nv2-nstreme: on AP - uses first wireless-protocol setting, always Nv2; on station - searches for Nv2 Access Point, then for Nstreme Access Point.
            /// </summary>
            [TikEnum("nv2-nstreme")]
            NV2Nstreme,

            /// <summary>
            /// nv2 nstreme 802.11 - on AP - uses first wireless-protocol setting, always Nv2; on station - searches for Nv2 Access Point, then for Nstreme Access Point, then for regular 802.11 Access Point.
            /// </summary>
            [TikEnum("nv2-nstreme-802.11")]
            NV2NStreme80211,

            /// <summary>
            /// ?
            /// </summary>
            [TikEnum("802.11")]
            Plain80211,            
        }

        /// <summary>
        /// wireless-protocol
        /// Specifies protocol used on wireless interface; 
        ///  unspecified - protocol mode used on previous RouterOS versions (v3.x, v4.x). Nstreme is enabled by old enable-nstreme setting, Nv2 configuration is not possible.
        ///  any on AP - regular 802.11 Access Point or Nstreme Access Point; on station - selects Access Point without specific sequence, it could be changed by connect-list rules.
        ///  nstreme - enables Nstreme protocol (the same as old enable-nstreme setting).
        ///  nv2 - enables Nv2 protocol.
        ///  nv2-nstreme: on AP - uses first wireless-protocol setting, always Nv2; on station - searches for Nv2 Access Point, then for Nstreme Access Point.
        ///  nv2-nstreme 802.11 - on AP - uses first wireless-protocol setting, always Nv2; on station - searches for Nv2 Access Point, then for Nstreme Access Point, then for regular 802.11 Access Point.
        /// Warning! Nv2 doesn't have support for Virtual AP
        /// </summary>
        [TikProperty("wireless-protocol", DefaultValue = "unspecified")]
        public WirelessWirelessProtocol/*802.11 | any | nstreme | nv2 | nv2-nstreme | nv2-nstreme-802.11 | unspecified*/ WirelessProtocol { get; set; }

        /// <summary>
        /// wmm-support: Specifies whether to enable  WMM.
        /// 
        /// disabled | enabled | required
        /// </summary>
        [TikProperty("wmm-support", DefaultValue = "disabled")]
        public string/*disabled | enabled | required*/ WmmSupport { get; set; }
    }
}
