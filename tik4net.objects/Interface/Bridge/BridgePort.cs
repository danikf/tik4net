using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// interface/bridge/port: Port submenu is used to enslave interfaces in a particular bridge interface.
    /// </summary>
    [TikEntity("interface/bridge/port")]
    public class BridgePort
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// interface: Name of the interface
        /// </summary>
        [TikProperty("interface")]
        public string/*name*/ Interface { get; set; }

        /// <summary>
        /// bridge:  The bridge interface the respective interface is grouped in
        /// </summary>
        [TikProperty("bridge")]
        public string/*name*/ Bridge { get; set; }

        /// <summary>
        /// priority: The priority of the interface in comparison with other going to the same subnet
        /// </summary>
        [TikProperty("priority", DefaultValue = "128")]
        public int/*integer: 0..255*/ Priority { get; set; }

        /// <summary>
        /// path-cost: Path cost to the interface, used by STP to determine the "best" path
        /// </summary>
        [TikProperty("path-cost", DefaultValue = "10")]
        public int/*integer: 0..65535*/ PathCost { get; set; }

        /// <summary>
        /// horizon: Use split horizon bridging to prevent bridging loops.  read more»
        /// </summary>
        [TikProperty("horizon", DefaultValue = "none")]
        public string/*none | integer 0..429496729*/ Horizon { get; set; }

        /// <summary>
        /// edge: Set port as edge port or non-edge port, or enable automatic detection. Edge ports are connected to a LAN that has no other bridges attached. If the port is configured to discover edge port then as soon as the bridge detects a BPDU coming to an edge port, the port becomes a non-edge port.
        /// </summary>
        [TikProperty("edge", DefaultValue = "auto")]
        public string/*auto | no | no-discover | yes | yes-discover*/ Edge { get; set; }

        /// <summary>
        /// point-to-point: 
        /// </summary>
        [TikProperty("point-to-point", DefaultValue = "auto")]
        public string/*auto | yes | no*/ PointToPoint { get; set; }

        /// <summary>
        /// external-fdb: Whether to use wireless registration table to speed up bridge host learning
        /// </summary>
        [TikProperty("external-fdb", DefaultValue = "auto")]
        public string/*auto | no | yes*/ ExternalFdb { get; set; }

        /// <summary>
        /// auto-isolate: Prevents STP blocking port from erroneously moving into a forwarding state if no BPDU's are received on the bridge.
        /// </summary>
        [TikProperty("auto-isolate", DefaultValue = "no")]
        public bool AutoIsolate { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public BridgePort()
        {
            Priority = 0x80;
            PathCost = 10;
            Horizon = "none";
        }
    }
}
