using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip.Proxy
{
    /// <summary>
    /// /ip/proxy: MikroTik RouterOS performs proxying of HTTP and HTTP-proxy (for FTP and HTTP
    /// protocols) requests. The proxy service functions as an internet cache, storing requested
    /// objects closer to users to accelerate browsing speeds, and enabling content filtering and
    /// access control via /ip/proxy/access rules.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// Note: this menu rejects =detail=, so IncludeDetails is omitted.
    /// </summary>
    [TikEntity("/ip/proxy", IsSingleton = true)]
    public class IpProxy
    {
        /// <summary>enabled — enables or disables the web proxy service.</summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public TikValue<bool?> Enabled { get; set; }

        /// <summary>port — TCP port the proxy listens on. Default: 8080.</summary>
        [TikProperty("port", DefaultValue = "8080")]
        public TikValue<int?> Port { get; set; }

        /// <summary>src-address — source address used for outbound proxy connections. Default: 0.0.0.0 (any).</summary>
        [TikProperty("src-address", DefaultValue = "0.0.0.0")]
        public TikValue<string?> SrcAddress { get; set; }

        /// <summary>anonymous — when yes, does not pass client IP via X-Forwarded-For header.</summary>
        [TikProperty("anonymous", DefaultValue = "no")]
        public TikValue<bool?> Anonymous { get; set; }

        /// <summary>parent-proxy — IP address of the upstream (parent) proxy server. Default: 0.0.0.0 (none).</summary>
        [TikProperty("parent-proxy", DefaultValue = "0.0.0.0")]
        public TikValue<string?> ParentProxy { get; set; }

        /// <summary>parent-proxy-port — port number of the upstream proxy. Default: 0 (none).</summary>
        [TikProperty("parent-proxy-port", DefaultValue = "0")]
        public TikValue<int?> ParentProxyPort { get; set; }

        /// <summary>cache-administrator — e-mail address of the proxy administrator, shown on error pages. Default: webmaster.</summary>
        [TikProperty("cache-administrator", DefaultValue = "webmaster")]
        public TikValue<string?> CacheAdministrator { get; set; }

        /// <summary>max-cache-size — total cache size limit in KiB; accepts "none", "unlimited", or a number. Default: unlimited.</summary>
        [TikProperty("max-cache-size", DefaultValue = "unlimited")]
        public TikValue<string?> MaxCacheSize { get; set; }

        /// <summary>max-cache-object-size — maximum size of a single cached object in KiB. Default: 2048.</summary>
        [TikProperty("max-cache-object-size", DefaultValue = "2048")]
        public TikValue<int?> MaxCacheObjectSize { get; set; }

        /// <summary>cache-on-disk — enables storing cached objects on disk.</summary>
        [TikProperty("cache-on-disk", DefaultValue = "no")]
        public TikValue<bool?> CacheOnDisk { get; set; }

        /// <summary>cache-path — directory path where disk cache is stored. Default: web-proxy.</summary>
        [TikProperty("cache-path", DefaultValue = "web-proxy")]
        public TikValue<string?> CachePath { get; set; }

        /// <summary>max-client-connections — maximum number of concurrent client connections. Default: 600.</summary>
        [TikProperty("max-client-connections", DefaultValue = "600")]
        public TikValue<int?> MaxClientConnections { get; set; }

        /// <summary>max-server-connections — maximum number of concurrent connections to origin servers. Default: 600.</summary>
        [TikProperty("max-server-connections", DefaultValue = "600")]
        public TikValue<int?> MaxServerConnections { get; set; }

        /// <summary>max-fresh-time — maximum time a cached object is considered fresh. Default: 3d.</summary>
        [TikProperty("max-fresh-time", DefaultValue = "3d")]
        public TikValue<TikDuration?> MaxFreshTime { get; set; }

        /// <summary>serialize-connections — enforces sequential client processing over persistent connections.</summary>
        [TikProperty("serialize-connections", DefaultValue = "no")]
        public TikValue<bool?> SerializeConnections { get; set; }

        /// <summary>always-from-cache — when yes, ignores client refresh (no-cache) requests if content is considered fresh.</summary>
        [TikProperty("always-from-cache", DefaultValue = "no")]
        public TikValue<bool?> AlwaysFromCache { get; set; }

        /// <summary>cache-hit-dscp — DSCP value automatically applied to cache-hit packets. Range: 0..63. Default: 4.</summary>
        [TikProperty("cache-hit-dscp", DefaultValue = "4")]
        public TikValue<int?> CacheHitDscp { get; set; }

        /// <summary>Human-readable summary of proxy state.</summary>
        public override string ToString() => string.Format("enabled={0} port={1}", Enabled, Port);
    }
}
