using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Tunnel
{
    /// <summary>
    /// /interface/ipip
    /// IPIP (IP-in-IP) tunnel is a simple protocol that encapsulates IP packets in IP to create
    /// a tunnel between two routers, enabling Intranet traffic to traverse the Internet.
    /// See https://help.mikrotik.com/docs/display/ROS/IPIP
    /// </summary>
    [TikEntity("/interface/ipip", IncludeDetails = true)]
    public class InterfaceIpip
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>name — Tunnel interface name.</summary>
        [TikProperty("name", WinboxLabel = "Name")]
        public TikField<string?> Name { get; set; }

        /// <summary>mtu — Layer3 MTU. Can be "auto" or a specific integer. Default: auto.</summary>
        [TikProperty("mtu", DefaultValue = "auto", WinboxLabel = "MTU")]
        public TikField<string?> Mtu { get; set; }

        /// <summary>actual-mtu — Effective MTU after overhead (read-only).</summary>
        [TikProperty("actual-mtu", IsReadOnly = true, WinboxLabel = "Actual MTU")]
        public TikField<string?> ActualMtu { get; private set; }

        /// <summary>local-address — Local tunnel endpoint IP address. 0.0.0.0 means use the outgoing interface address.</summary>
        [TikProperty("local-address", DefaultValue = "0.0.0.0", WinboxLabel = "Local Address")]
        public TikField<string?> LocalAddress { get; set; }

        /// <summary>remote-address — Remote tunnel endpoint IP address. Required.</summary>
        [TikProperty("remote-address", WinboxLabel = "Remote Address")]
        public TikField<string?> RemoteAddress { get; set; }

        /// <summary>keepalive — Tunnel keepalive interval and retry count (e.g. "10s,10"). Default: 10s,10.</summary>
        [TikProperty("keepalive", DefaultValue = "10s,10", WinboxLabel = "Keepalive")]
        public TikField<string?> Keepalive { get; set; }

        /// <summary>dscp — DSCP value for tunnel packets. "inherit" copies from encapsulated traffic, or 0–63.</summary>
        [TikProperty("dscp", DefaultValue = "inherit", WinboxLabel = "DSCP")]
        public TikField<string?> Dscp { get; set; }

        /// <summary>dont-fragment — DF bit handling: "no" to fragment if needed; "inherit" copies from original packet.</summary>
        [TikProperty("dont-fragment", DefaultValue = "no", WinboxLabel = "Dont Fragment")]
        public TikField<string?> DontFragment { get; set; }

        /// <summary>clamp-tcp-mss — Adjust MSS for TCP SYN packets when they would exceed tunnel MTU. Default: yes.</summary>
        [TikProperty("clamp-tcp-mss", DefaultValue = "yes", WinboxLabel = "Clamp TCP MSS")]
        public TikField<bool?> ClampTcpMss { get; set; }

        /// <summary>allow-fast-path — Allow FastPath processing. Must be disabled when using IPsec. Default: yes.</summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes", WinboxLabel = "Allow Fast Path")]
        public TikField<bool?> AllowFastPath { get; set; }

        /// <summary>ipsec-secret — Pre-shared key for dynamic IPsec peer at the remote address.</summary>
        [TikProperty("ipsec-secret", DefaultValue = "", IsSensitive = true, WinboxLabel = "IPsec Secret")]
        public TikField<string?> IpsecSecret { get; set; }

        /// <summary>running — Whether the tunnel is running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true, WinboxLabel = "running")]
        public TikField<bool?> Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public TikField<bool?> Disabled { get; set; }

        /// <summary>comment — Short description of the tunnel.</summary>
        [TikProperty("comment")]
        public TikField<string?> Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string? ToString() => Name.Value;
    }
}
