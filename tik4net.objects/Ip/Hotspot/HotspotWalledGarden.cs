using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// /ip/hotspot/walled-garden: HTTP walled-garden rules for the HotSpot server. Rules are
    /// matched against HTTP requests from unauthenticated clients; matching "allow" rules let
    /// requests through without login. Evaluated in order (first match wins).
    /// </summary>
    [TikEntity("/ip/hotspot/walled-garden", IncludeDetails = true, IsOrdered = true)]
    public class HotspotWalledGarden
    {
        /// <summary>.id — primary key of the rule.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>action — what to do when the rule matches. Default: allow.</summary>
        [TikProperty("action", DefaultValue = "allow")]
        public WalledGardenAction Action { get; set; }

        /// <summary>server — HotSpot server name this rule applies to; empty means all servers.</summary>
        [TikProperty("server", DefaultValue = "")]
        public string Server { get; set; }

        /// <summary>src-address — source IP address or range of the unauthenticated client.</summary>
        [TikProperty("src-address", DefaultValue = "")]
        public string SrcAddress { get; set; }

        /// <summary>dst-host — destination hostname or wildcard (e.g. *.example.com).</summary>
        [TikProperty("dst-host", DefaultValue = "")]
        public string DstHost { get; set; }

        /// <summary>dst-port — destination port or port range to match.</summary>
        [TikProperty("dst-port", DefaultValue = "")]
        public string DstPort { get; set; }

        /// <summary>method — HTTP method to match (any/connect/delete/get/head/options/post/put/trace). Default: any.</summary>
        [TikProperty("method", DefaultValue = "any")]
        public string Method { get; set; }

        /// <summary>path — URL path pattern to match (without hostname, e.g. /images/*).</summary>
        [TikProperty("path", DefaultValue = "")]
        public string Path { get; set; }

        /// <summary>disabled — when yes, the rule is inactive.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — free-form annotation.</summary>
        [TikProperty("comment", DefaultValue = "")]
        public string Comment { get; set; }

        /// <summary>Human-readable rule summary.</summary>
        public override string ToString() => string.Format("{0} dst-host={1} path={2}", Action, DstHost, Path);
    }

    /// <summary>Action for <see cref="HotspotWalledGarden.Action"/>.</summary>
    public enum WalledGardenAction
    {
        /// <summary>allow — permit the matched unauthenticated request.</summary>
        [TikEnum("allow")] Allow,

        /// <summary>deny — block the matched unauthenticated request.</summary>
        [TikEnum("deny")] Deny,
    }
}
