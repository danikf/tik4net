using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Queue
{
    /// <summary>
    /// /queue/tree
    /// </summary>
    [TikEntity("/queue/tree", IncludeDetails = true, IncludeCliStats = true)]
    public class QueueTree
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// name: unique identifier for the queue, can be referenced as a parent by other queues.
        /// </summary>
        [TikProperty("name")]
        public TikValue<string?> Name { get; set; }

        /// <summary>
        /// parent: specifies the parent queue, typically "global" for top-level queues in one directional HTB.
        /// </summary>
        [TikProperty("parent")]
        public TikValue<string?> Parent { get; set; }

        /// <summary>
        /// packet-mark: references packet marks from /ip/firewall/mangle; matching traffic is subject to this queue.
        /// </summary>
        [TikProperty("packet-mark")]
        public TikValue<string?> PacketMark { get; set; }

        /// <summary>
        /// limit-at: guaranteed minimum bandwidth (committed information rate) for the queue.
        /// </summary>
        [TikProperty("limit-at")]
        public TikValue<long?> LimitAt { get; set; }

        /// <summary>
        /// queue: selects the queue type that determines the queueing algorithm applied.
        /// </summary>
        [TikProperty("queue")]
        public TikValue<string?> Queue { get; set; }

        /// <summary>
        /// priority: values 1-8 determine queue precedence (1=highest); higher priority queues reach max-limit first.
        /// </summary>
        [TikProperty("priority")]
        public TikValue<long?> Priority { get; set; }

        /// <summary>
        /// max-limit: maximum bandwidth allowed for the queue (maximum information rate).
        /// </summary>
        [TikProperty("max-limit")]
        public TikValue<long?> MaxLimit { get; set; }

        /// <summary>
        /// burst-limit: maximum rate achievable during burst periods.
        /// </summary>
        [TikProperty("burst-limit")]
        public TikValue<long?> BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold: trigger point for burst; when average rate is below this value, burst is allowed.
        /// </summary>
        [TikProperty("burst-threshold")]
        public TikValue<long?> BurstThreshold { get; set; }

        /// <summary>
        /// burst-time: time period in seconds over which average rate is calculated for bursts.
        /// </summary>
        [TikProperty("burst-time")]
        public TikValue<TikDuration?> BurstTime { get; set; }

        /// <summary>
        /// bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public TikValue<long?> Bytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public TikValue<long?> Packets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [TikProperty("dropped", IsReadOnly = true)]
        public TikValue<long?> Dropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [TikProperty("rate", IsReadOnly = true)]
        public TikValue<long?> Rate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [TikProperty("packet-rate", IsReadOnly = true)]
        public TikValue<long?> PacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [TikProperty("queued-packets", IsReadOnly = true)]
        public TikValue<long?> QueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [TikProperty("queued-bytes", IsReadOnly = true)]
        public TikValue<long?> QueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikValue<bool?> Invalid { get; private set; }

        /// <summary>
        /// disabled: enable or disable this queue.
        /// </summary>
        [TikProperty("disabled")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>
        /// comment: optional description or comment for this queue.
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }
    }

}
