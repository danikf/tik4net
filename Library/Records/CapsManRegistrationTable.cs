using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /caps-man/registration-table: In the registration table you can see various information about currently connected clients. It is used only for Controlled Access Points. All properties are read-only.
    /// </summary>
    [RosRecord("/caps-man/registration-table", IsReadOnly = true)]
    public class CapsManRegistrationTable  : SetRecordBase {
        /// <summary>
        /// mac-address: MAC address of the registered client
        /// </summary>
        [RosProperty("mac-address",IsReadOnly = true)]
        public string MACAddress { get; set; }

        /// <summary>
        /// interface: Name of the wireless interface to which wireless client is associated
        /// </summary>
        [RosProperty("interface",IsReadOnly = true)]
        public string Interface { get; set; }

        /// <summary>
        /// uptime: time the client is associated with the access point
        /// </summary>
        [RosProperty("uptime",IsReadOnly = true)]
        public TimeSpan Uptime { get; set; }

        /// <summary>
        /// ssid: SSID (service set identifier) is a name that identifies wireless Option.
        /// </summary>
        [RosProperty("ssid",IsReadOnly = true)]
        public string SSID { get; set; }

        /// <summary>
        /// tx-rate: transmit data rate
        /// </summary>
        [RosProperty("tx-rate",IsReadOnly = true)]
        public string TxRate { get; private set; }

        /// <summary>
        /// tx-rate-set: 
        /// </summary>
        [RosProperty("tx-rate-set",IsReadOnly = true)]
        public string TxRateSet { get; private set; }

        /// <summary>
        /// rx-rate: receive data rate
        /// </summary>
        [RosProperty("rx-rate",IsReadOnly = true)]
        public string RxRate { get; set; }

        /// <summary>
        /// signal-strength: average strength of the client signal recevied by the AP
        /// </summary>
        [RosProperty("rx-signal",IsReadOnly = true)]
        public int Signal { get; set; }

        /// <summary>
        /// packets: number of sent and received Option layer packets
        /// </summary>
        [RosProperty("packets",IsReadOnly = true)]
        public string Packets { get; set; }

        /// <summary>
        /// bytes: number of sent and received packet bytes
        /// </summary>
        [RosProperty("bytes",IsReadOnly = true)]
        public string Bytes { get; set; }

        /// <summary>
        /// comment: Description of an entry. comment is taken from appropriate Access List entry if specified.
        /// </summary>
        [RosProperty("comment",IsReadOnly = true)]
        public string Comment { get; set; }
    }
}
