using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool.Graphing
{
    /// <summary>
    /// /tool/graphing/interface — configures bandwidth graphing for selected interfaces.
    /// Each entry defines which interface to graph, who may view the graphs, and whether
    /// the collected data is persisted to disk.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Graphing</para>
    /// </summary>
    [TikEntity("/tool/graphing/interface", IncludeDetails = true)]
    public class GraphingInterface
    {
        /// <summary>.id — primary key of the row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>interface — name of the interface to graph. Use "all" to graph every interface.</summary>
        [TikProperty("interface", DefaultValue = "all")]
        public string Interface { get; set; }

        /// <summary>allow-address — IP address or prefix allowed to retrieve the graph (e.g. "0.0.0.0/0"). Empty means unrestricted.</summary>
        [TikProperty("allow-address")]
        public string/*IP/CIDR*/ AllowAddress { get; set; }

        /// <summary>store-on-disk — when yes, collected traffic data is saved to the router's disk. Default: no.</summary>
        [TikProperty("store-on-disk", DefaultValue = "no")]
        public bool StoreOnDisk { get; set; }

        /// <summary>disabled — when true the graphing entry is disabled. Default: no.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — free-form comment.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Returns a human-readable summary of this graphing entry.</summary>
        public override string ToString() => string.Format("graphing/interface: {0} (allow: {1})", Interface, AllowAddress);
    }
}
