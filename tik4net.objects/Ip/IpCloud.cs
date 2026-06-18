using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// ip/cloud: MikroTik provides cloud services for RouterBOARD devices connected to the Internet,
    /// including DDNS, time updates, backups, and relay services to ease configuration and monitoring.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("ip/cloud", IsSingleton = true)]
    public class IpCloud
    {
        /// <summary>ddns-enabled — enables DDNS; if <c>auto</c>, activates only when Back To Home is enabled.</summary>
        /// <seealso cref="DdnsEnabledMode"/>
        [TikProperty("ddns-enabled", DefaultValue = "auto")]
        public DdnsEnabledMode DdnsEnabled { get; set; }

        /// <summary>ddns-update-interval — sets the interval for DDNS connection attempts; <c>none</c> lets the router check the IP internally.</summary>
        [TikProperty("ddns-update-interval", DefaultValue = "none")]
        public string/*time*/ DdnsUpdateInterval { get; set; }

        /// <summary>update-time — synchronises the device clock with the cloud server when no NTP/SNTP client is enabled.</summary>
        [TikProperty("update-time", DefaultValue = "yes")]
        public bool UpdateTime { get; set; }

        // --- Read-only properties ---

        /// <summary>public-address — IPv4 address sent to the cloud server (visible after a successful request).</summary>
        [TikProperty("public-address", IsReadOnly = true)]
        public string/*IPv4 address*/ PublicAddress { get; private set; }

        /// <summary>public-address-ipv6 — IPv6 address sent to the cloud server (visible after a successful request).</summary>
        [TikProperty("public-address-ipv6", IsReadOnly = true)]
        public string/*IPv6 address*/ PublicAddressIpv6 { get; private set; }

        /// <summary>dns-name — assigned DNS name in the form <c>&lt;12-char-serial&gt;.sn.mynetname.net</c>.</summary>
        [TikProperty("dns-name", IsReadOnly = true)]
        public string DnsName { get; private set; }

        /// <summary>status — current cloud service state (e.g. updating, updated, error).</summary>
        [TikProperty("status", IsReadOnly = true)]
        public string Status { get; private set; }

        /// <summary>warning — alert raised when the device IP differs from the UDP-header IP (e.g. when behind NAT).</summary>
        [TikProperty("warning", IsReadOnly = true)]
        public string Warning { get; private set; }

        /// <summary>Human-readable summary of the cloud state.</summary>
        public override string ToString() => string.Format("ddns-enabled={0} dns-name={1} status={2}", DdnsEnabled, DnsName, Status);
    }

    /// <summary>ddns-enabled modes for <see cref="IpCloud.DdnsEnabled"/>.</summary>
    public enum DdnsEnabledMode
    {
        /// <summary>no — DDNS is disabled.</summary>
        [TikEnum("no")] No,

        /// <summary>yes — DDNS is always enabled.</summary>
        [TikEnum("yes")] Yes,

        /// <summary>auto — DDNS activates only when Back To Home is enabled.</summary>
        [TikEnum("auto")] Auto,
    }
}
