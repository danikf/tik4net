using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/ntp/server — NTP server settings (singleton). RouterOS 7 NTP server
    /// can serve time over unicast, broadcast, multicast, and manycast. Requires the
    /// NTP client to be synchronized (or use-local-clock enabled) before serving time.
    /// <para>Note: <c>=detail=</c> is rejected by this menu; plain print is used.</para>
    /// <para>See also: https://help.mikrotik.com/docs/display/ROS/NTP</para>
    /// </summary>
    // IncludeDetails omitted — detail= is rejected by this singleton.
    [TikEntity("/system/ntp/server", IsSingleton = true)]
    public class SystemNtpServer
    {
        /// <summary>enabled — activates the NTP server. Default: no.</summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>broadcast — when yes, the NTP server sends broadcast NTP packets on all interfaces. Default: no.</summary>
        [TikProperty("broadcast", DefaultValue = "no")]
        public bool Broadcast { get; set; }

        /// <summary>multicast — when yes, the NTP server joins the NTP multicast group and serves multicast clients. Default: no.</summary>
        [TikProperty("multicast", DefaultValue = "no")]
        public bool Multicast { get; set; }

        /// <summary>manycast — when yes, the NTP server responds to manycast client requests. Default: no.</summary>
        [TikProperty("manycast", DefaultValue = "no")]
        public bool Manycast { get; set; }

        /// <summary>broadcast-addresses — comma-separated list of broadcast addresses used when broadcast=yes. Empty uses the interface broadcast address.</summary>
        [TikProperty("broadcast-addresses", DefaultValue = "")]
        public string BroadcastAddresses { get; set; }

        /// <summary>vrf — Virtual Routing and Forwarding instance for NTP server traffic. Default: main.</summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        /// <summary>use-local-clock — when yes, the router uses its own RTC as the NTP reference even without an upstream sync. Default: no.</summary>
        [TikProperty("use-local-clock", DefaultValue = "no")]
        public bool UseLocalClock { get; set; }

        /// <summary>local-clock-stratum — NTP stratum value advertised when use-local-clock=yes. Real default: 5; 0 is CLR sentinel (omitted on add).</summary>
        // Range 0–15; DefaultValue="0" so CLR default 0 is omitted on add (router applies 5).
        [TikProperty("local-clock-stratum", DefaultValue = "0")]
        public int LocalClockStratum { get; set; }

        /// <summary>auth-key — NTP authentication key name. Default: none (no authentication).</summary>
        [TikProperty("auth-key", DefaultValue = "none")]
        public string AuthKey { get; set; }

        /// <summary>Returns a human-readable summary of the NTP server settings.</summary>
        public override string ToString() => string.Format("ntp/server: enabled={0}, bcast={1}, mcast={2}", Enabled, Broadcast, Multicast);
    }
}
