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
        public string Id { get; private set; }

        /// <summary>name — Tunnel interface name.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        /// <summary>mtu — Layer3 MTU. Can be "auto" or a specific integer. Default: auto.</summary>
        [TikProperty("mtu", DefaultValue = "auto")]
        public string Mtu { get; set; }

        /// <summary>actual-mtu — Effective MTU after overhead (read-only).</summary>
        [TikProperty("actual-mtu", IsReadOnly = true)]
        public string ActualMtu { get; private set; }

        /// <summary>local-address — Local tunnel endpoint IP address. 0.0.0.0 means use the outgoing interface address.</summary>
        [TikProperty("local-address", DefaultValue = "0.0.0.0")]
        public string/*IP*/ LocalAddress { get; set; }

        /// <summary>remote-address — Remote tunnel endpoint IP address. Required.</summary>
        [TikProperty("remote-address", IsMandatory = true)]
        public string/*IP*/ RemoteAddress { get; set; }

        /// <summary>keepalive — Tunnel keepalive interval and retry count (e.g. "10s,10"). Default: 10s,10.</summary>
        [TikProperty("keepalive", DefaultValue = "10s,10")]
        public string Keepalive { get; set; }

        /// <summary>dscp — DSCP value for tunnel packets. "inherit" copies from encapsulated traffic, or 0–63.</summary>
        [TikProperty("dscp", DefaultValue = "inherit")]
        public string Dscp { get; set; }

        /// <summary>dont-fragment — DF bit handling: "no" to fragment if needed; "inherit" copies from original packet.</summary>
        [TikProperty("dont-fragment", DefaultValue = "no")]
        public string DontFragment { get; set; }

        /// <summary>clamp-tcp-mss — Adjust MSS for TCP SYN packets when they would exceed tunnel MTU. Default: yes.</summary>
        [TikProperty("clamp-tcp-mss", DefaultValue = "yes")]
        public bool ClampTcpMss { get; set; }

        /// <summary>allow-fast-path — Allow FastPath processing. Must be disabled when using IPsec. Default: yes.</summary>
        [TikProperty("allow-fast-path", DefaultValue = "yes")]
        public bool AllowFastPath { get; set; }

        /// <summary>ipsec-secret — Pre-shared key for dynamic IPsec peer at the remote address.</summary>
        [TikProperty("ipsec-secret", DefaultValue = "")]
        public string IpsecSecret { get; set; }

        /// <summary>running — Whether the tunnel is running (read-only).</summary>
        [TikProperty("running", IsReadOnly = true)]
        public bool Running { get; private set; }

        /// <summary>disabled — Whether the interface is disabled.</summary>
        [TikProperty("disabled", DefaultValue = "no")]
        public bool Disabled { get; set; }

        /// <summary>comment — Short description of the tunnel.</summary>
        [TikProperty("comment")]
        public string Comment { get; set; }

        /// <summary>Human-readable identity.</summary>
        public override string ToString() => Name;
    }
}
