

namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Option interface
    /// </summary>
    [RosRecord("/interface", IncludeDetails = true)]
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
        [RosProperty("default-name", IsReadOnly = true)]
        public string DefaultName { get; private set; }

        /// <summary>
        /// Total received bytes
        /// </summary>
        [RosProperty("rx-byte", IsReadOnly = true)]
        public long RxByte { get; private set; }

        /// <summary>
        /// Total transmit bytes
        /// </summary>
        [RosProperty("tx-byte", IsReadOnly = true)]
        public long TxByte { get; private set; }

        /// <summary>
        /// Total received packets
        /// </summary>
        [RosProperty("rx-packet", IsReadOnly = true)]
        public long RxPacket { get; private set; }

        /// <summary>
        /// Total transmitted packets
        /// </summary>
        [RosProperty("tx-packet", IsReadOnly = true)]
        public long TxPacket { get; private set; }

        /// <summary>
        /// Total received packets that have been dropped
        /// </summary>
        [RosProperty("rx-drop", IsReadOnly = true)]
        public long RxDrop { get; private set; }

        /// <summary>
        /// Total transmitted packets that have been dropped
        /// </summary>
        [RosProperty("tx-drop", IsReadOnly = true)]
        public long TxDrop { get; private set; }

        [RosProperty("tx-queue-drop", IsReadOnly = true)]
        public long TxQueueDrop { get; private set; }

        /// <summary>
        /// Total receive errors
        /// </summary>
        [RosProperty("rx-error", IsReadOnly = true)]
        public long RxError { get; private set; }

        /// <summary>
        /// Total transmission errors
        /// </summary>
        [RosProperty("tx-error", IsReadOnly = true)]
        public long TxError { get; private set; }

        /// <summary>
        /// Whether interface is running.Note that some interfaces may not have a 'running check' and they will always be reported as "running" (e.g.EoIP)
        /// </summary>
        [RosProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }


        [RosProperty("bindstr", IsReadOnly = true)]
        public string BindStr { get; private set; }

        [RosProperty("bindstr2", IsReadOnly = true)]
        public string BindStr2 { get; private set; }

        [RosProperty("caps", IsReadOnly = true)]
        public string Caps { get; private set; }

        //Whether interface is dynamically created
        [RosProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        //interface index
        [RosProperty("ifindex", IsReadOnly = true)]
        public int IfIndex { get; private set; }

        //interface name in Linux kernel
        [RosProperty("ifname", IsReadOnly = true)]
        public string IfName { get; private set; }

        // Max supported L2MTU
        [RosProperty("max-l2mtu", IsReadOnly = true)]
        public int MaxL2Mtu { get; private set; }
        
        [RosProperty("actual-mtu")]
        public int ActualMtu { get; private set; }

        [RosProperty("last-link-up-time")]
        public string LastLinkUpTime { get; private set; }

        [RosProperty("link-downs")]
        public int LinkDowns { get; private set; }


        //Whether interface is configured as a slave of another interface "]for example Bonding)
        [RosProperty("slave", IsReadOnly = true)]
        public bool Slave { get; private set; }

        [RosProperty("status", IsReadOnly = true)]
        public string Status { get; private set; }

        public override string ToString() {
            return $"id={Id},name={Name},comment={Comment}";
        }
    }
}
