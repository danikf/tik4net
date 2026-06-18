using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.TrafficFlow
{
    /// <summary>
    /// /ip/traffic-flow: MikroTik Traffic-Flow is a system that provides statistical information
    /// about packets that pass through the router. It supports NetFlow versions 1, 5, 9 and IPFIX
    /// formats, compatible with Cisco and third-party collection tools. This is a singleton
    /// (global configuration — no .id).
    /// </summary>
    [TikEntity("ip/traffic-flow", IsSingleton = true)]
    public class IpTrafficFlow
    {
        /// <summary>
        /// enabled — whether Traffic-Flow data collection is active.
        /// Default: no
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>
        /// interfaces — names of interfaces used to gather traffic-flow statistics.
        /// Use "all" to collect on every interface, or comma-separated interface names.
        /// Default: all
        /// </summary>
        [TikProperty("interfaces", DefaultValue = "all")]
        public string Interfaces { get; set; }

        /// <summary>
        /// cache-entries — number of flows that can simultaneously exist in router memory.
        /// Documented values: 1k, 2k, 4k (default), 16k, 128k, 256k; live routers may also
        /// report additional values such as 1M depending on RouterOS version.
        /// Default: 4k
        /// </summary>
        [TikProperty("cache-entries", DefaultValue = "4k")]
        public string/*enum: 1k|2k|4k|16k|128k|256k|1M|...*/ CacheEntries { get; set; }

        /// <summary>
        /// active-flow-timeout — maximum lifespan duration for a flow (time value, e.g. "30m").
        /// A flow that stays active longer than this timeout is exported and removed from cache.
        /// Default: 30m
        /// </summary>
        [TikProperty("active-flow-timeout", DefaultValue = "30m")]
        public string/*time*/ ActiveFlowTimeout { get; set; }

        /// <summary>
        /// inactive-flow-timeout — duration to maintain an idle flow before treating it as a new
        /// flow (time value, e.g. "15s"). Default: 15s
        /// </summary>
        [TikProperty("inactive-flow-timeout", DefaultValue = "15s")]
        public string/*time*/ InactiveFlowTimeout { get; set; }

        /// <summary>
        /// packet-sampling — enable or disable packet sampling functionality (RouterOS v7+).
        /// When enabled, only a sampled subset of packets is counted per flow.
        /// Default: no
        /// </summary>
        [TikProperty("packet-sampling", DefaultValue = "no")]
        public bool PacketSampling { get; set; }

        /// <summary>
        /// sampling-interval — count of consecutive packets included (sampled) per sampling cycle.
        /// Only meaningful when <see cref="PacketSampling"/> is enabled.
        /// Default: 0
        /// </summary>
        [TikProperty("sampling-interval", DefaultValue = "0")]
        public int SamplingInterval { get; set; }

        /// <summary>
        /// sampling-space — count of consecutive packets omitted (skipped) per sampling cycle.
        /// Only meaningful when <see cref="PacketSampling"/> is enabled.
        /// Default: 0
        /// </summary>
        [TikProperty("sampling-space", DefaultValue = "0")]
        public int SamplingSpace { get; set; }
    }
}
