
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Access list is used by access point to restrict allowed connections from other devices, and to control connection parameters.
    /// Operation:
    ///     Access list rules are checked sequentially.
    ///     Disabled rules are always ignored.
    ///     Only the first matching rule is applied.
    ///     If there are no matching rules for the remote connection, then the default values from the wireless interface configuration are used.
    ///     If remote device is matched by rule that has authentication = no value, the connection from that remote device is rejected.
    /// </summary>
    [RosRecord("/interface/wireless/access-list")]
    public class InterfaceWirelessAccessList : SetRecordBase {
        /// <summary>
        /// ap-tx-limit: Limit rate of data transmission to this client. Value 0 means no limit. Value is in bits per second.
        /// integer [0..4294967295]
        /// </summary>
        [RosProperty("ap-tx-limit")]
        public long? ApTxLimit { get; set; }

        /// <summary>
        /// authentication
        /// .
        ///  no - Client association will always fail.
        ///  yes - Use authentication procedure that is specified in the  security-profile of the interface.
        /// </summary>
        [RosProperty("authentication")]
        public bool Authentication { get; set; } = true;

        /// <summary>
        /// client-tx-limit
        /// Ask client to limit rate of data transmission. Value 0 means no limit.
        /// This is a proprietary extension that is supported by RouterOS clients.
        /// Value is in bits per second.
        /// integer [0..4294967295]
        /// </summary>
        [RosProperty("client-tx-limit")]
        public long? ClientTxLimit { get; set; }

        /// <summary>
        /// comment: Short description of an entry
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// forwarding
        /// .
        ///  no - Client cannot send frames to other station that are connected to same access point.
        ///  yes - Client can send frames to other stations on the same access point.
        /// </summary>
        [RosProperty("forwarding")]
        public bool Forwarding { get; set; } = true;

        /// <summary>
        /// interface: Rules with interface=all are used for all wireless interfaces. To make rule that applies only to one wireless interface, specify that interface as a value of this property.
        /// </summary>
        [RosProperty("interface")]
        public string/*string | all*/ Interface { get; set; } = "all";

        /// <summary>
        /// mac-address: Rule matches client with the specified MAC address. Value 00:00:00:00:00:00 matches always.
        /// </summary>
        [RosProperty("mac-address")]
        public string/*MAC*/ MacAddress { get; set; } = "00:00:00:00:00:00";

        /// <summary>
        /// management-protection-key: 
        /// </summary>
        [RosProperty("management-protection-key")]
        public string ManagementProtectionKey { get; set; } = "";

        /// <summary>
        /// private-algo: Only for WEP modes.
        /// </summary>
        [RosProperty("private-algo")]
        public string/*104bit-wep | 40bit-wep | aes-ccm | none | tkip*/ PrivateAlgo { get; set; } = "none"; // TODO: Make enum

        /// <summary>
        /// private-key: Only for WEP modes.
        /// </summary>
        [RosProperty("private-key")]
        public string PrivateKey { get; set; } = "";

        /// <summary>
        /// private-pre-shared-key: Used in WPA PSK mode.
        /// </summary>
        [RosProperty("private-pre-shared-key")]
        public string PrivatePreSharedKey { get; set; } = "";

        /// <summary>
        /// signal-range
        /// Rule matches if signal strength of the station is within the range.
        /// If signal strength of the station will go out of the range that is specified in the rule, access point will disconnect that station.
        /// </summary>
        [RosProperty("signal-range")]
        public string/*NUM..NUM - both NUM are numbers in the range -120..120*/ SignalRange { get; set; } = "-120..120";

        /// <summary>
        /// time
        /// Rule will match only during specified time.
        /// Station will be disconnected after specified time ends.
        /// Both start and end time is expressed as time since midnight, 00:00.
        /// Rule will match only during specified days of the week.
        /// </summary>
        [RosProperty("time")]
        public string/*TIME-TIME,sun,mon,tue,wed,thu,fri,sat - TIME is time interval 0..86400 seconds; all day names are optional; value can be unset*/ Time { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public InterfaceWirelessAccessList() {
            SignalRange = "-120..120";
            PrivateAlgo = "none";
            Interface = "all";
        }
    }
}
