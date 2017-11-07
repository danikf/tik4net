
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InvertedTomato.TikLink.RosRecords {
    /// <summary>
    /// interface/bridge/port: Port submenu is used to enslave interfaces in a particular bridge interface.
    /// </summary>
    [RosRecord("/interface/bridge/port")]
    public class BridgePort : IHasId {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [RosProperty(".id", IsRequired = true)]
        public string Id { get; set; }

        /// <summary>
        /// interface: Name of the interface
        /// </summary>
        [RosProperty("interface")]
        public string/*name*/ Interface { get; set; }

        /// <summary>
        /// bridge:  The bridge interface the respective interface is grouped in
        /// </summary>
        [RosProperty("bridge")]
        public string/*name*/ Bridge { get; set; }

        /// <summary>
        /// priority: The priority of the interface in comparison with other going to the same subnet
        /// </summary>
        [RosProperty("priority")]
        public int/*integer: 0..255*/ Priority { get; set; } = 128;

        /// <summary>
        /// path-cost: Path cost to the interface, used by STP to determine the "best" path
        /// </summary>
        [RosProperty("path-cost")]
        public int/*integer: 0..65535*/ PathCost { get; set; } = 10;

        /// <summary>
        /// horizon: Use split horizon bridging to prevent bridging loops.  read more»
        /// </summary>
        [RosProperty("horizon")]
        public string/*none | integer 0..429496729*/ Horizon { get; set; } = "none";

        /// <summary>
        /// edge: Set port as edge port or non-edge port, or enable automatic detection. Edge ports are connected to a LAN that has no other bridges attached. If the port is configured to discover edge port then as soon as the bridge detects a BPDU coming to an edge port, the port becomes a non-edge port.
        /// </summary>
        [RosProperty("edge")]
        public string/*auto | no | no-discover | yes | yes-discover*/ Edge { get; set; } = "auto";

        /// <summary>
        /// point-to-point: 
        /// </summary>
        [RosProperty("point-to-point")]
        public string/*auto | yes | no*/ PointToPoint { get; set; } = "auto";

        /// <summary>
        /// external-fdb: Whether to use wireless registration table to speed up bridge host learning
        /// </summary>
        [RosProperty("external-fdb")]
        public string/*auto | no | yes*/ ExternalFdb { get; set; } = "auto";

        /// <summary>
        /// auto-isolate: Prevents STP blocking port from erroneously moving into a forwarding state if no BPDU's are received on the bridge.
        /// </summary>
        [RosProperty("auto-isolate")]
        public bool AutoIsolate { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public BridgePort() {
            Priority = 0x80;
            PathCost = 10;
            Horizon = "none";
        }
    }
}
