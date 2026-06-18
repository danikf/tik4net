using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Firewall
{
    /// <summary>
    /// /ip/firewall/layer7-protocol
    /// Layer 7 protocol definitions used to match network traffic by inspecting application-layer
    /// data (NBAR-style regexp matching). Each entry defines a name and a regular expression
    /// that is tested against the first 10 packets or 2 KB of a TCP/UDP stream.
    /// </summary>
    [TikEntity("/ip/firewall/layer7-protocol", IncludeDetails = true)]
    public class FirewallLayer7Protocol
    {
        /// <summary>.id — primary key of row</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>
        /// name — unique name for this layer 7 protocol definition.
        /// WinBox: "Name"
        /// </summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>
        /// regexp — POSIX extended regular expression matched against the first 10 packets
        /// or 2048 bytes of a TCP/UDP connection payload. The match is case-insensitive.
        /// WinBox: "Regexp"
        /// </summary>
        [TikProperty("regexp")]
        public string Regexp { get; set; }

        /// <summary>
        /// comment — free-form descriptive text for this entry.
        /// </summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
