using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// interface/bridge/settings: 
    /// </summary>
    [RosRecord("/interface/bridge/settings", IsSingleton = true)]
    public class BridgeSettings {
        /// <summary>
        /// allow-fast-path: Allows  fast path
        /// </summary>
        [RosProperty("allow-fast-path", DefaultValue = "yes")]
        public bool AllowFastPath { get; set; }

        /// <summary>
        /// use-ip-firewall: Force bridged traffic to also be processed by prerouting, forward and postrouting sections of IP routing (http://wiki.mikrotik.com/wiki/Manual:Packet_Flow_v6). This does not apply to routed traffic.
        /// </summary>
        [RosProperty("use-ip-firewall", DefaultValue = "no")]
        public bool UseIpFirewall { get; set; }

        /// <summary>
        /// use-ip-firewall-for-pppoe: Send bridged un-encrypted PPPoE traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
        /// </summary>
        [RosProperty("use-ip-firewall-for-pppoe", DefaultValue = "no")]
        public bool UseIpFirewallForPppoe { get; set; }

        /// <summary>
        /// use-ip-firewall-for-vlan: Send bridged VLAN traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
        /// </summary>
        [RosProperty("use-ip-firewall-for-vlan", DefaultValue = "no")]
        public bool UseIpFirewallForVlan { get; set; }
    }

}
