using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/neighbor: Neighbor Discovery protocols allow finding devices compatible with MNDP, CDP,
    /// or LLDP in the Layer 2 broadcast domain. This is a read-only discovery table — entries are
    /// populated automatically by the router as neighbors are detected; they cannot be added or
    /// removed manually.
    /// https://help.mikrotik.com/docs/display/ROS/Neighbor+Discovery
    /// </summary>
    [TikEntity("/ip/neighbor", IncludeDetails = true, IsReadOnly = true)]
    public class IpNeighbor
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// interface: Name of the local interface through which the neighbor was discovered.
        /// </summary>
        [TikProperty("interface", IsReadOnly = true)]
        public string Interface { get; private set; }

        /// <summary>
        /// address: The highest IP address configured on the discovered device.
        /// </summary>
        [TikProperty("address", IsReadOnly = true)]
        public string Address { get; private set; }

        /// <summary>
        /// address4: IPv4 address of the discovered device.
        /// </summary>
        [TikProperty("address4", IsReadOnly = true)]
        public string Address4 { get; private set; }

        /// <summary>
        /// address6: IPv6 address of the discovered device.
        /// </summary>
        [TikProperty("address6", IsReadOnly = true)]
        public string Address6 { get; private set; }

        /// <summary>
        /// mac-address: MAC address of the remote device. /*MAC*/
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true)]
        public string MacAddress { get; private set; }

        /// <summary>
        /// identity: Configured system identity of the discovered device.
        /// </summary>
        [TikProperty("identity", IsReadOnly = true)]
        public string Identity { get; private set; }

        /// <summary>
        /// platform: Platform identifier string (e.g. "MikroTik").
        /// </summary>
        [TikProperty("platform", IsReadOnly = true)]
        public string Platform { get; private set; }

        /// <summary>
        /// version: Software version running on the discovered device.
        /// </summary>
        [TikProperty("version", IsReadOnly = true)]
        public string Version { get; private set; }

        /// <summary>
        /// board: RouterBoard hardware model of the discovered device (MikroTik devices only).
        /// </summary>
        [TikProperty("board", IsReadOnly = true)]
        public string Board { get; private set; }

        /// <summary>
        /// software-id: RouterOS software ID of the discovered device.
        /// </summary>
        [TikProperty("software-id", IsReadOnly = true)]
        public string SoftwareId { get; private set; }

        /// <summary>
        /// interface-name: Name of the remote interface through which discovery was received (reported via CDP).
        /// </summary>
        [TikProperty("interface-name", IsReadOnly = true)]
        public string InterfaceName { get; private set; }

        /// <summary>
        /// age: Time elapsed since the last discovery packet was received from this neighbor. /*time*/
        /// </summary>
        [TikProperty("age", IsReadOnly = true)]
        public string Age { get; private set; }

        /// <summary>
        /// uptime: Uptime of the remote device at the time of the last discovery packet. /*time*/
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true)]
        public string Uptime { get; private set; }

        /// <summary>
        /// ipv6: Whether IPv6 is enabled on the discovered device.
        /// </summary>
        [TikProperty("ipv6", IsReadOnly = true)]
        public bool Ipv6 { get; private set; }

        /// <summary>
        /// unpack: Packet compression/decompression method used in discovery packets.
        /// </summary>
        [TikProperty("unpack", IsReadOnly = true)]
        public string Unpack { get; private set; }

        /// <summary>
        /// system-caps: LLDP system capabilities advertised by the discovered device.
        /// </summary>
        [TikProperty("system-caps", IsReadOnly = true)]
        public string SystemCaps { get; private set; }

        /// <summary>
        /// system-caps-enabled: Subset of LLDP system capabilities that are currently enabled.
        /// </summary>
        [TikProperty("system-caps-enabled", IsReadOnly = true)]
        public string SystemCapsEnabled { get; private set; }

        /// <summary>
        /// discovered-by: Comma-separated list of discovery protocols (cdp, lldp, mndp) that reported this neighbor.
        /// </summary>
        [TikProperty("discovered-by", IsReadOnly = true)]
        public string DiscoveredBy { get; private set; }

        /// <summary>Human-readable identity of the neighbor.</summary>
        public override string ToString()
        {
            return string.Format("{0} ({1}) on {2}", Identity, Address, Interface);
        }
    }
}
