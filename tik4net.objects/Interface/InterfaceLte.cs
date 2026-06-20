using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface/lte
    /// LTE (Long-Term Evolution) client interface for cellular modem hardware.
    /// Interfaces are created automatically when an LTE/5G modem is detected — they cannot be
    /// added manually. Use LoadAll to enumerate available modems; use Save to update settings.
    /// See https://help.mikrotik.com/docs/display/ROS/LTE
    /// </summary>
    [TikEntity("/interface/lte", IncludeDetails = true)]
    public class InterfaceLte
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — Interface name.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>mtu — Maximum Transmit Unit in bytes. Default: 1500. DefaultValue="0" prevents sending 0 on set.</summary>
        [TikProperty("mtu", DefaultValue = "0")]
        public int Mtu { get; set; }

        /// <summary>mac-address — MAC address of the LTE interface (read-only, assigned by modem).</summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; private set; }

        /// <summary>apn-profiles — APN profile(s) to use for data connection.</summary>
        [TikProperty("apn-profiles", DefaultValue = "")]
        public string ApnProfiles { get; set; }

        /// <summary>allow-roaming — Allow the modem to use a roaming data connection. Default: no.</summary>
        [TikProperty("allow-roaming", DefaultValue = "no")]
        public bool AllowRoaming { get; set; }

        /// <summary>band — LTE frequency bands to use (comma-separated band numbers, e.g. "3,7,20"). Empty means all bands.</summary>
        [TikProperty("band", DefaultValue = "")]
        public string Band { get; set; }

        /// <summary>nr-band — 5G NR frequency bands to use (comma-separated). Empty means all bands.</summary>
        [TikProperty("nr-band", DefaultValue = "")]
        public string NrBand { get; set; }

        public enum NetworkModeType
        {
            /// <summary>auto — Automatically select the best available network mode.</summary>
            [TikEnum("auto")] Auto,
            /// <summary>3g — Force 3G (WCDMA/HSPA) only.</summary>
            [TikEnum("3g")] ThreeG,
            /// <summary>gsm — Force 2G (GSM/GPRS/EDGE) only.</summary>
            [TikEnum("gsm")] Gsm,
            /// <summary>lte — Force 4G LTE only.</summary>
            [TikEnum("lte")] Lte,
            /// <summary>5g — Force 5G NR only.</summary>
            [TikEnum("5g")] FiveG,
        }

        /// <summary>network-mode — Preferred cellular network technology. Default: auto.</summary>
        /// <seealso cref="NetworkModeType"/>
        [TikProperty("network-mode", DefaultValue = "auto")]
        public NetworkModeType NetworkMode { get; set; }

        /// <summary>operator — Operator PLMN code for manual operator selection. Empty for automatic.</summary>
        [TikProperty("operator", DefaultValue = "")]
        public string Operator { get; set; }

        /// <summary>pin — SIM card PIN code. Leave empty if no PIN is required.</summary>
        [TikProperty("pin", DefaultValue = "")]
        public string Pin { get; set; }

        /// <summary>modem-init — AT command string sent to the modem at initialization.</summary>
        [TikProperty("modem-init", DefaultValue = "")]
        public string ModemInit { get; set; }

        public enum SmsProtocolType
        {
            /// <summary>auto — Automatically detect SMS protocol.</summary>
            [TikEnum("auto")] Auto,
            /// <summary>3gpp — 3GPP (GSM/UMTS/LTE) SMS protocol.</summary>
            [TikEnum("3gpp")] ThreeGpp,
            /// <summary>3gpp2 — 3GPP2 (CDMA) SMS protocol.</summary>
            [TikEnum("3gpp2")] ThreeGpp2,
        }

        /// <summary>sms-protocol — SMS signaling protocol. Default: auto.</summary>
        /// <seealso cref="SmsProtocolType"/>
        [TikProperty("sms-protocol", DefaultValue = "auto")]
        public SmsProtocolType SmsProtocol { get; set; }

        /// <summary>sms-read — Whether to read incoming SMS messages. Default: no.</summary>
        [TikProperty("sms-read", DefaultValue = "no")]
        public bool SmsRead { get; set; }

        /// <summary>running — Whether the LTE interface is connected and running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — Short description of the interface.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
