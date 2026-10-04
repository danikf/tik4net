using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Queue
{
    /// <summary>
    /// /queue/type
    /// </summary>
    [TikEntity("/queue/type", IncludeDetails = true)]
    public class QueueType
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name: unique identifier for the queue type referenced by other queue configurations.
        /// </summary>
        [TikProperty("name", WinboxLabel = "Type Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>
        /// kind: packet processing algorithm used (PFIFO, BFIFO, MQ-PFIFO, RED, SFQ, PCQ, CoDel, FQ-Codel, CAKE).
        /// </summary>
        [TikProperty("kind", WinboxLabel = "Kind")]
        public TikField<string?> Kind { get; set; }

        /// <summary>
        /// pfifo-limit: maximum number of packets the PFIFO queue can hold.
        /// </summary>
        [TikProperty("pfifo-limit", WinboxLabel = "pfifo: PFIFO Queue Size")]
        public TikField<long?> PfifoLimit { get; set; }

        /// <summary>
        /// default: indicates if this is a pre-configured queue type provided by RouterOS. A flag, not a setting.
        /// </summary>
        [TikProperty("default", IsReadOnly = true)]
        public TikField<bool?> Default { get; private set; }

        /// <summary>
        /// sfq-perturb: interval in seconds for re-hashing SFQ algorithm to prevent hash collisions.
        /// </summary>
        [TikProperty("sfq-perturb", WinboxLabel = "sfq: Perturb")]
        public TikField<long?> SfqPerturb { get; set; }

        /// <summary>
        /// sfq-allot: number of bytes distributed to each sub-stream per fair queuing round.
        /// </summary>
        [TikProperty("sfq-allot", WinboxLabel = "sfq: Allot")]
        public TikField<long?> SfqAllot { get; set; }

        /// <summary>
        /// red-limit: maximum RED queue size before packets are dropped.
        /// </summary>
        [TikProperty("red-limit", WinboxLabel = "red: RED Queue Size")]
        public TikField<long?> RedLimit { get; set; }

        /// <summary>
        /// red-min-threshold: RED lower threshold; no drops occur below this average queue size.
        /// </summary>
        [TikProperty("red-min-threshold", WinboxLabel = "red: Min Threshold")]
        public TikField<long?> RedMinThreshold { get; set; }

        /// <summary>
        /// red-max-threshold: RED upper threshold; all packets dropped above this average queue size.
        /// </summary>
        [TikProperty("red-max-threshold", WinboxLabel = "red: Max Threshold")]
        public TikField<long?> RedMaxThreshold { get; set; }

        /// <summary>
        /// red-burst: burst allowance for the RED algorithm.
        /// </summary>
        [TikProperty("red-burst", WinboxLabel = "red: Burst")]
        public TikField<long?> RedBurst { get; set; }

        /// <summary>
        /// red-avg-packet: average packet size used in RED algorithm calculations.
        /// </summary>
        [TikProperty("red-avg-packet", WinboxLabel = "red: Avg. Packet Size")]
        public TikField<long?> RedAvgPacket { get; set; }

        /// <summary>
        /// pcq-rate: maximum data rate per individual PCQ sub-stream; 0 means equal bandwidth division.
        /// </summary>
        [TikProperty("pcq-rate", WinboxLabel = "pcq: Rate")]
        public TikField<long?> PcqRate { get; set; }

        /// <summary>
        /// pcq-limit: queue size for a single PCQ sub-stream in KiB.
        /// </summary>
        [TikProperty("pcq-limit", WinboxLabel = "pcq: Queue Size")]
        public TikField<long?> PcqLimit { get; set; }

        /// <summary>
        /// pcq-classifier: selection of sub-stream identifiers (src-address, dst-address, src-port, dst-port).
        /// </summary>
        [TikProperty("pcq-classifier", WinboxLabel = "pcq: Classifier")]
        public TikField<string?> PcqClassifier { get; set; }

        /// <summary>
        /// pcq-total-limit: maximum amount of queued data across all PCQ sub-streams in KiB.
        /// </summary>
        [TikProperty("pcq-total-limit", WinboxLabel = "pcq: Total Queue Size")]
        public TikField<long?> PcqTotalLimit { get; set; }

        /// <summary>
        /// pcq-burst-rate: maximum rate during burst periods for PCQ sub-streams.
        /// </summary>
        [TikProperty("pcq-burst-rate", WinboxLabel = "pcq: Burst Rate")]
        public TikField<long?> PcqBurstRate { get; set; }

        /// <summary>
        /// pcq-burst-threshold: burst activation threshold value for PCQ.
        /// </summary>
        [TikProperty("pcq-burst-threshold", WinboxLabel = "pcq: Burst Threshold")]
        public TikField<long?> PcqBurstThreshold { get; set; }

        /// <summary>
        /// pcq-burst-time: period over which average data rate is calculated for PCQ bursts.
        /// </summary>
        [TikProperty("pcq-burst-time", WinboxLabel = "pcq: Burst Time")]
        public TikField<TikDuration?> PcqBurstTime { get; set; }

        /// <summary>
        /// pcq-src-address-mask: IPv4 network size for source address PCQ sub-stream identification.
        /// </summary>
        [TikProperty("pcq-src-address-mask", WinboxLabel = "pcq: Src. Address Mask")]
        public TikField<long?> PcqSrcAddressMask { get; set; }

        /// <summary>
        /// pcq-dst-address-mask: IPv4 network size for destination address PCQ identification.
        /// </summary>
        [TikProperty("pcq-dst-address-mask", WinboxLabel = "pcq: Dst. Address Mask")]
        public TikField<long?> PcqDstAddressMask { get; set; }

        /// <summary>
        /// pcq-src-address6-mask: IPv6 network size for source address PCQ identification.
        /// </summary>
        [TikProperty("pcq-src-address6-mask", WinboxLabel = "pcq: Src. Address6 Mask")]
        public TikField<long?> PcqSrcAddress6Mask { get; set; }

        /// <summary>
        /// pcq-dst-address6-mask: IPv6 network size for destination address PCQ identification.
        /// </summary>
        [TikProperty("pcq-dst-address6-mask", WinboxLabel = "pcq: Dst. Address6 Mask")]
        public TikField<long?> PcqDstAddress6Mask { get; set; }

        /// <summary>
        /// mq-pfifo-limit: packet limit for MQ-PFIFO queues supporting multiple transmit queues on SMP systems.
        /// </summary>
        [TikProperty("mq-pfifo-limit", WinboxLabel = "mq-pfifo: MQ Queue Size")]
        public TikField<long?> MqPfifoLimit { get; set; }
    }
}
