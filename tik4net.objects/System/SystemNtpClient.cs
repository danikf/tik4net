using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/ntp/client — NTP client configuration and status (singleton).
    /// RouterOS NTP client based on RFC 5905. Manages time synchronization with
    /// one or more NTP servers. Writable fields: enabled, mode, servers, vrf.
    /// Read-only status fields reflect the current synchronization state.
    /// </summary>
    [TikEntity("/system/ntp/client", IsSingleton = true)]
    public class SystemNtpClient
    {
        /// <summary>
        /// enabled — enable or disable NTP client time synchronization.
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>
        /// mode — operational mode for the NTP client.
        /// </summary>
        /// <seealso cref="NtpClientMode"/>
        [TikProperty("mode", DefaultValue = "unicast")]
        public NtpClientMode Mode { get; set; }

        /// <summary>
        /// servers — comma-separated list of NTP server addresses (FQDN, IPv4, IPv6, or IPv6 link-local).
        /// ROS 7 unified field (replaces ROS 6 primary-ntp / secondary-ntp).
        /// </summary>
        [TikProperty("servers")]
        public string Servers { get; set; }

        /// <summary>
        /// vrf — Virtual Routing and Forwarding instance used for NTP traffic. Default: main.
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        // --- Read-only status fields ---

        /// <summary>
        /// freq-drift — fractional frequency drift per unit time (ppm), read-only.
        /// </summary>
        [TikProperty("freq-drift", IsReadOnly = true)]
        public string FreqDrift { get; private set; }

        /// <summary>
        /// status — current NTP client synchronization state, read-only.
        /// </summary>
        /// <seealso cref="NtpClientStatus"/>
        [TikProperty("status", IsReadOnly = true)]
        public string Status { get; private set; }

        /// <summary>
        /// synced-server — IP address of the NTP server the client is currently synchronized to, read-only.
        /// </summary>
        [TikProperty("synced-server", IsReadOnly = true)]
        public string SyncedServer { get; private set; }

        /// <summary>
        /// synced-stratum — stratum level of the currently synced NTP server (1 = primary reference), read-only.
        /// </summary>
        [TikProperty("synced-stratum", IsReadOnly = true)]
        public string SyncedStratum { get; private set; }

        /// <summary>
        /// system-offset — offset of the NTP server clock relative to the local clock (milliseconds), read-only.
        /// </summary>
        [TikProperty("system-offset", IsReadOnly = true)]
        public string SystemOffset { get; private set; }

        /// <summary>
        /// Human-readable summary of the NTP client state.
        /// </summary>
        public override string ToString()
        {
            return string.Format("NTP client: enabled={0}, mode={1}, status={2}, synced-server={3}", Enabled, Mode, Status, SyncedServer);
        }
    }

    /// <summary>
    /// Operational mode for the NTP client (<see cref="SystemNtpClient.Mode"/>).
    /// </summary>
    public enum NtpClientMode
    {
        /// <summary>unicast — client queries a specific list of NTP servers (most common mode).</summary>
        [TikEnum("unicast")]
        Unicast,

        /// <summary>broadcast — client listens for NTP broadcast packets on the local network.</summary>
        [TikEnum("broadcast")]
        Broadcast,

        /// <summary>manycast — client sends requests to a multicast group and uses responding servers.</summary>
        [TikEnum("manycast")]
        Manycast,

        /// <summary>multicast — client listens for NTP multicast packets.</summary>
        [TikEnum("multicast")]
        Multicast,
    }
}
