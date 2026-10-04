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
    [TikEntity("/ip/neighbor", IncludeDetails = true, SupportedOperations = TikEntityOperations.None)]
    public class IpNeighbor
    {
        /// <summary>
        /// .id: primary key of row
        /// </summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>
        /// interface: Name of the local interface through which the neighbor was discovered.
        /// </summary>
        [TikProperty("interface", IsReadOnly = true, WinboxLabel = "Interface")]
        public TikField<TikValueList<string>?> Interface { get; private set; }

        /// <summary>
        /// address: The highest IP address configured on the discovered device.
        /// </summary>
        [TikProperty("address", IsReadOnly = true, WinboxLabel = "IP Address")]
        public TikField<string?> Address { get; private set; }

        /// <summary>
        /// address4: IPv4 address of the discovered device.
        /// </summary>
        [TikProperty("address4", IsReadOnly = true)]
        public TikField<string?> Address4 { get; private set; }

        /// <summary>
        /// address6: IPv6 address of the discovered device.
        /// </summary>
        [TikProperty("address6", IsReadOnly = true, WinboxLabel = "IPv6 Address")]
        public TikField<string?> Address6 { get; private set; }

        /// <summary>
        /// mac-address: MAC address of the remote device. /*MAC*/
        /// </summary>
        [TikProperty("mac-address", IsReadOnly = true, WinboxLabel = "MAC Address")]
        public TikField<string?> MacAddress { get; private set; }

        /// <summary>
        /// identity: Configured system identity of the discovered device.
        /// </summary>
        [TikProperty("identity", IsReadOnly = true, WinboxLabel = "Identity")]
        public TikField<string?> Identity { get; private set; }

        /// <summary>
        /// platform: Platform identifier string (e.g. "MikroTik").
        /// </summary>
        [TikProperty("platform", IsReadOnly = true, WinboxLabel = "Platform")]
        public TikField<string?> Platform { get; private set; }

        /// <summary>
        /// version: Software version running on the discovered device.
        /// </summary>
        [TikProperty("version", IsReadOnly = true, WinboxLabel = "Version")]
        public TikField<string?> Version { get; private set; }

        /// <summary>
        /// board: RouterBoard hardware model of the discovered device (MikroTik devices only).
        /// </summary>
        [TikProperty("board", IsReadOnly = true, WinboxLabel = "Board Name")]
        public TikField<string?> Board { get; private set; }

        /// <summary>
        /// software-id: RouterOS software ID of the discovered device.
        /// </summary>
        [TikProperty("software-id", IsReadOnly = true, WinboxLabel = "Software ID")]
        public TikField<string?> SoftwareId { get; private set; }

        /// <summary>
        /// interface-name: Name of the remote interface through which discovery was received (reported via CDP).
        /// </summary>
        [TikProperty("interface-name", IsReadOnly = true, WinboxLabel = "Interface Name")]
        public TikField<string?> InterfaceName { get; private set; }

        /// <summary>
        /// age: Time elapsed since the last discovery packet was received from this neighbor. /*time*/
        /// </summary>
        [TikProperty("age", IsReadOnly = true, WinboxLabel = "Age")]
        public TikField<TikDuration?> Age { get; private set; }

        /// <summary>
        /// uptime: Uptime of the remote device at the time of the last discovery packet. /*time*/
        /// </summary>
        [TikProperty("uptime", IsReadOnly = true, WinboxLabel = "Uptime")]
        public TikField<TikDuration?> Uptime { get; private set; }

        /// <summary>
        /// ipv6: Whether IPv6 is enabled on the discovered device.
        /// </summary>
        [TikProperty("ipv6", IsReadOnly = true, WinboxLabel = "IPv6")]
        public TikField<bool?> Ipv6 { get; private set; }

        /// <summary>
        /// unpack: Packet compression/decompression method used in discovery packets.
        /// </summary>
        [TikProperty("unpack", IsReadOnly = true, WinboxLabel = "Unpacking")]
        public TikField<string?> Unpack { get; private set; }

        /// <summary>
        /// system-caps: LLDP system capabilities advertised by the discovered device.
        /// </summary>
        [TikProperty("system-caps", IsReadOnly = true, WinboxLabel = "System Caps")]
        public TikField<TikValueList<string>?> SystemCaps { get; private set; }

        /// <summary>
        /// system-caps-enabled: Subset of LLDP system capabilities that are currently enabled.
        /// </summary>
        [TikProperty("system-caps-enabled", IsReadOnly = true, WinboxLabel = "System Caps Enabled")]
        public TikField<TikValueList<string>?> SystemCapsEnabled { get; private set; }

        /// <summary>
        /// discovered-by: Comma-separated list of discovery protocols (cdp, lldp, mndp) that reported this neighbor.
        /// </summary>
        [TikProperty("discovered-by", IsReadOnly = true, WinboxLabel = "Discovered By")]
        public TikField<string?> DiscoveredBy { get; private set; }

        /// <summary>Human-readable identity of the neighbor.</summary>
        public override string ToString()
        {
            return string.Format("{0} ({1}) on {2}", Identity, Address, Interface);
        }
    }
}
