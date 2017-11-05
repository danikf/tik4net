using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// /queue/tree
    /// </summary>
    [RosRecord("/queue/tree", IncludeDetails = true)]
    public class QueueTree  : IHasId {
        /// <summary>
        /// .id
        /// </summary>
        [RosProperty(".id", IsReadOnly = true, IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// Name
        /// </summary>
        [RosProperty("name", IsRequired = true)]
        public string Name { get; set; }

        /// <summary>
        /// parent
        /// </summary>
        [RosProperty("parent")]
        public string Parent { get; set; }

        /// <summary>
        /// packet-mark
        /// </summary>
        [RosProperty("packet-mark")]
        public string PacketMark { get; set; }

        /// <summary>
        /// limit-at
        /// </summary>
        [RosProperty("limit-at")]
        public long LimitAt { get; set; }

        /// <summary>
        /// queue
        /// </summary>
        [RosProperty("queue")]
        public string Queue { get; set; }

        /// <summary>
        /// priority
        /// </summary>
        [RosProperty("priority")]
        public long Priority { get; set; }

        /// <summary>
        /// max-limit
        /// </summary>
        [RosProperty("max-limit")]
        public long MaxLimit { get; set; }

        /// <summary>
        /// burst-limit
        /// </summary>
        [RosProperty("burst-limit")]
        public long BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold
        /// </summary>
        [RosProperty("burst-threshold")]
        public long BurstThreshold { get; set; }

        /// <summary>
        /// burst-time
        /// </summary>
        [RosProperty("burst-time")]
        public string BurstTime { get; set; }

        /// <summary>
        /// bytes
        /// </summary>
        [RosProperty("bytes", IsReadOnly = true)]
        public long Bytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [RosProperty("packets", IsReadOnly = true)]
        public long Packets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [RosProperty("dropped", IsReadOnly = true)]
        public long Dropped { get; private set; }

        /// <summary>
        /// rate
        /// </summary>
        [RosProperty("rate", IsReadOnly = true)]
        public long Rate { get; private set; }

        /// <summary>
        /// packet-rate
        /// </summary>
        [RosProperty("packet-rate", IsReadOnly = true)]
        public long PacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [RosProperty("queued-packets", IsReadOnly = true)]
        public long QueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [RosProperty("queued-bytes", IsReadOnly = true)]
        public long QueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [RosProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        /// <summary>
        /// disabled
        /// </summary>
        [RosProperty("disabled")]
        public bool Disabled { get; set; }

        /// <summary>
        /// comment
        /// </summary>
        [RosProperty("comment")]
        public string Comment { get; set; }
    }

}
