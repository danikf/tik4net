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
        [RosProperty(".id", typeof(RosIdentifier), IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// Name
        /// </summary>
        [RosProperty("name", typeof(RosString), IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// Underlying technology type
        /// </summary>
        [RosProperty("type", typeof(RosString))]
        public string Type { get; set; }

        /// <summary>
        /// Maximum transmission unit
        /// </summary>
        [RosProperty("mtu", typeof(RosString))]
        public string Mtu { get; set; }

        /// <summary>
        /// L2 MAC addrewss
        /// </summary>
        [RosProperty("mac-address", typeof(RosMacAddress))]
        public string MacAddress { get; set; }

        /// <summary>
        /// If fast-path is enabled
        /// </summary>
        [RosProperty("fast-path", typeof(RosBoolean))]
        public bool FastPath { get; set; }
        
        /// <summary>
        /// If the interface is disabled (shutdown)
        /// </summary>
        [RosProperty("disabled", typeof(RosBoolean))]
        public bool Disabled { get; set; }

        /// <summary>
        /// Comment
        /// </summary>
        [RosProperty("comment", typeof(RosString))]
        public string Comment { get; set; }



        /// <summary>
        /// Initial name from factory before renamed by user
        /// </summary>
        [RosProperty("default-name", typeof(RosString), IsReadOnly = true)]
        public string DefaultName { get; set; }

        /// <summary>
        /// Total received bytes
        /// </summary>
        [RosProperty("rx-byte", typeof(RosInteger), IsReadOnly = true)]
        public long RxByte { get; private set; }

        /// <summary>
        /// Total transmit bytes
        /// </summary>
        [RosProperty("tx-byte", typeof(RosInteger), IsReadOnly = true)]
        public long TxByte { get; private set; }

        /// <summary>
        /// Total received packets
        /// </summary>
        [RosProperty("rx-packet", typeof(RosInteger), IsReadOnly = true)]
        public long RxPacket { get; private set; }

        /// <summary>
        /// Total transmitted packets
        /// </summary>
        [RosProperty("tx-packet", typeof(RosInteger), IsReadOnly = true)]
        public long TxPacket { get; private set; }

        /// <summary>
        /// Total received packets that have been dropped
        /// </summary>
        [RosProperty("rx-drop", typeof(RosInteger), IsReadOnly = true)]
        public long RxDrop { get; private set; }

        /// <summary>
        /// Total transmitted packets that have been dropped
        /// </summary>
        [RosProperty("tx-drop", typeof(RosInteger), IsReadOnly = true)]
        public long TxDrop { get; private set; }

        /// <summary>
        /// Total receive errors
        /// </summary>
        [RosProperty("rx-error", typeof(RosInteger), IsReadOnly = true)]
        public long RxError { get; private set; }

        /// <summary>
        /// Total transmission errors
        /// </summary>
        [RosProperty("tx-error", typeof(RosInteger), IsReadOnly = true)]
        public long TxError { get; private set; }

        /// <summary>
        /// If the interface is running (active)
        /// </summary>
        [RosProperty("running", typeof(RosBoolean), IsReadOnly = true)]
        public bool Running { get; private set; }


        public override string ToString() {
            return $"id={Id},name={Name},comment={Comment}";
        }
    }
}
