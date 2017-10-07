using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Queue
{
    /// <summary>
    /// /queue/simple
    /// </summary>
    [TikEntity("/queue/simple", IncludeDetails = true, IsOrdered = true)]
    public class QueueSimple
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
        /// target
        /// </summary>
        [TikProperty("target")]
        public string Target { get; set; }

        /// <summary>
        /// parent
        /// </summary>
        [TikProperty("parent")]
        public string Parent { get; set; }

        /// <summary>
        /// priority
        /// </summary>
        [TikProperty("priority")]
        public string Priority { get; set; }

        /// <summary>
        /// queue
        /// </summary>
        [TikProperty("queue")]
        public string Queue { get; set; }

        /// <summary>
        /// limit-at
        /// </summary>
        [TikProperty("limit-at")]
        public string LimitAt { get; set; }

        /// <summary>
        /// max-limit
        /// </summary>
        [TikProperty("max-limit")]
        public string MaxLimit { get; set; }

        /// <summary>
        /// burst-limit
        /// </summary>
        [TikProperty("burst-limit")]
        public string BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold
        /// </summary>
        [TikProperty("burst-threshold")]
        public string BurstThreshold { get; set; }

        /// <summary>
        /// burst-time
        /// </summary>
        [TikProperty("burst-time")]
        public string BurstTime { get; set; }

        /// <summary>
        /// bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public string Bytes { get; private set; }

        /// <summary>
        /// total-bytes
        /// </summary>
        [TikProperty("total-bytes", IsReadOnly = true)]
        public long TotalBytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public string Packets { get; private set; }

        /// <summary>
        /// total-packets
        /// </summary>
        [TikProperty("total-packets", IsReadOnly = true)]
        public long TotalPackets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [TikProperty("dropped", IsReadOnly = true)]
        public string Dropped { get; private set; }

        /// <summary>
        /// total-dropped
        /// </summary>
        [TikProperty("total-dropped", IsReadOnly = true)]
        public long TotalDropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [TikProperty("rate", IsReadOnly = true)]
        public string Rate { get; private set; }

        /// <summary>
        /// total-rate
        /// </summary>
        [TikProperty("total-rate", IsReadOnly = true)]
        public long TotalRate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [TikProperty("packet-rate", IsReadOnly = true)]
        public string PacketRate { get; private set; }

        /// <summary>
        /// total-packet-rate
        /// </summary>
        [TikProperty("total-packet-rate", IsReadOnly = true)]
        public long TotalPacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [TikProperty("queued-packets", IsReadOnly = true)]
        public string QueuedPackets { get; private set; }

        /// <summary>
        /// total-queued-packets
        /// </summary>
        [TikProperty("total-queued-packets", IsReadOnly = true)]
        public long TotalQueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [TikProperty("queued-bytes", IsReadOnly = true)]
        public string QueuedBytes { get; private set; }

        /// <summary>
        /// total-queued-bytes
        /// </summary>
        [TikProperty("total-queued-bytes", IsReadOnly = true)]
        public long TotalQueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public bool Dynamic { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// dst
        /// </summary>
        [TikProperty("dst")]
        public string Dst { get; set; }

        /// <summary>
        /// total-max-limit
        /// </summary>
        [TikProperty("total-max-limit")]
        public long TotalMaxLimit { get; set; }

        /// <summary>
        /// total-queue
        /// </summary>
        [TikProperty("total-queue")]
        public string TotalQueue { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }
    }
}
