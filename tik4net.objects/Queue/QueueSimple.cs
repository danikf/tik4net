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
        [TikProperty("name", WinboxLabel = "Name")]
        public TikValue<string?> Name { get; set; }

        /// <summary>
        /// target: IP address/netmask or interface used to identify traffic direction. Upload when source matches, download when destination matches.
        /// </summary>
        [TikProperty("target", WinboxLabel = "Target")]
        public TikValue<string?> Target { get; set; }

        /// <summary>
        /// parent: designates this queue as subordinate to another queue, enabling hierarchical structures.
        /// </summary>
        [TikProperty("parent", WinboxLabel = "Parent")]
        public TikValue<string?> Parent { get; set; }

        /// <summary>
        /// priority: numerical ranking (1-8) where 1 is highest priority; determines which child queue reaches max-limit first.
        /// </summary>
        [TikProperty("priority")]
        public TikValue<string?> Priority { get; set; }

        /// <summary>
        /// queue: specifies the queue type algorithm to use, created via /queue/type.
        /// </summary>
        [TikProperty("queue")]
        public TikValue<string?> Queue { get; set; }

        /// <summary>
        /// limit-at: guaranteed upload/download data rate for the target (CIR).
        /// </summary>
        [TikProperty("limit-at")]
        public TikValue<TikRatePair?> LimitAt { get; set; }

        /// <summary>
        /// max-limit: maximal upload/download data rate allowed for the target (MIR).
        /// </summary>
        [TikProperty("max-limit")]
        public TikValue<TikRatePair?> MaxLimit { get; set; }

        /// <summary>
        /// burst-limit: maximum rate achievable during burst activation periods.
        /// </summary>
        [TikProperty("burst-limit")]
        public TikValue<TikRatePair?> BurstLimit { get; set; }

        /// <summary>
        /// burst-threshold: rate threshold for toggling burst on/off, positioned between limit-at and max-limit.
        /// </summary>
        [TikProperty("burst-threshold")]
        public TikValue<TikRatePair?> BurstThreshold { get; set; }

        /// <summary>
        /// burst-time: duration in seconds for calculating average data rate during bursts.
        /// </summary>
        [TikProperty("burst-time")]
        public TikValue<string?> BurstTime { get; set; }

        /// <summary>
        /// bytes
        /// </summary>
        [TikProperty("bytes", IsReadOnly = true)]
        public TikValue<string?> Bytes { get; private set; }

        /// <summary>
        /// total-bytes
        /// </summary>
        [TikProperty("total-bytes", IsReadOnly = true, WinboxLabel = "Total Bytes")]
        public TikValue<long?> TotalBytes { get; private set; }

        /// <summary>
        /// packets
        /// </summary>
        [TikProperty("packets", IsReadOnly = true)]
        public TikValue<string?> Packets { get; private set; }

        /// <summary>
        /// total-packets
        /// </summary>
        [TikProperty("total-packets", IsReadOnly = true, WinboxLabel = "Total Packets")]
        public TikValue<long?> TotalPackets { get; private set; }

        /// <summary>
        /// dropped
        /// </summary>
        [TikProperty("dropped", IsReadOnly = true)]
        public TikValue<string?> Dropped { get; private set; }

        /// <summary>
        /// total-dropped
        /// </summary>
        [TikProperty("total-dropped", IsReadOnly = true, WinboxLabel = "Total Dropped")]
        public TikValue<long?> TotalDropped { get; private set; }

        /// <summary>
        /// The traffic currently passing the queue, as an <c>upload/download</c> pair — <c>0/0</c> on an
        /// idle queue. <see cref="TotalRate"/> is the same reading as one number.
        /// </summary>
        /// <remarks>
        /// The transports disagree about the spelling and the type is what reconciles them: measured on
        /// RouterOS 7.24 the binary API writes <c>0/0</c> and the CLI transports write <c>0bps/0bps</c>,
        /// which are the same rate and compare equal. <see cref="TikDataRate"/> reads the <c>bps</c> unit
        /// for exactly this field.
        /// </remarks>
        [TikProperty("rate", IsReadOnly = true)]
        public TikValue<TikRatePair?> Rate { get; private set; }

        /// <summary>
        /// total-rate
        /// </summary>
        [TikProperty("total-rate", IsReadOnly = true, WinboxLabel = "Total Avg. Rate")]
        public TikValue<long?> TotalRate { get; private set; }

        /// <summary>
        /// Packets per second through the queue, as an <c>upload/download</c> pair — <c>0/0</c> on both the
        /// API and the CLI transports when idle.
        /// </summary>
        /// <remarks>
        /// The idle spelling agrees on every transport (<c>0/0</c>), and what the CLI writes for a NON-zero
        /// packet rate has never been read — a simple queue counts forwarded traffic only and the lab CHR
        /// forwards none. That is no longer a reason to leave the field a <c>string</c>: a spelling
        /// <see cref="TikDataRate"/> does not recognise is kept as a <see cref="TikDataRate.Token"/> rather
        /// than throwing, so an unexpected form degrades this one property instead of failing the load of
        /// the whole entity.
        /// </remarks>
        [TikProperty("packet-rate", IsReadOnly = true)]
        public TikValue<TikRatePair?> PacketRate { get; private set; }

        /// <summary>
        /// total-packet-rate
        /// </summary>
        [TikProperty("total-packet-rate", IsReadOnly = true, WinboxLabel = "Total Avg. Packet Rate")]
        public TikValue<long?> TotalPacketRate { get; private set; }

        /// <summary>
        /// queued-packets
        /// </summary>
        [TikProperty("queued-packets", IsReadOnly = true)]
        public TikValue<string?> QueuedPackets { get; private set; }

        /// <summary>
        /// total-queued-packets
        /// </summary>
        [TikProperty("total-queued-packets", IsReadOnly = true, WinboxLabel = "Total Queued Packets")]
        public TikValue<long?> TotalQueuedPackets { get; private set; }

        /// <summary>
        /// queued-bytes
        /// </summary>
        [TikProperty("queued-bytes", IsReadOnly = true)]
        public TikValue<string?> QueuedBytes { get; private set; }

        /// <summary>
        /// total-queued-bytes
        /// </summary>
        [TikProperty("total-queued-bytes", IsReadOnly = true, WinboxLabel = "Total Queued Bytes")]
        public TikValue<long?> TotalQueuedBytes { get; private set; }

        /// <summary>
        /// invalid
        /// </summary>
        [TikProperty("invalid", IsReadOnly = true)]
        public TikValue<bool?> Invalid { get; private set; }

        /// <summary>
        /// dynamic
        /// </summary>
        [TikProperty("dynamic", IsReadOnly = true)]
        public TikValue<bool?> Dynamic { get; private set; }

        /// <summary>
        /// disabled: enable or disable this queue.
        /// </summary>
        [TikProperty("disabled")]
        public TikValue<bool?> Disabled { get; set; }

        /// <summary>
        /// dst: destination IP address/netmask for filtering specific traffic streams.
        /// </summary>
        [TikProperty("dst")]
        public TikValue<string?> Dst { get; set; }

        /// <summary>
        /// total-max-limit: maximal data rate for the global-total HTB queue.
        /// </summary>
        [TikProperty("total-max-limit", WinboxLabel = "Total Max Limit")]
        public TikValue<long?> TotalMaxLimit { get; set; }

        /// <summary>
        /// total-queue: queue type for the global-total HTB queue.
        /// </summary>
        [TikProperty("total-queue")]
        public TikValue<string?> TotalQueue { get; set; }

        /// <summary>
        /// comment: optional description or comment for this queue.
        /// </summary>
        [TikProperty("comment")]
        public TikValue<string?> Comment { get; set; }
    }
}
