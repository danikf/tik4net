using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Queue
{
    [TikEntity("/queue/type", IncludeDetails = true)]
    public class QueueType
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        [TikProperty("kind")]
        public string Kind { get; set; }

        [TikProperty("pfifo-limit")]
        public long PfifoLimit { get; set; }

        [TikProperty("default")]
        public bool Default { get; set; }

        [TikProperty("sfq-perturb")]
        public long SfqPerturb { get; set; }

        [TikProperty("sfq-allot")]
        public long SfqAllot { get; set; }

        [TikProperty("red-limit")]
        public long RedLimit { get; set; }

        [TikProperty("red-min-threshold")]
        public long RedMinThreshold { get; set; }

        [TikProperty("red-max-threshold")]
        public long RedMaxThreshold { get; set; }

        [TikProperty("red-burst")]
        public long RedBurst { get; set; }

        [TikProperty("red-avg-packet")]
        public long RedAvgPacket { get; set; }

        [TikProperty("pcq-rate")]
        public long PcqRate { get; set; }

        [TikProperty("pcq-limit")]
        public long PcqLimit { get; set; }

        [TikProperty("pcq-classifier")]
        public string PcqClassifier { get; set; }

        [TikProperty("pcq-total-limit")]
        public long PcqTotalLimit { get; set; }

        [TikProperty("pcq-burst-rate")]
        public long PcqBurstRate { get; set; }

        [TikProperty("pcq-burst-threshold")]
        public long PcqBurstThreshold { get; set; }

        [TikProperty("pcq-burst-time")]
        public string PcqBurstTime { get; set; }

        [TikProperty("pcq-src-address-mask")]
        public long PcqSrcAddressMask { get; set; }

        [TikProperty("pcq-dst-address-mask")]
        public long PcqDstAddressMask { get; set; }

        [TikProperty("pcq-src-address6-mask")]
        public long PcqSrcAddress6Mask { get; set; }

        [TikProperty("pcq-dst-address6-mask")]
        public long PcqDstAddress6Mask { get; set; }

        [TikProperty("mq-pfifo-limit")]
        public long MqPfifoLimit { get; set; }
    }
}
