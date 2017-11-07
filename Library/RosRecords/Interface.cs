using InvertedTomato.TikLink.RosDataTypes;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// Network interface
    /// </summary>
    [RosRecord("/interface", IncludeDetails = true)]
    public class Interface : IHasId {
        /// <summary>
        /// Unique identifier
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

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
        public string DefaultName { get; set; }

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
        /// If the interface is running (active)
        /// </summary>
        [RosProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }


        public override string ToString() {
            return $"id={Id},name={Name},comment={Comment}";
        }
    }
}
