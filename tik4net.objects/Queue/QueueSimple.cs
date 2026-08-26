using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Queue
{
    /// <summary>
    /// /queue/simple
    /// </summary>
    [TikEntity("/queue/simple", IncludeDetails = true, IsOrdered = true, IncludeCliStats = true)]
    public class QueueSimple
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name: unique queue identifier used as a parent for other queues.
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string? Name { get; set; }

        /// <summary>
        /// target: IP address/netmask or interface used to identify traffic direction. Upload when source matches, download when destination matches.
        /// </summary>
        [TikProperty("target")]
        public string? Target { get; set; }

        /// <summary>
        /// parent: designates this queue as subordinate to another queue, enabling hierarchical structures.
        /// </summary>
        [TikProperty("parent")]
        public string? Parent { get; set; }

        /// <summary>
        /// priority: numerical ranking (1-8) where 1 is highest priority; determines which child queue reaches max-limit first.
        /// </summary>
        [TikProperty("priority")]
        public string? Priority { get; set; }

        /// <summary>
        /// queue: specifies the queue type algorithm to use, created via /queue/type.
        /// </summary>
        [TikProperty("queue")]
        public string? Queue { get; set; }

        /// <summary>
        /// limit-at: guaranteed upload/download data rate for the target (CIR).
        /// </summary>
        [TikProperty("limit-at")]
        public TikRatePair? LimitAt { get; set; }

        /// <summary>
        /// max-limit: maximal upload/download data rate allowed for the target (MIR).
        /// </summary>
        [TikProperty("max-limit")]
        public TikRatePair? MaxLimit { get; set; }

        /// <summary>
        /// burst-limit: maximum rate achievable during burst activation periods.
        /// </summary>
        [TikProperty("burst-limit")]
        public TikRatePair? BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold: rate threshold for toggling burst on/off, positioned between limit-at and max-limit.
        /// </summary>
        [TikProperty("burst-threshold")]
        public TikRatePair? BurstThreshold { get; set; }

        /// <summary>
        /// burst-time: duration in seconds for calculating average data rate during bursts.
        /// </summary>
        [TikProperty("burst-time")]
        public string? BurstTime { get; set; }

        /// <summary>
        /// bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public string? Bytes { get; private set; }

        /// <summary>
        /// total-bytes
        /// </summary>
        [TikProperty("total-bytes", IsReadOnly = true)]
        public long TotalBytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public string? Packets { get; private set; }

        /// <summary>
        /// total-packets
        /// </summary>
        [TikProperty("total-packets", IsReadOnly = true)]
        public long TotalPackets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [TikProperty("dropped", IsReadOnly = true)]
        public string? Dropped { get; private set; }

        /// <summary>
        /// total-dropped
        /// </summary>
        [TikProperty("total-dropped", IsReadOnly = true)]
        public long TotalDropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [TikProperty("rate", IsReadOnly = true)]
        public string? Rate { get; private set; }

        /// <summary>
        /// total-rate
        /// </summary>
        [TikProperty("total-rate", IsReadOnly = true)]
        public long TotalRate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [TikProperty("packet-rate", IsReadOnly = true)]
        public string? PacketRate { get; private set; }

        /// <summary>
        /// total-packet-rate
        /// </summary>
        [TikProperty("total-packet-rate", IsReadOnly = true)]
        public long TotalPacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [TikProperty("queued-packets", IsReadOnly = true)]
        public string? QueuedPackets { get; private set; }

        /// <summary>
        /// total-queued-packets
        /// </summary>
        [TikProperty("total-queued-packets", IsReadOnly = true)]
        public long TotalQueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [TikProperty("queued-bytes", IsReadOnly = true)]
        public string? QueuedBytes { get; private set; }

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
        /// disabled: enable or disable this queue.
        /// </summary>
        [TikProperty("disabled")]
        public bool? Disabled { get; set; }

        /// <summary>
        /// dst: destination IP address/netmask for filtering specific traffic streams.
        /// </summary>
        [TikProperty("dst")]
        public string? Dst { get; set; }

        /// <summary>
        /// total-max-limit: maximal data rate for the global-total HTB queue.
        /// </summary>
        [TikProperty("total-max-limit")]
        public long TotalMaxLimit { get; set; }

        /// <summary>
        /// total-queue: queue type for the global-total HTB queue.
        /// </summary>
        [TikProperty("total-queue")]
        public string? TotalQueue { get; set; }

        /// <summary>
        /// comment: optional description or comment for this queue.
        /// </summary>
        [TikProperty("comment")]
        public string? Comment { get; set; }
    }
}
