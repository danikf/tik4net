using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Wireless
{
    /// <summary>
    /// Access list is used by access point to restrict allowed connections from other devices, and to control connection parameters.
    /// Operation:
    ///     Access list rules are checked sequentially.
    ///     Disabled rules are always ignored.
    ///     Only the first matching rule is applied.
    ///     If there are no matching rules for the remote connection, then the default values from the wireless interface configuration are used.
    ///     If remote device is matched by rule that has authentication = no value, the connection from that remote device is rejected.
    /// </summary>
    [TikEntity("/interface/wireless/access-list")]
    public class WirelessAccessList
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// ap-tx-limit: Limit rate of data transmission to this client. Value 0 means no limit. Value is in bits per second.
        /// integer [0..4294967295]
        /// </summary>
        [TikProperty("ap-tx-limit", DefaultValue = "0", WinboxLabel = "AP Tx Limit")]
        public TikValue<long?> ApTxLimit { get; set; }

        /// <summary>
        /// authentication
        /// .
        ///  no - Client association will always fail.
        ///  yes - Use authentication procedure that is specified in the  security-profile of the interface.
        /// </summary>
        [TikProperty("authentication", DefaultValue = "yes", WinboxLabel = "Authentication")]
        public TikValue<bool?> Authentication { get; set; }

        /// <summary>
        /// client-tx-limit
        /// Ask client to limit rate of data transmission. Value 0 means no limit.
        /// This is a proprietary extension that is supported by RouterOS clients.
        /// Value is in bits per second.
        /// integer [0..4294967295]
        /// </summary>
        [TikProperty("client-tx-limit", DefaultValue = "0", WinboxLabel = "Client Tx Limit")]
        public TikValue<long?> ClientTxLimit { get; set; }

        /// <summary>
        /// comment: Short description of an entry
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>
        /// forwarding
        /// .
        ///  no - Client cannot send frames to other station that are connected to same access point.
        ///  yes - Client can send frames to other stations on the same access point.
        /// </summary>
        [TikProperty("forwarding", DefaultValue = "yes", WinboxLabel = "Forwarding")]
        public TikValue<bool?> Forwarding { get; set; }

        /// <summary>
        /// interface: Rules with interface=all are used for all wireless interfaces. To make rule that applies only to one wireless interface, specify that interface as a value of this property.
        /// </summary>
        [TikProperty("interface", DefaultValue = "all", WinboxLabel = "Interface")]
        public TikValue<string?> Interface { get; set; }

        /// <summary>
        /// mac-address: Rule matches client with the specified MAC address. Value 00:00:00:00:00:00 matches always.
        /// </summary>
        [TikProperty("mac-address", DefaultValue = "00:00:00:00:00:00", WinboxLabel = "MAC Address")]
        public TikValue<string?> MacAddress { get; set; }

        /// <summary>
        /// management-protection-key: 
        /// </summary>
        [TikProperty("management-protection-key", DefaultValue = "", IsSensitive = true, WinboxLabel = "Management Protection Key")]
        public TikValue<string?> ManagementProtectionKey { get; set; }

        /// <summary>
        /// private-algo: Only for WEP modes.
        /// </summary>
        [TikProperty("private-algo", DefaultValue = "none")]
        public TikValue<string?> PrivateAlgo { get; set; }

        /// <summary>
        /// private-key: Only for WEP modes.
        /// </summary>
        [TikProperty("private-key", DefaultValue = "", IsSensitive = true)]
        public TikValue<string?> PrivateKey { get; set; }

        /// <summary>
        /// private-pre-shared-key: Used in WPA PSK mode.
        /// </summary>
        [TikProperty("private-pre-shared-key", DefaultValue = "", IsSensitive = true, WinboxLabel = "Private Pre Shared Key")]
        public TikValue<string?> PrivatePreSharedKey { get; set; }

        /// <summary>
        /// signal-range
        /// Rule matches if signal strength of the station is within the range.
        /// If signal strength of the station will go out of the range that is specified in the rule, access point will disconnect that station.
        /// </summary>
        [TikProperty("signal-range", DefaultValue = "-120..120")]
        public TikValue<string?> SignalRange { get; set; }

        /// <summary>
        /// time
        /// Rule will match only during specified time.
        /// Station will be disconnected after specified time ends.
        /// Both start and end time is expressed as time since midnight, 00:00.
        /// Rule will match only during specified days of the week.
        /// </summary>
        [TikProperty("time", WinboxLabel = "Time")]
        public TikValue<string?> Time { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public WirelessAccessList()
        {
            SignalRange = "-120..120";
            PrivateAlgo = "none";
            Interface = "all";
        }
    }
}
