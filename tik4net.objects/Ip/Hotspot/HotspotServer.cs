using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Hotspot
{
    /// <summary>
    /// /ip/hotspot: HotSpot server instances. Each entry binds the HotSpot service to a specific
    /// interface and references a server profile that controls authentication, HTML pages, and
    /// RADIUS settings. Use <see cref="TikConnectionExtensions.LoadAll{T}"/> to enumerate servers.
    /// </summary>
    [TikEntity("/ip/hotspot", IncludeDetails = true)]
    public class HotspotServer
    {
        /// <summary>.id — primary key of the server entry.</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        /// <summary>name — descriptive name for this HotSpot server instance.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>interface — interface on which the HotSpot server is running.</summary>
        [TikProperty("interface", IsMandatory = true)]
        public string Interface { get; set; }

        /// <summary>profile — server profile from /ip/hotspot/profile to use. Default: default.</summary>
        [TikProperty("profile", DefaultValue = "default")]
        public string Profile { get; set; }

        /// <summary>address-pool — IP pool for client address assignment. Default: none.</summary>
        [TikProperty("address-pool", DefaultValue = "none")]
        public string AddressPool { get; set; }

        /// <summary>addresses-per-mac — maximum number of simultaneous clients per MAC address. Default: unlimited.</summary>
        [TikProperty("addresses-per-mac", DefaultValue = "unlimited")]
        public string AddressesPerMac { get; set; }

        /// <summary>idle-timeout — how long an idle (no traffic) client remains connected. Default: none (disabled).</summary>
        [TikProperty("idle-timeout", DefaultValue = "none")]
        public string/*time|none*/ IdleTimeout { get; set; }

        /// <summary>keepalive-timeout — interval for checking that a client's host is still reachable. Default: none.</summary>
        [TikProperty("keepalive-timeout", DefaultValue = "none")]
        public string/*time|none*/ KeepaliveTimeout { get; set; }

        /// <summary>login-timeout — maximum time allowed for login after initial redirect. Default: none.</summary>
        [TikProperty("login-timeout", DefaultValue = "none")]
        public string/*time|none*/ LoginTimeout { get; set; }

        /// <summary>disabled — when yes, the server is inactive.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>Human-readable server summary.</summary>
        public override string ToString() => string.Format("{0} ({1})", Name, Interface);
    }
}
