using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Ip
{
    /// <summary>
    /// /ip/socks: MikroTik SOCKS proxy server settings (singleton). Supports SOCKS v4 and v5 protocols,
    /// enabling TCP connection relaying for clients that support the SOCKS standard.
    /// Use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load.
    /// </summary>
    [TikEntity("/ip/socks", IsSingleton = true)]
    public class IpSocks
    {
        /// <summary>enabled — enables or disables the SOCKS proxy server.</summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>port — TCP port on which the SOCKS server listens. Default: 1080.</summary>
        [TikProperty("port", DefaultValue = "1080")]
        public int Port { get; set; }

        /// <summary>connection-idle-timeout — time after which idle connections are terminated. Default: 2m.</summary>
        [TikProperty("connection-idle-timeout", DefaultValue = "2m")]
        public string/*time*/ ConnectionIdleTimeout { get; set; }

        /// <summary>max-connections — maximum number of simultaneous connections. Range: 1..500. Default: 200.</summary>
        [TikProperty("max-connections", DefaultValue = "200")]
        public int MaxConnections { get; set; }

        /// <summary>version — SOCKS protocol version to use (4 or 5). Default: 4.</summary>
        [TikProperty("version", DefaultValue = "4")]
        public string Version { get; set; }

        /// <summary>auth-method — authentication method (none or username_password). Default: none.</summary>
        [TikProperty("auth-method", DefaultValue = "none")]
        public string AuthMethod { get; set; }

        /// <summary>vrf — VRF instance the server listens on. Default: main.</summary>
        [TikProperty("vrf", DefaultValue = "main")]
        public string Vrf { get; set; }

        /// <summary>Human-readable summary of SOCKS settings.</summary>
        public override string ToString() => string.Format("enabled={0} port={1} version={2}", Enabled, Port, Version);
    }
}
