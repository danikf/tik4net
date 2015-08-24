using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Queue
{
    [TikEntity("/queue/simple", IncludeDetails = true)]
    public class QueueSimple
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        [TikProperty("target")]
        public string Target { get; set; }

        [TikProperty("parent")]
        public string Parent { get; set; }

        [TikProperty("priority")]
        public string Priority { get; set; }

        [TikProperty("queue")]
        public string Queue { get; set; }

        [TikProperty("limit-at")]
        public string LimitAt { get; set; }

        [TikProperty("max-limit")]
        public string MaxLimit { get; set; }

        [TikProperty("burst-limit")]
        public string BurstLimit { get; set; }

        [TikProperty("burst-threshold")]
        public string BurstThreshold { get; set; }

        [TikProperty("burst-time")]
        public string BurstTime { get; set; }

        [TikProperty("bytes")]
        public string Bytes { get; set; }

        [TikProperty("total-bytes")]
        public long TotalBytes { get; set; }

        [TikProperty("packets")]
        public string Packets { get; set; }

        [TikProperty("total-packets")]
        public long TotalPackets { get; set; }

        [TikProperty("dropped")]
        public string Dropped { get; set; }

        [TikProperty("total-dropped")]
        public long TotalDropped { get; set; }

        [TikProperty("rate")]
        public string Rate { get; set; }

        [TikProperty("total-rate")]
        public long TotalRate { get; set; }

        [TikProperty("packet-rate")]
        public string PacketRate { get; set; }

        [TikProperty("total-packet-rate")]
        public long TotalPacketRate { get; set; }

        [TikProperty("queued-packets")]
        public string QueuedPackets { get; set; }

        [TikProperty("total-queued-packets")]
        public long TotalQueuedPackets { get; set; }

        [TikProperty("queued-bytes")]
        public string QueuedBytes { get; set; }

        [TikProperty("total-queued-bytes")]
        public long TotalQueuedBytes { get; set; }

        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        [TikProperty("dst")]
        public string Dst { get; set; }

        [TikProperty("total-max-limit")]
        public long TotalMaxLimit { get; set; }

        [TikProperty("total-queue")]
        public string TotalQueue { get; set; }

        [TikProperty("comment")]
        public string Comment { get; set; }
    }

}
