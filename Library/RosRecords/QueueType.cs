namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /queue/type
    /// </summary>
    [RosRecord("/queue/type", IncludeDetails = true)]
    public class QueueType : ISetRecord {
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
        /// kind
        /// </summary>
        [RosProperty("kind")]
        public string Kind { get; set; }

        /// <summary>
        /// pfifo-limit
        /// </summary>
        [RosProperty("pfifo-limit")]
        public long PfifoLimit { get; set; }

        /// <summary>
        /// default
        /// </summary>
        [RosProperty("default")]
        public bool Default { get; set; }

        /// <summary>
        /// sfq-perturb
        /// </summary>
        [RosProperty("sfq-perturb")]
        public long SfqPerturb { get; set; }

        /// <summary>
        /// sfq-allot
        /// </summary>
        [RosProperty("sfq-allot")]
        public long SfqAllot { get; set; }

        /// <summary>
        /// red-limit
        /// </summary>
        [RosProperty("red-limit")]
        public long RedLimit { get; set; }

        /// <summary>
        /// red-min-threshold
        /// </summary>
        [RosProperty("red-min-threshold")]
        public long RedMinThreshold { get; set; }

        /// <summary>
        /// red-max-threshold
        /// </summary>
        [RosProperty("red-max-threshold")]
        public long RedMaxThreshold { get; set; }

        /// <summary>
        /// red-burst
        /// </summary>
        [RosProperty("red-burst")]
        public long RedBurst { get; set; }

        /// <summary>
        /// red-avg-packet
        /// </summary>
        [RosProperty("red-avg-packet")]
        public long RedAvgPacket { get; set; }

        /// <summary>
        /// pcq-rate
        /// </summary>
        [RosProperty("pcq-rate")]
        public long PcqRate { get; set; }

        /// <summary>
        /// pcq-limit
        /// </summary>
        [RosProperty("pcq-limit")]
        public long PcqLimit { get; set; }

        /// <summary>
        /// pcq-classifier
        /// </summary>
        [RosProperty("pcq-classifier")]
        public string PcqClassifier { get; set; }

        /// <summary>
        /// pcq-total-limit
        /// </summary>
        [RosProperty("pcq-total-limit")]
        public long PcqTotalLimit { get; set; }

        /// <summary>
        /// pcq-burst-rate
        /// </summary>
        [RosProperty("pcq-burst-rate")]
        public long PcqBurstRate { get; set; }

        /// <summary>
        /// pcq-burst-threshold
        /// </summary>
        [RosProperty("pcq-burst-threshold")]
        public long PcqBurstThreshold { get; set; }

        /// <summary>
        /// pcq-burst-time
        /// </summary>
        [RosProperty("pcq-burst-time")]
        public string PcqBurstTime { get; set; }

        /// <summary>
        /// pcq-src-address-mask
        /// </summary>
        [RosProperty("pcq-src-address-mask")]
        public long PcqSrcAddressMask { get; set; }

        /// <summary>
        /// pcq-dst-address-mask
        /// </summary>
        [RosProperty("pcq-dst-address-mask")]
        public long PcqDstAddressMask { get; set; }

        /// <summary>
        /// pcq-src-address6-mask
        /// </summary>
        [RosProperty("pcq-src-address6-mask")]
        public long PcqSrcAddress6Mask { get; set; }

        /// <summary>
        /// pcq-dst-address6-mask
        /// </summary>
        [RosProperty("pcq-dst-address6-mask")]
        public long PcqDstAddress6Mask { get; set; }

        /// <summary>
        /// mq-pfifo-limit
        /// </summary>
        [RosProperty("mq-pfifo-limit")]
        public long MqPfifoLimit { get; set; }
    }
}
