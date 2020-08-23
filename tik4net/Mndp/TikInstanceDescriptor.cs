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
        public string Identity;
        public string Version;
        public string Platform;
        public readonly TimeSpan Uptime;
        public readonly string SoftwareId;
        public readonly string BoardName;
        public readonly string Unpack;
        public readonly string Mac;
        public readonly string IPV6;
        public readonly string InterfaceName;
        public readonly IPAddress IPV4;

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
            IPV6 = ipv6;
            InterfaceName = interfaceName;
            IPV4 = iPV4;
        }

        /// <summary>
        /// IPV4 or IPV6 or MAC as fallback
        /// </summary>
        public string IpDescription
        {
            get
            {
                if (IPV4 != IPAddress.Any)
                    return IPV4.ToString();
                else if (!StringHelper.IsNullOrWhiteSpace(IPV6))
                    return IPV6.ToString();
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
