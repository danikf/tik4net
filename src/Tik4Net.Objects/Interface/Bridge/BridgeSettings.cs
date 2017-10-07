using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// interface/bridge/settings: 
    /// </summary>
    [TikEntity("interface/bridge/settings", IsSingleton = true)]
    public class BridgeSettings
    {
        /// <summary>
        /// allow-fast-path: Allows  fast path
        /// </summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes")]
        public bool AllowFastPath { get; set; }

        /// <summary>
        /// use-ip-firewall: Force bridged traffic to also be processed by prerouting, forward and postrouting sections of IP routing (http://wiki.mikrotik.com/wiki/Manual:Packet_Flow_v6). This does not apply to routed traffic.
        /// </summary>
        [TikProperty("use-ip-firewall", DefaultValue = "no")]
        public bool UseIpFirewall { get; set; }

        /// <summary>
        /// use-ip-firewall-for-pppoe: Send bridged un-encrypted PPPoE traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
        /// </summary>
        [TikProperty("use-ip-firewall-for-pppoe", DefaultValue = "no")]
        public bool UseIpFirewallForPppoe { get; set; }

        /// <summary>
        /// use-ip-firewall-for-vlan: Send bridged VLAN traffic to also be processed by 'IP firewall' (requires use-ip-firewall=yes to work)
        /// </summary>
        [TikProperty("use-ip-firewall-for-vlan", DefaultValue = "no")]
        public bool UseIpFirewallForVlan { get; set; }
    }

}
