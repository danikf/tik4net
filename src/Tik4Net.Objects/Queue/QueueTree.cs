using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Queue
{
    /// <summary>
    /// /queue/tree
    /// </summary>
    [TikEntity("/queue/tree", IncludeDetails = true)]
    public class QueueTree
    {
        /// <summary>
        /// .id
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// Name
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// parent
        /// </summary>
        [TikProperty("parent")]
        public string Parent { get; set; }

        /// <summary>
        /// packet-mark
        /// </summary>
        [TikProperty("packet-mark")]
        public string PacketMark { get; set; }

        /// <summary>
        /// limit-at
        /// </summary>
        [TikProperty("limit-at")]
        public long LimitAt { get; set; }

        /// <summary>
        /// queue
        /// </summary>
        [TikProperty("queue")]
        public string Queue { get; set; }

        /// <summary>
        /// priority
        /// </summary>
        [TikProperty("priority")]
        public long Priority { get; set; }

        /// <summary>
        /// max-limit
        /// </summary>
        [TikProperty("max-limit")]
        public long MaxLimit { get; set; }

        /// <summary>
        /// burst-limit
        /// </summary>
        [TikProperty("burst-limit")]
        public long BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold
        /// </summary>
        [TikProperty("burst-threshold")]
        public long BurstThreshold { get; set; }

        /// <summary>
        /// burst-time
        /// </summary>
        [TikProperty("burst-time")]
        public string BurstTime { get; set; }

        /// <summary>
        /// bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public long Bytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public long Packets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [TikProperty("dropped", IsReadOnly = true)]
        public long Dropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [TikProperty("rate", IsReadOnly = true)]
        public long Rate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [TikProperty("packet-rate", IsReadOnly = true)]
        public long PacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [TikProperty("queued-packets", IsReadOnly = true)]
        public long QueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [TikProperty("queued-bytes", IsReadOnly = true)]
        public long QueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [TikProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }
    }

}
