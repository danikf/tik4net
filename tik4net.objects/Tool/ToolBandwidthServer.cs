using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Tool
{
    /// <summary>
    /// /tool/bandwidth-server — built-in Bandwidth Test server settings (singleton).
    /// The bandwidth server accepts connections from MikroTik Bandwidth Test clients
    /// (BTest) for measuring throughput between two RouterOS devices.
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/Bandwidth+Test</para>
    /// </summary>
    // IncludeDetails omitted — plain print returns the full field set.
    [TikEntity("/tool/bandwidth-server", IsSingleton = true)]
    public class ToolBandwidthServer
    {
        /// <summary>enabled — activates the bandwidth test server. Default: yes.</summary>
        [TikProperty("enabled", DefaultValue = "yes")]
        public bool Enabled { get; set; }

        /// <summary>authenticate — when yes, only clients with matching credentials are accepted. Default: yes.</summary>
        [TikProperty("authenticate", DefaultValue = "yes")]
        public bool Authenticate { get; set; }

        /// <summary>allocate-udp-ports-from — first UDP port in the range allocated for UDP test sessions. Real default: 2000; set to 0 to omit on add.</summary>
        // Range 1000–65535; DefaultValue="0" so CLR default 0 is omitted on add (router applies 2000).
        [TikProperty("allocate-udp-ports-from", DefaultValue = "0")]
        public int AllocateUdpPortsFrom { get; set; }

        /// <summary>max-sessions — maximum number of simultaneous bandwidth-test sessions. Real default: 100; set to 0 to omit on add.</summary>
        // Range 1–1000; DefaultValue="0" so CLR default 0 is omitted on add (router applies 100).
        [TikProperty("max-sessions", DefaultValue = "0")]
        public int MaxSessions { get; set; }

        /// <summary>allowed-addresses4 — IPv4 address or prefix from which test connections are accepted. Empty means no restriction.</summary>
        [TikProperty("allowed-addresses4", DefaultValue = "")]
        public string/*IP/CIDR*/ AllowedAddresses4 { get; set; }

        /// <summary>allowed-addresses6 — IPv6 address or prefix from which test connections are accepted. Empty means no restriction.</summary>
        [TikProperty("allowed-addresses6", DefaultValue = "")]
        public string/*IPv6/CIDR*/ AllowedAddresses6 { get; set; }

        /// <summary>Returns a human-readable summary of the bandwidth server settings.</summary>
        public override string ToString() => string.Format("bandwidth-server: enabled={0}, authenticate={1}, max-sessions={2}", Enabled, Authenticate, MaxSessions);
    }
}
