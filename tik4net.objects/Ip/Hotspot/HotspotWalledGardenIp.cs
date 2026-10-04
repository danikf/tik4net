using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// /ip/hotspot/walled-garden/ip: IP-level walled-garden rules for the HotSpot server. Unlike
    /// /ip/hotspot/walled-garden (HTTP-layer), these rules match at the IP/transport layer and apply
    /// to all protocols, enabling access to non-HTTP services (e.g. DNS, ICMP) for unauthenticated clients.
    /// </summary>
    [TikEntity("/ip/hotspot/walled-garden/ip", IncludeDetails = true, IsOrdered = true)]
    public class HotspotWalledGardenIp
    {
        /// <summary>.id — primary key of the rule.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>action — what to do when the rule matches (firewall-style: accept/drop). Default: accept.</summary>
        [TikProperty("action", DefaultValue = "accept", WinboxLabel = "Action")]
        public TikField<WalledGardenIpAction?> Action { get; set; }

        /// <summary>server — HotSpot server name this rule applies to; empty means all servers.</summary>
        [TikProperty("server", DefaultValue = "", WinboxLabel = "Server", Negatable = true)]
        public TikField<string?> Server { get; set; }

        /// <summary>protocol — IP protocol to match (e.g. tcp, udp, icmp). Empty = any.</summary>
        [TikProperty("protocol", DefaultValue = "", WinboxLabel = "Protocol", Negatable = true)]
        public TikField<string?> Protocol { get; set; }

        /// <summary>src-address — source IP address or range of the unauthenticated client.</summary>
        [TikProperty("src-address", DefaultValue = "", WinboxLabel = "Src. Address", Negatable = true)]
        public TikField<string?> SrcAddress { get; set; }

        /// <summary>src-address-list — source address list name to match.</summary>
        [TikProperty("src-address-list", DefaultValue = "", WinboxLabel = "Src. Address List", Negatable = true)]
        public TikField<string?> SrcAddressList { get; set; }

        /// <summary>dst-address — destination IP address or range.</summary>
        [TikProperty("dst-address", DefaultValue = "", WinboxLabel = "Dst. Address", Negatable = true)]
        public TikField<string?> DstAddress { get; set; }

        /// <summary>dst-address-list — destination address list name to match.</summary>
        [TikProperty("dst-address-list", DefaultValue = "", WinboxLabel = "Dst. Address List", Negatable = true)]
        public TikField<string?> DstAddressList { get; set; }

        /// <summary>dst-host — destination hostname to resolve and match (useful for dynamic IPs).</summary>
        [TikProperty("dst-host", DefaultValue = "", WinboxLabel = "Dst. Host")]
        public TikField<string?> DstHost { get; set; }

        /// <summary>dst-port — destination port or port range to match (e.g. 80 or 80-90).</summary>
        [TikProperty("dst-port", DefaultValue = "", WinboxLabel = "Dst. Port", Negatable = true)]
        public TikField<string?> DstPort { get; set; }

        /// <summary>disabled — when yes, the rule is inactive.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>comment — free-form annotation.</summary>
        [TikProperty("comment", DefaultValue = "")]
        public TikField<string?> Comment { get; set; }

        /// <summary>Human-readable rule summary.</summary>
        public override string ToString() => string.Format("{0} proto={1} src={2} dst={3}:{4}", Action, Protocol, SrcAddress, DstAddress, DstPort);
    }

    /// <summary>IP-layer action for <see cref="HotspotWalledGardenIp.Action"/> (firewall-style: accept/drop).</summary>
    public enum WalledGardenIpAction
    {
        /// <summary>accept — permit the matched traffic without requiring login.</summary>
        [TikEnum("accept")] Accept,

        /// <summary>drop — silently discard the matched traffic.</summary>
        [TikEnum("drop")] Drop,
        /// <summary>reject — Deny access and answer with an ICMP reject, rather than silently dropping.</summary>
        [TikEnum("reject")] Reject,
    }
}
