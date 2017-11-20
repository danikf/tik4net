

using System;

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Option interface
    /// </summary>
    [RosRecord("/interface")]
    public class Interface : SetRecordBase {
        /// <summary>
        /// Name
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// Underlying technology type
        /// </summary>
        [RosProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Maximum transmission unit
        /// </summary>
        [RosProperty("mtu")]
        public string Mtu { get; set; }

        /// <summary>
        /// Layer 2 maxiumum transmission unit
        /// </summary>
        [RosProperty("l2mtu")]
        public string L2Mtu { get; set; }

        /// <summary>
        /// L2 MAC addrewss
        /// </summary>
        [RosProperty("mac-address")]
        public string MacAddress { get; set; }

        /// <summary>
        /// If fast-path is enabled
        /// </summary>
        [RosProperty("fast-path")]
        public bool FastPath { get; set; }

        /// <summary>
        /// If the interface is disabled (shutdown)
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// Comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }



        /// <summary>
        /// Initial name from factory before renamed by user
        /// </summary>
        [RosProperty("default-name")] // Read-only
        public string DefaultName { get; private set; }

        /// <summary>
        /// Total received bytes
        /// </summary>
        [RosProperty("rx-byte")] // Read-only
        public long RxByte { get; private set; }

        /// <summary>
        /// Total transmit bytes
        /// </summary>
        [RosProperty("tx-byte")] // Read-only
        public long TxByte { get; private set; }

        /// <summary>
        /// Total received packets
        /// </summary>
        [RosProperty("rx-packet")] // Read-only
        public long RxPacket { get; private set; }

        /// <summary>
        /// Total transmitted packets
        /// </summary>
        [RosProperty("tx-packet")] // Read-only
        public long TxPacket { get; private set; }

        /// <summary>
        /// Total received packets that have been dropped
        /// </summary>
        [RosProperty("rx-drop")] // Read-only
        public long RxDrop { get; private set; }

        /// <summary>
        /// Total transmitted packets that have been dropped
        /// </summary>
        [RosProperty("tx-drop")] // Read-only
        public long TxDrop { get; private set; }

        [RosProperty("tx-queue-drop")] // Read-only
        public long TxQueueDrop { get; private set; }

        /// <summary>
        /// Total receive errors
        /// </summary>
        [RosProperty("rx-error")] // Read-only
        public long RxError { get; private set; }

        /// <summary>
        /// Total transmission errors
        /// </summary>
        [RosProperty("tx-error")] // Read-only
        public long TxError { get; private set; }

        /// <summary>
        /// Whether interface is running.Note that some interfaces may not have a 'running check' and they will always be reported as "running" (e.g.EoIP)
        /// </summary>
        [RosProperty("running")] // Read-only
        public bool Running { get; private set; }


        [RosProperty("bindstr")] // Read-only
        public string BindStr { get; private set; }

        [RosProperty("bindstr2")] // Read-only
        public string BindStr2 { get; private set; }

        [RosProperty("caps")] // Read-only
        public string Caps { get; private set; }

        //Whether interface is dynamically created
        [RosProperty("dynamic")] // Read-only
        public bool Dynamic { get; private set; }

        //interface index
        [RosProperty("ifindex")] // Read-only
        public int IfIndex { get; private set; }

        //interface name in Linux kernel
        [RosProperty("ifname")] // Read-only
        public string IfName { get; private set; }

        // Max supported L2MTU
        [RosProperty("max-l2mtu")] // Read-only
        public int MaxL2Mtu { get; private set; }
        
        [RosProperty("actual-mtu")]
        public int ActualMtu { get; private set; }

        [RosProperty("last-link-up-time")]
        public DateTime? LastLinkUpTime { get; private set; }

        [RosProperty("link-downs")]
        public int LinkDowns { get; private set; }


        //Whether interface is configured as a slave of another interface "]for example Bonding)
        [RosProperty("slave")] // Read-only
        public bool Slave { get; private set; }

        [RosProperty("status")] // Read-only
        public string Status { get; private set; }

        public override string ToString() {
            return $"id={Id},name={Name},comment={Comment}";
        }
    }
}
