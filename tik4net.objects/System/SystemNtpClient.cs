using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tik4net.Objects.System
{
    /// <summary>
    /// /system/ntp/client — NTP client configuration and status (singleton).
    /// RouterOS NTP client based on RFC 5905. Manages time synchronization with
    /// one or more NTP servers. Writable fields: enabled, mode, servers, vrf on RouterOS 7; enabled, primary-ntp,
    /// secondary-ntp, server-dns-names on RouterOS 6, whose menu has a status of its own.
    /// Read-only status fields reflect the current synchronization state.
    /// </summary>
    [TikEntity("/system/ntp/client", IsSingleton = true)]
    public class SystemNtpClient
    {
        /// <summary>
        /// enabled — enable or disable NTP client time synchronization.
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no", WinboxLabel = "Enabled")]
        public TikField<bool?> Enabled { get; set; }

        /// <summary>
        /// mode — operational mode for the NTP client. Writable from RouterOS 7; RouterOS 6 only reports it and refuses
        /// a write with <c>unknown parameter</c>.
        /// </summary>
        /// <seealso cref="NtpClientMode"/>
        [TikProperty("mode", DefaultValue = "unicast", WinboxLabel = "Mode")]
        public TikField<NtpClientMode?> Mode { get; set; }

        /// <summary>
        /// servers — comma-separated list of NTP server addresses (FQDN, IPv4, IPv6, or IPv6 link-local).
        /// RouterOS 7; RouterOS 6 has <see cref="PrimaryNtp"/>, <see cref="SecondaryNtp"/> and <see cref="ServerDnsNames"/>.
        /// </summary>
        [TikProperty("servers", WinboxLabel = "NTP Servers", MinRouterOs = "7")]
        public TikField<TikValueList<string>?> Servers { get; set; }

        /// <summary>
        /// vrf — Virtual Routing and Forwarding instance used for NTP traffic. Default: main.
        /// </summary>
        [TikProperty("vrf", DefaultValue = "main", WinboxLabel = "VRF", MinRouterOs = "7")]
        public TikField<string?> Vrf { get; set; }

        /// <summary>
        /// primary-ntp — RouterOS 6: the first NTP server's IP address; <c>0.0.0.0</c> for none. RouterOS 7 has
        /// <see cref="Servers"/> instead.
        /// </summary>
        [TikProperty("primary-ntp", DefaultValue = "0.0.0.0", MaxRouterOs = "6")]
        public TikField<string?> PrimaryNtp { get; set; }

        /// <summary>
        /// secondary-ntp — RouterOS 6: the second NTP server's IP address; <c>0.0.0.0</c> for none.
        /// </summary>
        [TikProperty("secondary-ntp", DefaultValue = "0.0.0.0", MaxRouterOs = "6")]
        public TikField<string?> SecondaryNtp { get; set; }

        /// <summary>
        /// server-dns-names — RouterOS 6: NTP servers by DNS name, resolved into <see cref="DynamicServers"/>.
        /// </summary>
        [TikProperty("server-dns-names", MaxRouterOs = "6")]
        public TikField<TikValueList<string>?> ServerDnsNames { get; set; }

        // --- Read-only status fields ---

        /// <summary>
        /// dynamic-servers — RouterOS 6: the addresses <see cref="ServerDnsNames"/> resolved to, read-only.
        /// </summary>
        [TikProperty("dynamic-servers", IsReadOnly = true, MaxRouterOs = "6")]
        public TikField<TikValueList<string>?> DynamicServers { get; private set; }

        /// <summary>
        /// poll-interval — RouterOS 6: the current interval between two requests to the server, read-only.
        /// </summary>
        [TikProperty("poll-interval", IsReadOnly = true, MaxRouterOs = "6")]
        public TikField<TikDuration?> PollInterval { get; private set; }

        /// <summary>
        /// active-server — RouterOS 6: the address of the server the client synchronizes with, read-only.
        /// </summary>
        [TikProperty("active-server", IsReadOnly = true, MaxRouterOs = "6")]
        public TikField<string?> ActiveServer { get; private set; }

        /// <summary>
        /// last-update-from — RouterOS 6: the address of the server that sent the last accepted update, read-only.
        /// </summary>
        [TikProperty("last-update-from", IsReadOnly = true, MaxRouterOs = "6")]
        public TikField<string?> LastUpdateFrom { get; private set; }

        /// <summary>
        /// last-update-before — RouterOS 6: how long ago the last update was accepted, read-only.
        /// </summary>
        [TikProperty("last-update-before", IsReadOnly = true, MaxRouterOs = "6")]
        public TikField<TikDuration?> LastUpdateBefore { get; private set; }

        /// <summary>
        /// last-adjustment — RouterOS 6: the clock correction the last update made, negative when the clock was set
        /// back, read-only.
        /// </summary>
        [TikProperty("last-adjustment", IsReadOnly = true, MaxRouterOs = "6")]
        public TikField<TikDuration?> LastAdjustment { get; private set; }

        /// <summary>
        /// freq-drift — fractional frequency drift per unit time (ppm), read-only.
        /// </summary>
        [TikProperty("freq-drift", IsReadOnly = true, WinboxLabel = "Freq. Drift", MinRouterOs = "7")]
        public TikField<string?> FreqDrift { get; private set; }

        /// <summary>
        /// status — current NTP client synchronization state, read-only.
        /// </summary>
        [TikProperty("status", IsReadOnly = true, WinboxLabel = "Status", MinRouterOs = "7")]
        public TikField<string?> Status { get; private set; }

        /// <summary>
        /// synced-server — IP address of the NTP server the client is currently synchronized to, read-only.
        /// </summary>
        [TikProperty("synced-server", IsReadOnly = true, WinboxLabel = "Synced Server", MinRouterOs = "7")]
        public TikField<string?> SyncedServer { get; private set; }

        /// <summary>
        /// synced-stratum — stratum level of the currently synced NTP server (1 = primary reference), read-only.
        /// </summary>
        [TikProperty("synced-stratum", IsReadOnly = true, WinboxLabel = "Synced Stratum", MinRouterOs = "7")]
        public TikField<string?> SyncedStratum { get; private set; }

        /// <summary>
        /// system-offset — offset of the NTP server clock relative to the local clock (milliseconds), read-only.
        /// </summary>
        [TikProperty("system-offset", IsReadOnly = true, WinboxLabel = "System Offset", MinRouterOs = "7")]
        public TikField<string?> SystemOffset { get; private set; }

        /// <summary>
        /// Human-readable summary of the NTP client state.
        /// </summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "NTP client: enabled={0}, mode={1}, status={2}, synced-server={3}", Enabled, Mode, Status, SyncedServer);
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
