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
        [TikProperty("name", IsMandatory = true)]
        public string? Name { get; set; }

        /// <summary>
        /// kind: packet processing algorithm used (PFIFO, BFIFO, MQ-PFIFO, RED, SFQ, PCQ, CoDel, FQ-Codel, CAKE).
        /// </summary>
        [TikProperty("kind")]
        public string? Kind { get; set; }

        /// <summary>
        /// pfifo-limit: maximum number of packets the PFIFO queue can hold.
        /// </summary>
        [TikProperty("pfifo-limit")]
        public long PfifoLimit { get; set; }

        /// <summary>
        /// default: indicates if this is a pre-configured queue type provided by RouterOS.
        /// </summary>
        [TikProperty("default")]
        public bool? Default { get; set; }

        /// <summary>
        /// sfq-perturb: interval in seconds for re-hashing SFQ algorithm to prevent hash collisions.
        /// </summary>
        [TikProperty("sfq-perturb")]
        public long SfqPerturb { get; set; }

        /// <summary>
        /// sfq-allot: number of bytes distributed to each sub-stream per fair queuing round.
        /// </summary>
        [TikProperty("sfq-allot")]
        public long SfqAllot { get; set; }

        /// <summary>
        /// red-limit: maximum RED queue size before packets are dropped.
        /// </summary>
        [TikProperty("red-limit")]
        public long RedLimit { get; set; }

        /// <summary>
        /// red-min-threshold: RED lower threshold; no drops occur below this average queue size.
        /// </summary>
        [TikProperty("red-min-threshold")]
        public long RedMinThreshold { get; set; }

        /// <summary>
        /// red-max-threshold: RED upper threshold; all packets dropped above this average queue size.
        /// </summary>
        [TikProperty("red-max-threshold")]
        public long RedMaxThreshold { get; set; }

        /// <summary>
        /// red-burst: burst allowance for the RED algorithm.
        /// </summary>
        [TikProperty("red-burst")]
        public long RedBurst { get; set; }

        /// <summary>
        /// red-avg-packet: average packet size used in RED algorithm calculations.
        /// </summary>
        [TikProperty("red-avg-packet")]
        public long RedAvgPacket { get; set; }

        /// <summary>
        /// pcq-rate: maximum data rate per individual PCQ sub-stream; 0 means equal bandwidth division.
        /// </summary>
        [TikProperty("pcq-rate")]
        public long PcqRate { get; set; }

        /// <summary>
        /// pcq-limit: queue size for a single PCQ sub-stream in KiB.
        /// </summary>
        [TikProperty("pcq-limit")]
        public long PcqLimit { get; set; }

        /// <summary>
        /// pcq-classifier: selection of sub-stream identifiers (src-address, dst-address, src-port, dst-port).
        /// </summary>
        [TikProperty("pcq-classifier")]
        public string? PcqClassifier { get; set; }

        /// <summary>
        /// pcq-total-limit: maximum amount of queued data across all PCQ sub-streams in KiB.
        /// </summary>
        [TikProperty("pcq-total-limit")]
        public long PcqTotalLimit { get; set; }

        /// <summary>
        /// pcq-burst-rate: maximum rate during burst periods for PCQ sub-streams.
        /// </summary>
        [TikProperty("pcq-burst-rate")]
        public long PcqBurstRate { get; set; }

        /// <summary>
        /// pcq-burst-threshold: burst activation threshold value for PCQ.
        /// </summary>
        [TikProperty("pcq-burst-threshold")]
        public long PcqBurstThreshold { get; set; }

        /// <summary>
        /// pcq-burst-time: period over which average data rate is calculated for PCQ bursts.
        /// </summary>
        [TikProperty("pcq-burst-time")]
        public TikDuration? PcqBurstTime { get; set; }

        /// <summary>
        /// pcq-src-address-mask: IPv4 network size for source address PCQ sub-stream identification.
        /// </summary>
        [TikProperty("pcq-src-address-mask")]
        public long PcqSrcAddressMask { get; set; }

        /// <summary>
        /// pcq-dst-address-mask: IPv4 network size for destination address PCQ identification.
        /// </summary>
        [TikProperty("pcq-dst-address-mask")]
        public long PcqDstAddressMask { get; set; }

        /// <summary>
        /// pcq-src-address6-mask: IPv6 network size for source address PCQ identification.
        /// </summary>
        [TikProperty("pcq-src-address6-mask")]
        public long PcqSrcAddress6Mask { get; set; }

        /// <summary>
        /// pcq-dst-address6-mask: IPv6 network size for destination address PCQ identification.
        /// </summary>
        [TikProperty("pcq-dst-address6-mask")]
        public long PcqDstAddress6Mask { get; set; }

        /// <summary>
        /// mq-pfifo-limit: packet limit for MQ-PFIFO queues supporting multiple transmit queues on SMP systems.
        /// </summary>
        [TikProperty("mq-pfifo-limit")]
        public long MqPfifoLimit { get; set; }
    }
}
