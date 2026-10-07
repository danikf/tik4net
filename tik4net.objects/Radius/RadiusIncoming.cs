using System;

namespace tik4net.Objects.Radius
{
    /// <summary>
    /// /radius/incoming — the router's listener for unsolicited RADIUS messages (singleton): Disconnect and
    /// CoA (Change of Authorization) requests a RADIUS server sends to end or change a session (RFC 3576).
    /// https://help.mikrotik.com/docs/display/ROS/RADIUS
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton (7.24.5 and 6.49.13).
    [TikEntity("/radius/incoming", IsSingleton = true)]
    public class RadiusIncoming
    {
        /// <summary>accept — whether to accept unsolicited messages. Default: no.</summary>
        [TikProperty("accept", DefaultValue = "no", WinboxLabel = "Accept")]
        public TikField<bool?> Accept { get; set; }

        /// <summary>port — the UDP port the router listens on. Default: 3799.</summary>
        [TikProperty("port", DefaultValue = "3799", WinboxLabel = "Port")]
        public TikField<int?> Port { get; set; }

        /// <summary>vrf — the VRF the listener runs in (RouterOS 7.x; 6.x has no such field). Default: main.</summary>
        [TikProperty("vrf", DefaultValue = "main", WinboxLabel = "VRF", MinRouterOs = "7")]
        public TikField<string?> Vrf { get; set; }
    }
}
