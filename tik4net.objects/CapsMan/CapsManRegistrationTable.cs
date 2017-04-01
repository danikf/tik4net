using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.CapsMan
{
    /// <summary>
    /// /caps-man/registration-table: In the registration table you can see various information about currently connected clients. It is used only for Controlled Access Points. All properties are read-only.
    /// </summary>
    [TikEntity("caps-man/registration-table", IsReadOnly = true)]
    public class CapsManRegistrationTable
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; set; }

        /// <summary>
        /// mac-address: MAC address of the registered client
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MACAddress { get; set; }

        /// <summary>
        /// interface: Name of the wireless interface to which wireless client is associated
        /// </summary>
        [TikProperty("interface", IsReadOnly = true)]
        public string Interface { get; set; }

        /// <summary>
        /// uptime: time the client is associated with the access point
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public TimeSpan Uptime { get; set; }

        /// <summary>
        /// ssid: SSID (service set identifier) is a name that identifies wireless network.
        /// </summary>
        [TikProperty("ssid", IsReadOnly = true)]
        public string SSID { get; set; }

        /// <summary>
        /// tx-rate: transmit data rate
        /// </summary>
        [TikProperty("tx-rate", IsReadOnly = true)]
        public string TxRate { get; private set; }

        /// <summary>
        /// tx-rate-set: 
        /// </summary>
        [TikProperty("tx-rate-set", IsReadOnly = true)]
        public string TxRateSet { get; private set; }

        /// <summary>
        /// rx-rate: receive data rate
        /// </summary>
        [TikProperty("rx-rate", IsReadOnly = true)]
        public string RxRate { get; set; }

        /// <summary>
        /// signal-strength: average strength of the client signal recevied by the AP
        /// </summary>
        [TikProperty("rx-signal", IsReadOnly = true)]
        public int Signal { get; set; }

        /// <summary>
        /// packets: number of sent and received network layer packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public string Packets { get; set; }

        /// <summary>
        /// bytes: number of sent and received packet bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public string Bytes { get; set; }

        /// <summary>
        /// comment: Description of an entry. comment is taken from appropriate Access List entry if specified.
        /// </summary>
        [TikProperty("comment", IsReadOnly = true)]
        public string Comment { get; set; }
    }
}
