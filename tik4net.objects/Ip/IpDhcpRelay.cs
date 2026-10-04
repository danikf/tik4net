using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// ip/dhcp relay
    /// DHCP Relay is just a proxy that is able to receive a DHCP request and resend it to the real DHCP server.
    /// 
    /// </summary>
    [TikEntity("/ip/dhcp-relay", IncludeDetails = true)]
    public class IpDhcpRelay
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// add-relay-info: Adds DHCP relay agent information if enabled according to RFC 3046.  Agent Circuit ID Sub-option contains mac address of an interface, Agent Remote ID Sub-option contains MAC address of the client from which request was received.
        /// </summary>
        [TikProperty("add-relay-info", DefaultValue = "no", WinboxLabel = "Add Relay Info")]
        public TikField<string?> AddRelayInfo { get; set; }

        /// <summary>
        /// delay-threshold: If secs field in DHCP packet is smaller than delay-threshold, then this packet is ignored
        /// </summary>
        [TikProperty("delay-threshold", DefaultValue = "none", WinboxLabel = "Delay Threshold")]
        public TikField<TikDuration?> DelayThreshold { get; set; }

        /// <summary>
        /// dhcp-server: List of DHCP servers' IP addresses which should the DHCP requests be forwarded to
        /// </summary>
        [TikProperty("dhcp-server", WinboxLabel = "DHCP Server")]
        public TikField<TikValueList<string>?> DhcpServer { get; set; }

        /// <summary>
        /// interface: Interface name the DHCP relay will be working on.
        /// </summary>
        [TikProperty("interface", WinboxLabel = "Interface")]
        public TikField<string?> Interface { get; set; }

        /// <summary>
        /// local-address: The unique IP address of this DHCP relay needed for DHCP server to distinguish relays. If set to 0.0.0.0 - the IP address will be chosen automatically
        /// </summary>
        [TikProperty("local-address", DefaultValue = "0.0.0.0", WinboxLabel = "Local Address")]
        public TikField<string?> LocalAddress { get; set; }

        /// <summary>
        /// relay-info-remote-id: relay will use this string instead of client MAC address when constructing Option 82 to be sent to DHCP-server. Option 82 consist of interface packets was received from + client mac address or relay-info-remote-id
        /// </summary>
        [TikProperty("relay-info-remote-id", WinboxLabel = "Relay Info Remote ID")]
        public TikField<string?> RelayInfoRemoteId { get; set; }

        /// <summary>
        /// name: Descriptive name for the relay
        /// </summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// disabled: 
        /// </summary>
        [TikProperty("disabled")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>
        /// comment: Short description of the client
        /// </summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>
        /// invalid: Shows whether configuration is invalid.
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikField<bool?> Invalid { get; private set; }

        /// <summary>
        /// Reset counters
        /// </summary>
        public void ResetCounters(ITikConnection connection)
        {
            // Id: IsMandatory=true, so a loaded entity always carries it.
            connection.CreateCommandAndParameters("ip/dhcp-relay/reset-counters",
                TikSpecialProperties.Id, Id!).ExecuteNonQuery();
        }
    }

}
