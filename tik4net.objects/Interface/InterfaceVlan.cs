using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// /interface
    /// </summary>
    [TikEntity("/interface/vlan", IncludeDetails = true)]
    public class InterfaceVlan
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// mtu
        /// </summary>
        [TikProperty("mtu")]
        public string Mtu { get; set; }

        /// <summary>
        /// l2mtu
        /// </summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string L2Mtu { get; set; }

        /// <summary>
        /// mac-address
        /// </summary>
        [TikProperty("mac-address")]
        public string MacAddress { get; set; }
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
        /// arp-timeout
        /// </summary>
        [TikProperty("arp-timeout")]
        public string ArpTimeout { get; set; }
        public enum LoopProtectMode
        {
            /// <summary>
            /// Using Interface Default
            /// </summary>
            [TikEnum("default")]
            Default,
            /// <summary>
            /// Turn Loop back Protection Off
            /// </summary>
            [TikEnum("off")]
            Off,
            /// <summary>
            /// Turn Loop back Protection On
            /// </summary>
            [TikEnum("on")]
            On,
        }

        /// <summary>
        /// loop-protect
        /// Address Resolution Protocol setting    
        /// </summary>
        /// <seealso cref="LoopProtectMode"/>
        [TikProperty("loop-protect", DefaultValue = "default")]
        public LoopProtectMode LoopProtect { get; set; }

        /// <summary>
        /// loop-protect-status
        /// </summary>
        [TikProperty("loop-protect-status", IsReadOnly = true)]
        public bool LoopProtectStatus { get; private set; }

        /// <summary>
        /// loop-protect-send-interval
        /// </summary>
        [TikProperty("loop-protect-send-interval", DefaultValue = "00:00:05")]
        public string/*time*/ LoopProtectSendInterval { get; set; }

        /// <summary>
        /// loop-protect-disable-time
        /// </summary>
        [TikProperty("loop-protect-disable-time", DefaultValue = "00:05:00")]
        public string/*time*/ LoopProtectDisableTime { get; set; }

        [TikProperty("vlan-id", IsMandatory =true)]
        public string vlanId { get; set; }
        /// <summary>
        /// interface
        /// </summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>
        /// use-service-tag
        /// </summary>
        [TikProperty("use-service-tag")]
        public bool UseServiceTag { get; set; }

        /// <summary>
        /// running
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

    }

}
