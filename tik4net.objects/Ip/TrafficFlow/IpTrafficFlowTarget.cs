using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.TrafficFlow
{
    /// <summary>
    /// /ip/traffic-flow/target: Table of NetFlow/IPFIX export targets — each row defines a
    /// collector host that will receive Traffic-Flow packets exported by the router.
    /// Supports NetFlow versions 1, 5, 9 and IPFIX. Version 9 and IPFIX additionally
    /// support template-based records controlled by <see cref="V9TemplateRefresh"/> and
    /// <see cref="V9TemplateTimeout"/>.
    /// </summary>
    [TikEntity("/ip/traffic-flow/target", IncludeDetails = true)]
    public class IpTrafficFlowTarget
    {
        /// <summary>Enumeration of NetFlow/IPFIX export protocol versions.</summary>
        public enum NetFlowVersion
        {
            /// <summary>Original NetFlow format with basic IP packet information.</summary>
            [TikEnum("1")] V1,
            /// <summary>Enhanced format supporting ToS, TCP flags, and AS numbers.</summary>
            [TikEnum("5")] V5,
            /// <summary>Template-based format supporting IPv4 and IPv6.</summary>
            [TikEnum("9")] V9,
            /// <summary>IETF standardised protocol with extended capabilities including multicast support.</summary>
            [TikEnum("ipfix")] Ipfix,
        }

        /// <summary>.id — primary key of row.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// dst-address — IP address of the host which receives Traffic-Flow statistic packets
        /// from the router.
        /// </summary>
        [TikProperty("dst-address", IsMandatory = true)]
        public string/*IPv4*/ DstAddress { get; set; }

        /// <summary>
        /// src-address — IP address used as the source when sending Traffic-Flow statistics.
        /// Default: 0.0.0.0 (router picks the outgoing interface address automatically).
        /// </summary>
        [TikProperty("src-address", DefaultValue = "0.0.0.0")]
        public string/*IPv4*/ SrcAddress { get; set; }

        /// <summary>
        /// port — UDP port of the receiving host.
        /// Default: 2055
        /// </summary>
        [TikProperty("port")] // router default 2055; omitted on add when left 0
        public int Port { get; set; }

        /// <summary>
        /// version — NetFlow/IPFIX format version to use when exporting records.
        /// <seealso cref="NetFlowVersion"/>
        /// </summary>
        [TikProperty("version")]
        public NetFlowVersion Version { get; set; }

        /// <summary>
        /// v9-template-refresh — number of packets after which the template record is
        /// re-sent to the receiving host. Applies only to NetFlow v9 and IPFIX.
        /// Default: 20
        /// </summary>
        [TikProperty("v9-template-refresh")] // router default 20; omitted on add when left 0
        public int V9TemplateRefresh { get; set; }

        /// <summary>
        /// v9-template-timeout — maximum time interval after which the template is sent even
        /// if the packet count threshold has not been reached. Applies only to NetFlow v9 and IPFIX.
        /// Default: 30m
        /// </summary>
        [TikProperty("v9-template-timeout", DefaultValue = "30m")]
        public string/*time*/ V9TemplateTimeout { get; set; }

        /// <summary>
        /// disabled — whether this export target is administratively disabled.
        /// Default: false
        /// </summary>
        [TikProperty("disabled", DefaultValue = "false")]
        public bool Disabled { get; set; }

        /// <summary>Returns a human-readable description of this export target.</summary>
        public override string ToString() => string.Format("{0}:{1} (v{2})", DstAddress, Port, Version);
    }
}
