using System;
using System.Net;

namespace tik4net.Mndp
{
    /// <summary>
    /// A MikroTik router as one MNDP (neighbor discovery) announcement describes it.
    /// </summary>
    public struct TikInstanceDescriptor
    {
        /// <summary>The router's identity (<c>/system/identity</c> name).</summary>
        public string Identity { get; }
        /// <summary>The RouterOS version, e.g. <c>6.49.2 (stable)</c>.</summary>
        public string Version { get; }
        /// <summary>The platform name, e.g. <c>MikroTik</c>.</summary>
        public string Platform { get; }
        /// <summary>The router's uptime at the time of the announcement; zero when the announcement carries none.</summary>
        public TimeSpan Uptime { get; }
        /// <summary>The RouterOS software ID, as <c>/system/license</c> shows it.</summary>
        public string SoftwareId { get; }
        /// <summary>The board name, e.g. <c>RB800</c>.</summary>
        public string BoardName { get; }
        /// <summary>
        /// Raw MNDP field 14, kept verbatim because its meaning is not established. It is surfaced rather
        /// than dropped so that a caller who works out what a router puts here is not blocked by us.
        /// </summary>
        public string Unpack { get; }
        /// <summary>The MAC address of the interface that sent the announcement, as colon-separated hex.</summary>
        public string Mac { get; }
        /// <summary>The IPv4 address of the interface that sent the announcement; <see cref="IPAddress.Any"/> (<c>0.0.0.0</c>) when the announcement carries none.</summary>
        public IPAddress IPv4 { get; }
        /// <summary>The IPv6 address of the interface that sent the announcement, in standard textual form (<c>fe80::1</c>); empty when the announcement carries none.</summary>
        public string IPv6 { get; }
        /// <summary>The router's name of the interface that sent the announcement, e.g. <c>ether1</c>.</summary>
        public string InterfaceName { get; }

        /// <summary>
        /// Creates the descriptor from the fields of one MNDP announcement.
        /// </summary>
        internal TikInstanceDescriptor(string identity, string version, string platform, TimeSpan uptime, string softwareId, string boardName,
            string unpack, string mac, string ipv6, string interfaceName, IPAddress iPV4)
        {
            Identity = identity;
            Version = version;
            Platform = platform;
            Uptime = uptime;
            SoftwareId = softwareId;
            BoardName = boardName;
            Unpack = unpack;
            Mac = mac;
            IPv6 = ipv6;
            InterfaceName = interfaceName;
            IPv4 = iPV4;
        }

        /// <summary>
        /// IPV4 or IPV6 or MAC as fallback
        /// </summary>
        public string IpDescription
        {
            get
            {
                if (IPv4 != null && !IPAddress.Any.Equals(IPv4))
                    return IPv4.ToString();
                else if (!string.IsNullOrWhiteSpace(IPv6))
                    return IPv6;
                else
                    return Mac;
            }
        }

        /// <summary>
        /// Description of <see cref="TikInstanceDescriptor"/>
        /// </summary>
        public override string ToString()
        {
            return $"{IpDescription} - {Identity} - {Version}@{BoardName} [{InterfaceName}]";
        }
    }
}
