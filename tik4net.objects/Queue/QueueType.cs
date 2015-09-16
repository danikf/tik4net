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
        public string Id { get; private set; }

        /// <summary>
        /// name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// kind
        /// </summary>
        [TikProperty("kind")]
        public string Kind { get; set; }

        /// <summary>
        /// pfifo-limit
        /// </summary>
        [TikProperty("pfifo-limit")]
        public long PfifoLimit { get; set; }

        /// <summary>
        /// default
        /// </summary>
        [TikProperty("default")]
        public bool Default { get; set; }

        /// <summary>
        /// sfq-perturb
        /// </summary>
        [TikProperty("sfq-perturb")]
        public long SfqPerturb { get; set; }

        /// <summary>
        /// sfq-allot
        /// </summary>
        [TikProperty("sfq-allot")]
        public long SfqAllot { get; set; }

        /// <summary>
        /// red-limit
        /// </summary>
        [TikProperty("red-limit")]
        public long RedLimit { get; set; }

        /// <summary>
        /// red-min-threshold
        /// </summary>
        [TikProperty("red-min-threshold")]
        public long RedMinThreshold { get; set; }

        /// <summary>
        /// red-max-threshold
        /// </summary>
        [TikProperty("red-max-threshold")]
        public long RedMaxThreshold { get; set; }

        /// <summary>
        /// red-burst
        /// </summary>
        [TikProperty("red-burst")]
        public long RedBurst { get; set; }

        /// <summary>
        /// red-avg-packet
        /// </summary>
        [TikProperty("red-avg-packet")]
        public long RedAvgPacket { get; set; }

        /// <summary>
        /// pcq-rate
        /// </summary>
        [TikProperty("pcq-rate")]
        public long PcqRate { get; set; }

        /// <summary>
        /// pcq-limit
        /// </summary>
        [TikProperty("pcq-limit")]
        public long PcqLimit { get; set; }

        /// <summary>
        /// pcq-classifier
        /// </summary>
        [TikProperty("pcq-classifier")]
        public string PcqClassifier { get; set; }

        /// <summary>
        /// pcq-total-limit
        /// </summary>
        [TikProperty("pcq-total-limit")]
        public long PcqTotalLimit { get; set; }

        /// <summary>
        /// pcq-burst-rate
        /// </summary>
        [TikProperty("pcq-burst-rate")]
        public long PcqBurstRate { get; set; }

        /// <summary>
        /// pcq-burst-threshold
        /// </summary>
        [TikProperty("pcq-burst-threshold")]
        public long PcqBurstThreshold { get; set; }

        /// <summary>
        /// pcq-burst-time
        /// </summary>
        [TikProperty("pcq-burst-time")]
        public string PcqBurstTime { get; set; }

        /// <summary>
        /// pcq-src-address-mask
        /// </summary>
        [TikProperty("pcq-src-address-mask")]
        public long PcqSrcAddressMask { get; set; }

        /// <summary>
        /// pcq-dst-address-mask
        /// </summary>
        [TikProperty("pcq-dst-address-mask")]
        public long PcqDstAddressMask { get; set; }

        /// <summary>
        /// pcq-src-address6-mask
        /// </summary>
        [TikProperty("pcq-src-address6-mask")]
        public long PcqSrcAddress6Mask { get; set; }

        /// <summary>
        /// pcq-dst-address6-mask
        /// </summary>
        [TikProperty("pcq-dst-address6-mask")]
        public long PcqDstAddress6Mask { get; set; }

        /// <summary>
        /// mq-pfifo-limit
        /// </summary>
        [TikProperty("mq-pfifo-limit")]
        public long MqPfifoLimit { get; set; }
    }
}
