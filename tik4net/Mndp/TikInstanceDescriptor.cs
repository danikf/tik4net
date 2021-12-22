using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace tik4net.Mndp
{
    /// <summary>
    /// Mikrotik instances visible by MNDP discovery protocol
    /// </summary>
    public struct TikInstanceDescriptor
    {
        /// <summary>Mikrotik identity</summary>
        public string Identity { get; }
        /// <summary>Mikrotik version (eq. 6.49.2 (stable))</summary>
        public string Version { get; }
        /// <summary>Mikrotik platform (eq. MikroTik)</summary>
        public string Platform;
        /// <summary>Mikrotik uptime</summary>
        public TimeSpan Uptime { get; }
        /// <summary>Mikrotik software</summary>
        public string SoftwareId { get; }
        /// <summary>Mikrotik board (eq. RB800)</summary>
        public string BoardName { get; }
        /// <summary>no idea :-)</summary>
        public string Unpack { get; }
        /// <summary>Mikrotik interface MAC</summary>
        public string Mac { get; }
        /// <summary>Mikrotik interface IP</summary>
        public IPAddress IPv4 { get; }
        /// <summary>Mikrotik interface IPv6 (can be empty)</summary>
        public string IPv6 { get; }
        /// <summary>Mikrotik interface name</summary>
        public string InterfaceName { get; }

        /// <summary>
        /// .ctor
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
                if (IPv4 != IPAddress.Any)
                    return IPv4.ToString();
                else if (!string.IsNullOrWhiteSpace(IPv6))
                    return IPv6.ToString();
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
