using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net.Objects.Queue
{
    [TikEntity("/queue/tree", IncludeDetails = true)]
    public class QueueTree
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        [TikProperty("parent")]
        public string Parent { get; set; }

        [TikProperty("packet-mark")]
        public string PacketMark { get; set; }

        [TikProperty("limit-at")]
        public long LimitAt { get; set; }

        [TikProperty("queue")]
        public string Queue { get; set; }

        [TikProperty("priority")]
        public long Priority { get; set; }

        [TikProperty("max-limit")]
        public long MaxLimit { get; set; }

        [TikProperty("burst-limit")]
        public long BurstLimit { get; set; }

        [TikProperty("burst-threshold")]
        public long BurstThreshold { get; set; }

        [TikProperty("burst-time")]
        public string BurstTime { get; set; }

        [TikProperty("bytes")]
        public long Bytes { get; set; }

        [TikProperty("packets")]
        public long Packets { get; set; }

        [TikProperty("dropped")]
        public long Dropped { get; set; }

        [TikProperty("rate")]
        public long Rate { get; set; }

        [TikProperty("packet-rate")]
        public long PacketRate { get; set; }

        [TikProperty("queued-packets")]
        public long QueuedPackets { get; set; }

        [TikProperty("queued-bytes")]
        public long QueuedBytes { get; set; }

        [TikProperty("invalid", IsReadOnly = true)]
        public bool Invalid { get; private set; }

        [TikProperty("disabled")]
        public bool Disabled { get; set; }

    }

}
