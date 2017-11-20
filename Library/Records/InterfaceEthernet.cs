using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// interface/ethernet
    /// MikroTik RouterOS supports various types of Ethernet interfaces. 
    /// </summary>
    [RosRecord("/interface/ethernet")]
    public class InterfaceEthernet : SetRecordBase {
        /// <summary>
        /// arp
        /// Address Resolution Protocol mode:
        ///  disabled - the interface will not use ARP
        ///  enabled - the interface will use ARP
        ///  proxy-arp - the interface will use the ARP proxy feature
        ///  reply-only - the interface will only reply to requests originated from matching IP address/MAC address combinations which are entered as static entries in the  ARP table. No dynamic entries will be automatically stored in the ARP table. Therefore for communications to be successful, a valid static entry must already exist.
        /// </summary>
        [RosProperty("arp")]
        public string/*disabled | enabled | proxy-arp | reply-only*/ Arp { get; set; } = "enabled";

        /// <summary>
        /// auto-negotiation
        /// When enabled, the interface "advertises" its maximum capabilities to achieve the best connection possible.
        ///  Note1: Auto-negotiation should not be disabled on one end only, otherwise Ethernet Interfaces may not work properly. 
        ///  Note2: Gigabit link cannot work with auto-negotiation disabled.
        /// </summary>
        [RosProperty("auto-negotiation")]
        public bool AutoNegotiation { get; set; } = true;

        /// <summary>
        /// bandwidth: Sets max rx/tx bandwidth in kbps that will be handled by an interface. TX limit is supported on all Atheros  switch-chip ports. RX limit is supported only on AR8327 switch-chip ports.
        /// </summary>
        [RosProperty("bandwidth")]
        public string/*integer/integer*/ Bandwidth { get; set; } = "unlimited/unlimited";

        /// <summary>
        /// cable-setting: Changes the cable length setting (only applicable to NS DP83815/6 cards)
        /// </summary>
        [RosProperty("cable-setting")]
        public string/*default | short | standard*/ CableSetting { get; set; } = "default";

        /// <summary>
        /// comment: Descriptive name of an item
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }

        /// <summary>
        /// disable-running-check: Disable running check. If this value is set to 'no', the router automatically detects whether the NIC is connected with a device in the Option or not. Default value is 'yes' because older NICs do not support it. (only applicable to x86)
        /// </summary>
        [RosProperty("disable-running-check")]
        public bool DisableRunningCheck { get; set; } = true;

        /// <summary>
        /// Options for Yes-No properties.
        /// </summary>
        public enum YesNoOptions {
            /// <summary>
            /// yes
            /// </summary>
            [RosEnum("true")]
            Yes,

            /// <summary>
            /// no
            /// </summary>
            [RosEnum("false")]
            No,
        }

        /// <summary>
        /// flow-control-tx: When set to on, port will send pause frames when specific buffer usage thresholds is met. auto is the same as on except when auto-negotiation=yes flow control status is resolved by taking into account what other end advertises. Feature is supported on AR724x, AR9xxx, QCA9xxx CPU ports, all CCR ports and all Atheros switch chip ports.
        /// </summary>
        [RosProperty("flow-control-tx")]
        public YesNoOptions/*yes | no | auto*/ FlowControlTx { get; set; }

        /// <summary>
        /// flow-control-rx: When set to on, port will process received pause frames and suspend transmission if required. auto is the same as on except when auto-negotiation=yes flow control status is resolved by taking into account what other end advertises. Feature is supported on AR724x, AR9xxx, QCA9xxx CPU ports, all CCR ports and all Atheros switch chip ports.
        /// </summary>
        [RosProperty("flow-control-rx")]
        public YesNoOptions/*yes | no | auto*/ FlowControlRx { get; set; }

        /// <summary>
        /// flow-control-auto
        /// </summary>
        [RosProperty("flow-control-auto")]
        public YesNoOptions/*yes | no | auto*/ FlowControlAuto { get; set; }

        /// <summary>
        /// full-duplex: Defines whether the transmission of data appears in two directions simultaneously
        /// </summary>
        [RosProperty("full-duplex")]
        public bool FullDuplex { get; set; } = true;

        /// <summary>
        /// l2mtu: Layer2 Maximum transmission unit.  Read more&gt;&gt; 
        /// 
        /// integer [0..65536]
        /// </summary>
        [RosProperty("l2mtu")]
        public int/*integer [0..65536]*/ L2mtu { get; set; }

        /// <summary>
        /// mac-address: Media Access Control number of an interface.
        /// </summary>
        [RosProperty("mac-address")]
        public string/*MAC*/ MacAddress { get; set; }

        /// <summary>
        /// master-port: Sets interface to be a slave of this named switch group master interface
        /// </summary>
        [RosProperty("master-port")]
        public string/*name*/ MasterPort { get; set; } = "none";

        /// <summary>
        /// mdix-enable: Whether the MDI/X auto cross over cable correction feature is enabled for the port (Hardware specific, e.g. ether1 on RB500 can be set to yes/no. Fixed to 'yes' on other hardware.)
        /// </summary>
        [RosProperty("mdix-enable")]
        public bool MdixEnable { get; set; } = true;

        /// <summary>
        /// mtu: Layer3 Maximum transmission unit
        /// 
        /// integer [0..65536]
        /// </summary>
        [RosProperty("mtu")]
        public int/*integer [0..65536]*/ Mtu { get; set; } = 1500;

        /// <summary>
        /// name: Name of an interface
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// orig-mac-address: 
        /// </summary>
        [RosProperty("orig-mac-address")]
        public string/*MAC*/ OrigMacAddress { get; set; }

        /// <summary>
        /// poe-out: Poe Out settings.  Read more &gt;&gt;
        /// 
        /// auto-on | forced-on | off
        /// </summary>
        [RosProperty("poe-out")]
        public string/*auto-on | forced-on | off*/ PoeOut { get; set; } = "off";

        /// <summary>
        /// poe-priority: Poe Out settings.  Read more &gt;&gt;
        /// </summary>
        [RosProperty("poe-priority")]
        public string PoePriority { get; set; }

        /// <summary>
        /// sfp-rate-select: high | low
        /// </summary>
        [RosProperty("sfp-rate-select")]
        public string/*high | low*/ SfpRateSelect { get; set; } = "high";

        /// <summary>
        /// speed: Sets the data transmission speed of an interface. By default, this value is the maximal data rate supported by the interface
        /// 
        /// 10Mbps | 10Gbps | 100Mbps | 1Gbps
        /// </summary>
        [RosProperty("speed")]
        public string/*10Mbps | 10Gbps | 100Mbps | 1Gbps*/ Speed { get; set; }

        /// <summary>
        /// running: Whether interface is running. Note that some interface does not have running check and they are always reported as "running"
        /// </summary>
        [RosProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>
        /// rx-1024-1518: Total count of received 1024 to 1518 byte packets
        /// </summary>
        [RosProperty("rx-1024-1518")] // Read-only
        public int Rx10241518 { get; private set; }

        /// <summary>
        /// rx-128-255: Total count of received 128 to 255 byte packets
        /// </summary>
        [RosProperty("rx-128-255")] // Read-only
        public int Rx128255 { get; private set; }

        /// <summary>
        /// rx-1519-max: Total count of received packets larger than 1519 bytes
        /// </summary>
        [RosProperty("rx-1519-max")] // Read-only
        public int Rx1519Max { get; private set; }

        /// <summary>
        /// rx-256-511: Total count of received 256 to 511 byte packets
        /// </summary>
        [RosProperty("rx-256-511")] // Read-only
        public int Rx256511 { get; private set; }

        /// <summary>
        /// rx-512-1023: Total count of received 512 to 1023 byte packets
        /// </summary>
        [RosProperty("rx-512-1023")] // Read-only
        public int Rx5121023 { get; private set; }

        /// <summary>
        /// rx-64: Total count of received 64 byte packets
        /// </summary>
        [RosProperty("rx-64")] // Read-only
        public int Rx64 { get; private set; }

        /// <summary>
        /// rx-65-127: Total count of received 65 to 127 byte packets
        /// </summary>
        [RosProperty("rx-65-127")] // Read-only
        public int Rx65127 { get; private set; }

        /// <summary>
        /// rx-align-error: Total count of received align error messages
        /// </summary>
        [RosProperty("rx-align-error", IsReadOnly = true)]
        public int RxAlignError { get; private set; }

        /// <summary>
        /// rx-broadcast: Total count of received broadcast packets
        /// </summary>
        [RosProperty("rx-broadcast", IsReadOnly = true)]
        public int RxBroadcast { get; private set; }

        /// <summary>
        /// rx-bytes: Total count of received bytes
        /// </summary>
        [RosProperty("rx-bytes", IsReadOnly = true)]
        public long RxBytes { get; private set; }

        /// <summary>
        /// rx-fcs-error: Total count of received frames with incorrect checksum
        /// </summary>
        [RosProperty("rx-fcs-error", IsReadOnly = true)]
        public int RxFcsError { get; private set; }

        /// <summary>
        /// rx-fragment: Total count of received fragmented frames
        /// </summary>
        [RosProperty("rx-fragment", IsReadOnly = true)]
        public int RxFragment { get; private set; }

        /// <summary>
        /// rx-multicast: Total count of received multicast packets
        /// </summary>
        [RosProperty("rx-multicast", IsReadOnly = true)]
        public int RxMulticast { get; private set; }

        /// <summary>
        /// rx-overflow: Total count of received overflowed packets
        /// </summary>
        [RosProperty("rx-overflow", IsReadOnly = true)]
        public int RxOverflow { get; private set; }

        /// <summary>
        /// rx-pause: Total count of received pause frames
        /// </summary>
        [RosProperty("rx-pause", IsReadOnly = true)]
        public int RxPause { get; private set; }

        /// <summary>
        /// rx-runt
        /// Total count of received frames shorter than the minimum 64 bytes
        /// but with a valid CRC
        /// </summary>
        [RosProperty("rx-runt", IsReadOnly = true)]
        public int RxRunt { get; private set; }

        /// <summary>
        /// rx-too-long: Total count of received packets that were larger than the maximum packet size
        /// </summary>
        [RosProperty("rx-too-long", IsReadOnly = true)]
        public int RxTooLong { get; private set; }

        /// <summary>
        /// slave: Whether interface is configured as a slave of another interface (for example Bonding)
        /// </summary>
        [RosProperty("slave", IsReadOnly = true)]
        public bool Slave { get; private set; }

        /// <summary>
        /// switch: ID to which switch chip interface belongs to.
        /// </summary>
        [RosProperty("switch", IsReadOnly = true)]
        public string Switch { get; private set; }

        /// <summary>
        /// tx-1024-1518: Total count of transmitted 1024 to 1518 byte packets
        /// </summary>
        [RosProperty("tx-1024-1518")] // Read-only
        public int Tx10241518 { get; private set; }

        /// <summary>
        /// tx-128-255: Total count of transmitted 128 to 255 byte packets
        /// </summary>
        [RosProperty("tx-128-255")] // Read-only
        public int Tx128255 { get; private set; }

        /// <summary>
        /// tx-1519-max: Total count of transmitted packets larger than 1519 bytes
        /// </summary>
        [RosProperty("tx-1519-max")] // Read-only
        public int Tx1519Max { get; private set; }

        /// <summary>
        /// tx-256-511: Total count of transmitted 256 to 511 byte packets
        /// </summary>
        [RosProperty("tx-256-511")] // Read-only
        public int Tx256511 { get; private set; }

        /// <summary>
        /// tx-512-1023: Total count of transmitted 512 to 1023 byte packets
        /// </summary>
        [RosProperty("tx-512-1023")] // Read-only
        public int Tx5121023 { get; private set; }

        /// <summary>
        /// tx-64: Total count of transmitted 64 byte packets
        /// </summary>
        [RosProperty("tx-64")] // Read-only
        public int Tx64 { get; private set; }

        /// <summary>
        /// tx-65-127: Total count of transmitted 65 to 127 byte packets
        /// </summary>
        [RosProperty("tx-65-127")] // Read-only
        public int Tx65127 { get; private set; }

        /// <summary>
        /// tx-align-error: Total count of transmitted align error messages
        /// </summary>
        [RosProperty("tx-align-error", IsReadOnly = true)]
        public int TxAlignError { get; private set; }

        /// <summary>
        /// tx-broadcast: Total count of transmitted broadcast packets
        /// </summary>
        [RosProperty("tx-broadcast", IsReadOnly = true)]
        public int TxBroadcast { get; private set; }

        /// <summary>
        /// tx-bytes: Total count of transmitted bytes
        /// </summary>
        [RosProperty("tx-bytes", IsReadOnly = true)]
        public long TxBytes { get; private set; }

        /// <summary>
        /// tx-fcs-error: Total count of transmitted frames with incorrect checksum
        /// </summary>
        [RosProperty("tx-fcs-error", IsReadOnly = true)]
        public int TxFcsError { get; private set; }

        /// <summary>
        /// tx-fragment: Total count of transmitted fragmented frames
        /// </summary>
        [RosProperty("tx-fragment", IsReadOnly = true)]
        public int TxFragment { get; private set; }

        /// <summary>
        /// tx-multicast: Total count of transmitted multicast packets
        /// </summary>
        [RosProperty("tx-multicast", IsReadOnly = true)]
        public int TxMulticast { get; private set; }

        /// <summary>
        /// tx-overflow: Total count of transmitted overflowed packets
        /// </summary>
        [RosProperty("tx-overflow", IsReadOnly = true)]
        public int TxOverflow { get; private set; }

        /// <summary>
        /// tx-pause: Total count of transmitted pause frames
        /// </summary>
        [RosProperty("tx-pause", IsReadOnly = true)]
        public int TxPause { get; private set; }

        /// <summary>
        /// tx-runt
        /// Total count of transmitted frames shorter than the minimum 64 bytes
        /// but with a valid CRC
        /// </summary>
        [RosProperty("tx-runt", IsReadOnly = true)]
        public int TxRunt { get; private set; }

        /// <summary>
        /// tx-too-long: Total count of transmitted packets that were larger than the maximum packet size
        /// </summary>
        [RosProperty("tx-too-long", IsReadOnly = true)]
        public int TxTooLong { get; private set; }

        /// <summary>
        /// disabled: Whether interface is disabled
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
