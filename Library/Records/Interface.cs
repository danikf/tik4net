namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// Network interface
    /// </summary>
    [TikRecord("/interface", IncludeDetails = true)]
    public class Interface : IHasId {
        /// <summary>
        /// Unique identifier
        /// </summary>
        [TikProperty(".id", DataType.Id, IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// Name
        /// </summary>
        [TikProperty("name", DataType.String, IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// Underlying technology type
        /// </summary>
        [TikProperty("type", DataType.String)]
        public string Type { get; set; } // TODO: convert to enum

        /// <summary>
        /// Maximum transmission unit
        /// </summary>
        [TikProperty("mtu", DataType.String)]
        public string Mtu { get; set; }

        /// <summary>
        /// L2 MAC addrewss
        /// </summary>
        [TikProperty("mac-address", DataType.MacAddress)]
        public string MacAddress { get; set; }

        /// <summary>
        /// If fast-path is enabled
        /// </summary>
        [TikProperty("fast-path", DataType.Boolean)]
        public bool FastPath { get; set; }
        
        /// <summary>
        /// If the interface is disabled (shutdown)
        /// </summary>
        [TikProperty("disabled", DataType.Boolean)]
        public bool Disabled { get; set; }

        /// <summary>
        /// Comment
        /// </summary>
        [TikProperty("comment", DataType.String)]
        public string Comment { get; set; }



        /// <summary>
        /// Initial name from factory before renamed by user
        /// </summary>
        [TikProperty("default-name", DataType.String, IsReadOnly = true)]
        public string DefaultName { get; set; }

        /// <summary>
        /// Total received bytes
        /// </summary>
        [TikProperty("rx-byte", DataType.Integer, IsReadOnly = true)]
        public long RxByte { get; private set; }

        /// <summary>
        /// Total transmit bytes
        /// </summary>
        [TikProperty("tx-byte", DataType.Integer, IsReadOnly = true)]
        public long TxByte { get; private set; }

        /// <summary>
        /// Total received packets
        /// </summary>
        [TikProperty("rx-packet", DataType.Integer, IsReadOnly = true)]
        public long RxPacket { get; private set; }

        /// <summary>
        /// Total transmitted packets
        /// </summary>
        [TikProperty("tx-packet", DataType.Integer, IsReadOnly = true)]
        public long TxPacket { get; private set; }

        /// <summary>
        /// Total received packets that have been dropped
        /// </summary>
        [TikProperty("rx-drop", DataType.Integer, IsReadOnly = true)]
        public long RxDrop { get; private set; }

        /// <summary>
        /// Total transmitted packets that have been dropped
        /// </summary>
        [TikProperty("tx-drop", DataType.Integer, IsReadOnly = true)]
        public long TxDrop { get; private set; }

        /// <summary>
        /// Total receive errors
        /// </summary>
        [TikProperty("rx-error", DataType.Integer, IsReadOnly = true)]
        public long RxError { get; private set; }

        /// <summary>
        /// Total transmission errors
        /// </summary>
        [TikProperty("tx-error", DataType.Integer, IsReadOnly = true)]
        public long TxError { get; private set; }

        /// <summary>
        /// If the interface is running (active)
        /// </summary>
        [TikProperty("running", DataType.Boolean, IsReadOnly = true)]
        public bool Running { get; private set; }


        public override string ToString() {
            return $"id={Id},name={Name},comment={Comment}";
        }
    }
}
