using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool.Graphing
{
    /// <summary>
    /// /tool/graphing/queue — configures bandwidth graphing for simple queues.
    /// Each entry selects a simple queue whose traffic statistics are graphed.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Graphing</para>
    /// </summary>
    [TikEntity("/tool/graphing/queue", IncludeDetails = true)]
    public class GraphingQueue
    {
        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>simple-queue — name of the simple queue to graph. Use "all" to graph every queue.</summary>
        [TikProperty("simple-queue", DefaultValue = "all")]
        public string SimpleQueue { get; set; }

        /// <summary>allow-address — IP address or prefix allowed to retrieve the graph (e.g. "0.0.0.0/0"). Empty means unrestricted.</summary>
        [TikProperty("allow-address")]
        public string/*IP/CIDR*/ AllowAddress { get; set; }

        /// <summary>allow-target — when yes, the queue target address range may also view the graph in addition to the allow-address. Default: no.</summary>
        [TikProperty("allow-target", DefaultValue = "no")]
        public bool AllowTarget { get; set; }

        /// <summary>store-on-disk — when yes, collected queue data is saved to the router's disk. Default: no.</summary>
        [TikProperty("store-on-disk", DefaultValue = "no")]
        public bool StoreOnDisk { get; set; }

        /// <summary>disabled — when true the graphing entry is disabled. Default: no.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — free-form comment.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Returns a human-readable summary of this queue graphing entry.</summary>
        public override string ToString() => string.Format("graphing/queue: {0} (allow: {1})", SimpleQueue, AllowAddress);
    }
}
