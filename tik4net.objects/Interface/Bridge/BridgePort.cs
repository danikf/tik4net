using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Bridge
{
    /// <summary>
    /// interface/bridge/port: Port submenu is used to enslave interfaces in a particular bridge interface.
    /// </summary>
    [TikEntity("/interface/bridge/port", IncludeDetails = true)]
    public class BridgePort
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// interface: Name of the interface
        /// </summary>
        [TikProperty("interface", WinboxLabel = "Interface")]
        public TikValue<string?> Interface { get; set; }

        /// <summary>
        /// bridge:  The bridge interface the respective interface is grouped in
        /// </summary>
        [TikProperty("bridge", WinboxLabel = "Bridge")]
        public TikValue<string?> Bridge { get; set; }

        /// <summary>
        /// priority: The priority of the interface in comparison with other going to the same subnet.
        /// The router writes it in hex, and the CLI before 7.24 in decimal — both read to the same
        /// <see cref="TikHexNumber"/>, which is written back in hex: RouterOS 7.24 refuses a decimal one.
        /// Router default: <c>0x80</c>.
        /// </summary>
        [TikProperty("priority", DefaultValue = "0x80", WinboxLabel = "Priority")]
        public TikValue<TikHexNumber?> Priority { get; set; }

        /// <summary>
        /// path-cost: Path cost to the interface, used by STP to determine the "best" path
        /// </summary>
        [TikProperty("path-cost", DefaultValue = "10", WinboxLabel = "Path Cost")]
        public TikValue<int?> PathCost { get; set; }

        /// <summary>
        /// horizon: Use split horizon bridging to prevent bridging loops.  read more»
        /// </summary>
        [TikProperty("horizon", DefaultValue = "none", WinboxLabel = "Horizon")]
        public TikValue<string?> Horizon { get; set; }

        /// <summary>
        /// edge: Set port as edge port or non-edge port, or enable automatic detection. Edge ports are connected to a LAN that has no other bridges attached. If the port is configured to discover edge port then as soon as the bridge detects a BPDU coming to an edge port, the port becomes a non-edge port.
        /// </summary>
        [TikProperty("edge", DefaultValue = "auto", WinboxLabel = "Edge")]
        public TikValue<string?> Edge { get; set; }

        /// <summary>
        /// point-to-point: 
        /// </summary>
        [TikProperty("point-to-point", DefaultValue = "auto", WinboxLabel = "Point To Point")]
        public TikValue<string?> PointToPoint { get; set; }

        /// <summary>
        /// external-fdb: Whether to use wireless registration table to speed up bridge host learning
        /// </summary>
        [TikProperty("external-fdb", DefaultValue = "auto")]
        public TikValue<string?> ExternalFdb { get; set; }

        /// <summary>
        /// auto-isolate: Prevents STP blocking port from erroneously moving into a forwarding state if no BPDU's are received on the bridge.
        /// </summary>
        [TikProperty("auto-isolate", DefaultValue = "no", WinboxLabel = "Auto Isolate")]
        public TikValue<bool?> AutoIsolate { get; set; }

    }
}
