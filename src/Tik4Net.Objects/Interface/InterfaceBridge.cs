using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// interface/bridge
    /// Ethernet-like networks (Ethernet, Ethernet over IP, IEEE802.11 in ap-bridge or bridge mode, WDS, VLAN) can be connected together using MAC bridges. The bridge feature allows the interconnection of hosts connected to separate LANs (using EoIP, geographically distributed networks can be bridged as well if any kind of IP network interconnection exists between them) as if they were attached to a single LAN. As bridges are transparent, they do not appear in traceroute list, and no utility can make a distinction between a host working in one LAN and a host working in another LAN if these LANs are bridged (depending on the way the LANs are interconnected, latency and data rate between hosts may vary).
    /// Network loops may emerge (intentionally or not) in complex topologies. Without any special treatment, loops would prevent network from functioning normally, as they would lead to avalanche-like packet multiplication. Each bridge runs an algorithm which calculates how the loop can be prevented. STP and RSTP allows bridges to communicate with each other, so they can negotiate a loop free topology. All other alternative connections that would otherwise form loops, are put to standby, so that should the main connection fail, another connection could take its place. This algorithm exchanges  configuration messages (BPDU - Bridge Protocol Data Unit) periodically, so that all bridges are updated with the newest information about changes in network topology. (R)STP selects a root bridge which is responsible for network reconfiguration, such as blocking and opening ports on other bridges. The root bridge is the bridge with the lowest bridge ID.
    /// </summary>
    [TikEntity("interface/bridge")]
    public class InterfaceBridge
    {
        #region Submenu classes - Obsolete

        /// <summary>
        /// Obsolete: use Bridge.BridgeSettings class.
        /// </summary>
        [Obsolete("use Bridge.BridgeSettings class.", true)]
        public abstract class BridgeSettings
        {

        }

        /// <summary>
        /// Obsolete: use Bridge.BridgePort class.
        /// </summary>
        [Obsolete("use Bridge.BridgeSettings class.", true)]
        public abstract class BridgePort
        {

        }


        /// <summary>
        /// Obsolete: use Bridge.BridgeFilter class.
        /// </summary>
        [Obsolete("use Bridge.BridgeFilter class.", true)]

        public abstract class BridgeFilter
        {

        }

        /// <summary>
        /// Obsolete: use Bridge.BridgeNat class.
        /// </summary>
        [Obsolete("use Bridge.BridgeNat class.", true)]
        public abstract class BridgeNat
        {

        }
        #endregion


        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// admin-mac: Static MAC address of the bridge (takes effect if auto-mac=no)
        /// </summary>
        [TikProperty("admin-mac")]
        public string/*MAC address*/ AdminMac { get; set; }

        /// <summary>
        /// ageing-time: How long a host's information will be kept in the bridge database
        /// </summary>
        [TikProperty("ageing-time", DefaultValue = "00:05:00")]
        public string/*time*/ AgeingTime { get; set; }

        /// <summary>
        /// Address Resolution Protocol setting  
        /// </summary>
        /// <seealso cref="Arp"/>
        public enum ArpMode
        {
            /// <summary>
            /// disabled - the interface will not use ARP
            /// </summary>
            [TikEnum("disabled")]
            Disabled,
            /// <summary>
            /// enabled - the interface will use ARP
            /// </summary>
            [TikEnum("enabled")]
            Enabled,
            /// <summary>
            /// proxy-arp - the interface will use the ARP proxy feature
            /// </summary>
            [TikEnum("proxy-arp")]
            ProxyArp,
            /// <summary>
            /// reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the "/ip arp" table. No dynamic entries will be automatically stored in the "/ip arp" table. Therefore for communications to be successful, a valid static entry must already exist.
            /// </summary>
            [TikEnum("reply-only")]
            ReplyOnly
        }

        /// <summary>
        /// arp
        /// Address Resolution Protocol setting    
        ///          disabled - the interface will not use ARP
        ///          enabled - the interface will use ARP
        ///          proxy-arp - the interface will use the ARP proxy feature
        ///          reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the "/ip arp" table. No dynamic entries will be automatically stored in the "/ip arp" table. Therefore for communications to be successful, a valid static entry must already exist.
        /// </summary>
        /// <seealso cref="ArpMode"/>
        [TikProperty("arp", DefaultValue = "enabled")]
        public ArpMode Arp { get; set; }

        /// <summary>
        /// auto-mac: Automatically select the smallest MAC address of bridge ports as a bridge MAC address
        /// </summary>
        [TikProperty("auto-mac", DefaultValue = "yes")]
        public bool AutoMac { get; set; }

        /// <summary>
        /// forward-delay: Time which is spent during the initialization phase of the bridge interface (i.e., after router startup or enabling the interface) in listening/learning state before the bridge will start functioning normally
        /// </summary>
        [TikProperty("forward-delay", DefaultValue = "00:00:15")]
        public string/*time*/ ForwardDelay { get; set; }

        /// <summary>
        /// l2mtu: Layer2 Maximum transmission unit.  read more&#187; 
        /// </summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string/*integer; read-only*/ L2mtu { get; private set; }

        /// <summary>
        /// max-message-age: How long to remember Hello messages received from other bridges
        /// </summary>
        [TikProperty("max-message-age", DefaultValue = "00:00:20")]
        public string/*time*/ MaxMessageAge { get; set; }

        /// <summary>
        /// mtu: Maximum Transmission Unit
        /// </summary>
        [TikProperty("mtu", DefaultValue = "1500")]
        public string Mtu { get; set; }

        /// <summary>
        /// name: Name of the bridge interface
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string/*text*/ Name { get; set; }

        /// <summary>
        /// priority
        /// Spanning tree protocol priority for bridge interface. Bridge with the smallest (lowest) bridge ID becomes a Root-Bridge. Bridge ID consists of two numbers - priority and MAC address of the bridge. To compare two bridge IDs, the priority is compared first. If two bridges have equal priority, then the MAC addresses are compared.
        /// </summary>
        [TikProperty("priority", DefaultValue = "8000")]
        public string/*integer: 0..65535 decimal format or 0x0000-0xffff hex format*/ Priority { get; set; }

        /// <summary>
        /// protocol-mode: Select Spanning tree protocol (STP) or Rapid spanning tree protocol (RSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change.
        /// </summary>
        /// <seealso cref="ProtocolMode"/>
        public enum ProtocolModeModes
        {
            /// <summary>
            /// 
            /// </summary>
            [TikEnum("none")]
            None,
            /// <summary>
            /// rstp - Select Spanning tree protocol (STP)
            /// </summary>
            [TikEnum("rstp")]
            Rstp,
            /// <summary>
            /// rstp - Rapid spanning tree protocol (RSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change.
            /// </summary>
            [TikEnum("stp")]
            Stp,
        }

        /// <summary>
        /// protocol-mode: Select Spanning tree protocol (STP) or Rapid spanning tree protocol (RSTP) to ensure a loop-free topology for any bridged LAN. RSTP provides for faster spanning tree convergence after a topology change.
        /// </summary>
        /// <seealso cref="ProtocolModeModes"/>
        [TikProperty("protocol-mode", DefaultValue = "rstp")]
        public ProtocolModeModes ProtocolMode { get; set; }

        /// <summary>
        /// transmit-hold-count: The Transmit Hold Count used by the Port Transmit state machine to limit transmission rate
        /// </summary>
        [TikProperty("transmit-hold-count", DefaultValue = "6")]
        public int/*integer: 1..10*/ TransmitHoldCount { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public InterfaceBridge()
        {
            AgeingTime = "00:05:00";
            Arp = ArpMode.Enabled;
            AutoMac = true;
            ForwardDelay = "00:00:15";
            MaxMessageAge = "00:00:20";
            Mtu = "1500";
            Priority = "8000";
            ProtocolMode = ProtocolModeModes.Rstp;
            TransmitHoldCount = 6;
        }
    }
}
