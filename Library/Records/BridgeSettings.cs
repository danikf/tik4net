using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// interface/bridge/settings: 
    /// </summary>
    [RosRecord("/interface/bridge/settings")]
    public class BridgeSettings : SingleRecordBase {
        /// <summary>
        /// allow-fast-path: Allows  fast path
        /// </summary>
        [RosProperty("allow-fast-path")]
        public bool AllowFastPath { get; set; } = true;

        /// <summary>
        /// use-ip-firewall: Force bridged traffic to also be processed by prerouting, forward and postrouting sections of IP routing (http://wiki.mikrotik.com/wiki/Manual:Packet_Flow_v6). This does not apply to routed traffic.
        /// </summary>
        [RosProperty("use-ip-firewall")]
        public bool UseIpFirewall { get; set; }

        /// <summary>
        /// use-ip-firewall-for-pppoe: Send bridged un-encrypted PPPoE traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
        /// </summary>
        [RosProperty("use-ip-firewall-for-pppoe")]
        public bool UseIpFirewallForPppoe { get; set; }

        /// <summary>
        /// use-ip-firewall-for-vlan: Send bridged VLAN traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
        /// </summary>
        [RosProperty("use-ip-firewall-for-vlan")]
        public bool UseIpFirewallForVlan { get; set; }
    }

}
