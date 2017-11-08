namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /queue/simple
    /// </summary>
    [RosRecord("/queue/simple", IncludeDetails = true, IsOrdered = true)]
    public class QueueSimple : ISetRecord {
        /// <summary>
        /// .id
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

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
        [RosProperty("bytes", IsReadOnly = true)]
        public string Bytes { get; private set; }

        /// <summary>
        /// total-bytes
        /// </summary>
        [RosProperty("total-bytes", IsReadOnly = true)]
        public long TotalBytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [RosProperty("packets", IsReadOnly = true)]
        public string Packets { get; private set; }

        /// <summary>
        /// total-packets
        /// </summary>
        [RosProperty("total-packets", IsReadOnly = true)]
        public long TotalPackets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [RosProperty("dropped", IsReadOnly = true)]
        public string Dropped { get; private set; }

        /// <summary>
        /// total-dropped
        /// </summary>
        [RosProperty("total-dropped", IsReadOnly = true)]
        public long TotalDropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [RosProperty("rate", IsReadOnly = true)]
        public string Rate { get; private set; }

        /// <summary>
        /// total-rate
        /// </summary>
        [RosProperty("total-rate", IsReadOnly = true)]
        public long TotalRate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [RosProperty("packet-rate", IsReadOnly = true)]
        public string PacketRate { get; private set; }

        /// <summary>
        /// total-packet-rate
        /// </summary>
        [RosProperty("total-packet-rate", IsReadOnly = true)]
        public long TotalPacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [RosProperty("queued-packets", IsReadOnly = true)]
        public string QueuedPackets { get; private set; }

        /// <summary>
        /// total-queued-packets
        /// </summary>
        [RosProperty("total-queued-packets", IsReadOnly = true)]
        public long TotalQueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [RosProperty("queued-bytes", IsReadOnly = true)]
        public string QueuedBytes { get; private set; }

        /// <summary>
        /// total-queued-bytes
        /// </summary>
        [RosProperty("total-queued-bytes", IsReadOnly = true)]
        public long TotalQueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [RosProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [RosProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }
    }
}
