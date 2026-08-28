using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface
{
    /// <summary>
    /// Virtual Local Area Network (VLAN) is a Layer 2 method that allows multiple Virtual LANs on a single physical interface (ethernet, wireless, etc.), giving the ability to segregate LANs efficiently.
    /// You can use MikroTik RouterOS(as well as Cisco IOS, Linux and other router systems) to mark these packets as well as to accept and route marked ones.
    /// As VLAN works on OSI Layer 2, it can be used just as any other network interface without any restrictions.VLAN successfully passes through regular Ethernet bridges.
    /// You can also transport VLANs over wireless links and put multiple VLAN interfaces on a single wireless interface. Note that as VLAN is not a full tunnel protocol(i.e., it does not have additional fields to transport MAC addresses of sender and recipient), the same limitation applies to bridging over VLAN as to bridging plain wireless interfaces.In other words, while wireless clients may participate in VLANs put on wireless interfaces, it is not possible to have VLAN put on a wireless interface in station mode bridged with any other interface. 
    /// </summary>
    [TikEntity("/interface/vlan", IncludeDetails = true)]
    public class InterfaceVlan
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name - Interface name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string? Name { get; set; }

        /// <summary>
        /// mtu - Layer3 Maximum transmission unit
        /// </summary>
        [TikProperty("mtu")]
        public string? Mtu { get; set; }

        /// <summary>
        /// l2mtu - Layer2 MTU. For VLANS this value is not configurable.
        /// </summary>
        [TikProperty("l2mtu", IsReadOnly = true)]
        public string? L2Mtu { get; set; }

        /// <summary>
        /// mac-address
        /// </summary>
        [TikProperty("mac-address")]
        public string? MacAddress { get; set; }
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
        public ArpMode? Arp { get; set; }

        /// <summary>
        /// arp-timeout: how long the ARP record is kept in the ARP table after no packets are received from IP.
        /// </summary>
        [TikProperty("arp-timeout")]
        public TikDuration? ArpTimeout { get; set; }
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
        public TikDuration? LoopProtectSendInterval { get; set; }

        /// <summary>
        /// loop-protect-disable-time
        /// </summary>
        [TikProperty("loop-protect-disable-time", DefaultValue = "00:05:00")]
        public TikDuration? LoopProtectDisableTime { get; set; }

        /// <summary>
        /// vlan-id: the VLAN tag (1–4094) carried on this interface's traffic.
        /// </summary>
        [TikProperty("vlan-id", IsMandatory = true)]
        public string? VlanId { get; set; }

        /// <summary>
        /// Previous spelling of <see cref="VlanId"/>, kept so existing code compiles. It was the only
        /// camelCase public property in the library.
        /// </summary>
        /// <remarks>
        /// Deliberately carries no <c>[TikProperty]</c>: the mapper maps only decorated properties, so this
        /// forwarder is invisible to it and <c>vlan-id</c> stays mapped exactly once.
        /// </remarks>
        [Obsolete("Renamed to VlanId (the library's naming is PascalCase).")]
        public string? vlanId
        {
            get => VlanId;
            set => VlanId = value;
        }

        /// <summary>
        /// interface
        /// </summary>
        [TikProperty("interface", IsMandatory = true)]
        public string? Interface { get; set; }

        /// <summary>
        /// use-service-tag: IEEE 802.1ad compatible Service Tag.
        /// </summary>
        [TikProperty("use-service-tag")]
        public bool? UseServiceTag { get; set; }

        /// <summary>
        /// running
        /// </summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool? Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string? Comment { get; set; }

    }

}
