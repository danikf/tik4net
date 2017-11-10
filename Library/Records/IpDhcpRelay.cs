
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// ip/dhcp relay
    /// DHCP Relay is just a proxy that is able to receive a DHCP request and resend it to the real DHCP server.
    /// 
    /// </summary>
    [RosRecord("/ip/dhcp-relay")]
    public class IpDhcpRelay : SetRecordBase {
        /// <summary>
        /// add-relay-info: Adds DHCP relay agent information if enabled according to RFC 3046.  Agent Circuit ID Sub-option contains mac address of an interface, Agent Remote ID Sub-option contains MAC address of the client from which request was received.
        /// </summary>
        [RosProperty("add-relay-info")]
        public string AddRelayInfo { get; set; } = "no";

        /// <summary>
        /// delay-threshold: If secs field in DHCP packet is smaller than delay-threshold, then this packet is ignored
        /// </summary>
        [RosProperty("delay-threshold")]
        public string DelayThreshold { get; set; } = "none";

        /// <summary>
        /// dhcp-server: List of DHCP servers' IP addresses which should the DHCP requests be forwarded to
        /// </summary>
        [RosProperty("dhcp-server")]
        public string DhcpServer { get; set; }

        /// <summary>
        /// interface: Interface name the DHCP relay will be working on.
        /// </summary>
        [RosProperty("interface")]
        public string Interface { get; set; }

        /// <summary>
        /// local-address: The unique IP address of this DHCP relay needed for DHCP server to distinguish relays. If set to 0.0.0.0 - the IP address will be chosen automatically
        /// </summary>
        [RosProperty("local-address")]
        public string/*IP*/ LocalAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// relay-info-remote-id: relay will use this string instead of client MAC address when constructing Option 82 to be sent to DHCP-server. Option 82 consist of interface packets was received from + client mac address or relay-info-remote-id
        /// </summary>
        [RosProperty("relay-info-remote-id", UnsetOnDefault = true)]
        public string RelayInfoRemoteId { get; set; }

        /// <summary>
        /// name: Descriptive name for the relay
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment: Short description of the client
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// invalid: Shows whether configuration is invalid.
        /// </summary>
        [RosProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /* TODO
        /// <summary>
        /// Reset counters
        /// </summary>
        public void ResetCounters(ITikConnection connection) {
            connection.CreateCommandAndParameters("ip/dhcp-relay/reset-counters",
                TikSpecialProperties.Id, Id).ExecuteNonQuery();
        }
        */
    }

}
