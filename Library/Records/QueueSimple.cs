namespace InvertedTomato.TikLink.Records {
    /// <summary>
    /// /queue/simple
    /// </summary>
    [RosRecord("/queue/simple")]
    public class QueueSimple : OrderedSetRecordBase {
        /// <summary>
        /// name
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// target
        /// </summary>
        [RosProperty("target")]
        public string Target { get; set; }

        /// <summary>
        /// parent
        /// </summary>
        [RosProperty("parent")]
        public string Parent { get; set; }

        /// <summary>
        /// priority
        /// </summary>
        [RosProperty("priority")]
        public string Priority { get; set; }

        /// <summary>
        /// queue
        /// </summary>
        [RosProperty("queue")]
        public string Queue { get; set; }

        /// <summary>
        /// limit-at
        /// </summary>
        [RosProperty("limit-at")]
        public string LimitAt { get; set; }

        /// <summary>
        /// max-limit
        /// </summary>
        [RosProperty("max-limit")]
        public string MaxLimit { get; set; }

        /// <summary>
        /// burst-limit
        /// </summary>
        [RosProperty("burst-limit")]
        public string BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold
        /// </summary>
        [RosProperty("burst-threshold")]
        public string BurstThreshold { get; set; }

        /// <summary>
        /// burst-time
        /// </summary>
        [RosProperty("burst-time")]
        public string BurstTime { get; set; }

        /// <summary>
        /// disabled
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// dst
        /// </summary>
        [RosProperty("dst")]
        public string Dst { get; set; }

        /// <summary>
        /// total-max-limit
        /// </summary>
        [RosProperty("total-max-limit")]
        public long TotalMaxLimit { get; set; }

        /// <summary>
        /// total-queue
        /// </summary>
        [RosProperty("total-queue")]
        public string TotalQueue { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }



        /// <summary>
        /// bytes
        /// </summary>
        [RosProperty("bytes")] // Read-only
        public string Bytes { get; private set; }

        /// <summary>
        /// total-bytes
        /// </summary>
        [RosProperty("total-bytes")] // Read-only
        public long TotalBytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [RosProperty("packets")] // Read-only
        public string Packets { get; private set; }

        /// <summary>
        /// total-packets
        /// </summary>
        [RosProperty("total-packets")] // Read-only
        public long TotalPackets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [RosProperty("dropped")] // Read-only
        public string Dropped { get; private set; }

        /// <summary>
        /// total-dropped
        /// </summary>
        [RosProperty("total-dropped")] // Read-only
        public long TotalDropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [RosProperty("rate")] // Read-only
        public string Rate { get; private set; }

        /// <summary>
        /// total-rate
        /// </summary>
        [RosProperty("total-rate")] // Read-only
        public long TotalRate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [RosProperty("packet-rate")] // Read-only
        public string PacketRate { get; private set; }

        /// <summary>
        /// total-packet-rate
        /// </summary>
        [RosProperty("total-packet-rate")] // Read-only
        public long TotalPacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [RosProperty("queued-packets")] // Read-only
        public string QueuedPackets { get; private set; }

        /// <summary>
        /// total-queued-packets
        /// </summary>
        [RosProperty("total-queued-packets")] // Read-only
        public long TotalQueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [RosProperty("queued-bytes")] // Read-only
        public string QueuedBytes { get; private set; }

        /// <summary>
        /// total-queued-bytes
        /// </summary>
        [RosProperty("total-queued-bytes")] // Read-only
        public long TotalQueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [RosProperty("invalid")] // Read-only
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [RosProperty("dynamic")] // Read-only
        public bool Dynamic { get; private set; }
    }
}
