namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// Network interface
    /// </summary>
    [RosRecord("/interface", IncludeDetails = true)]
    public class Interface : IHasId {
        /// <summary>
        /// Unique identifier
        /// </summary>
        [RosProperty(".id", RosDataType.Id, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// Name
        /// </summary>
        [RosProperty("name", RosDataType.String, IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// Underlying technology type
        /// </summary>
        [RosProperty("type", RosDataType.String)]
        public string Type { get; set; }

        /// <summary>
        /// Maximum transmission unit
        /// </summary>
        [RosProperty("mtu", RosDataType.String)]
        public string Mtu { get; set; }

        /// <summary>
        /// L2 MAC addrewss
        /// </summary>
        [RosProperty("mac-address", RosDataType.MacAddress)]
        public string MacAddress { get; set; }

        /// <summary>
        /// If fast-path is enabled
        /// </summary>
        [RosProperty("fast-path", RosDataType.Boolean)]
        public bool FastPath { get; set; }
        
        /// <summary>
        /// If the interface is disabled (shutdown)
        /// </summary>
        [RosProperty("disabled", RosDataType.Boolean)]
        public bool Disabled { get; set; }

        /// <summary>
        /// Comment
        /// </summary>
        [RosProperty("comment", RosDataType.String)]
        public string Comment { get; set; }



        /// <summary>
        /// Initial name from factory before renamed by user
        /// </summary>
        [RosProperty("default-name", RosDataType.String, IsReadOnly = true)]
        public string DefaultName { get; set; }

        /// <summary>
        /// Total received bytes
        /// </summary>
        [RosProperty("rx-byte", RosDataType.Integer, IsReadOnly = true)]
        public long RxByte { get; private set; }

        /// <summary>
        /// Total transmit bytes
        /// </summary>
        [RosProperty("tx-byte", RosDataType.Integer, IsReadOnly = true)]
        public long TxByte { get; private set; }

        /// <summary>
        /// Total received packets
        /// </summary>
        [RosProperty("rx-packet", RosDataType.Integer, IsReadOnly = true)]
        public long RxPacket { get; private set; }

        /// <summary>
        /// Total transmitted packets
        /// </summary>
        [RosProperty("tx-packet", RosDataType.Integer, IsReadOnly = true)]
        public long TxPacket { get; private set; }

        /// <summary>
        /// Total received packets that have been dropped
        /// </summary>
        [RosProperty("rx-drop", RosDataType.Integer, IsReadOnly = true)]
        public long RxDrop { get; private set; }

        /// <summary>
        /// Total transmitted packets that have been dropped
        /// </summary>
        [RosProperty("tx-drop", RosDataType.Integer, IsReadOnly = true)]
        public long TxDrop { get; private set; }

        /// <summary>
        /// Total receive errors
        /// </summary>
        [RosProperty("rx-error", RosDataType.Integer, IsReadOnly = true)]
        public long RxError { get; private set; }

        /// <summary>
        /// Total transmission errors
        /// </summary>
        [RosProperty("tx-error", RosDataType.Integer, IsReadOnly = true)]
        public long TxError { get; private set; }

        /// <summary>
        /// If the interface is running (active)
        /// </summary>
        [RosProperty("running", RosDataType.Boolean, IsReadOnly = true)]
        public bool Running { get; private set; }


        public override string ToString() {
            return $"id={Id},name={Name},comment={Comment}";
        }
    }
}
