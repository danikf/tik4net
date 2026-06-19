using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Objects.Interface.Vpn
{
    /// <summary>
    /// /interface/pptp-server/server: PPTP server configuration singleton.
    /// Point-to-Point Tunneling Protocol (PPTP) is a method for implementing virtual private networks.
    /// PPTP uses a control channel over TCP and a GRE tunnel operating to encapsulate PPP packets.
    /// The server accepts incoming PPTP connections from clients and validates them using the
    /// configured authentication methods.
    /// This is a singleton menu — use <see cref="TikConnectionExtensions.LoadSingle{T}"/> to load it.
    /// </summary>
    [TikEntity("/interface/pptp-server/server", IsSingleton = true)] // no =detail= — this server singleton rejects it
    public class PptpServer
    {
        // ---- Writable properties ----

        /// <summary>
        /// authentication — comma-separated list of permitted PPP authentication methods the server will accept.
        /// Valid values: pap, chap, mschap1, mschap2.
        /// Default: mschap1,mschap2
        /// </summary>
        [TikProperty("authentication", DefaultValue = "mschap1,mschap2")]
        public string Authentication { get; set; }

        /// <summary>
        /// default-profile — PPP profile applied to new PPTP sessions.
        /// Default: default-encryption
        /// </summary>
        [TikProperty("default-profile", DefaultValue = "default-encryption")]
        public string DefaultProfile { get; set; }

        /// <summary>
        /// enabled — when <c>true</c> the PPTP server accepts incoming connections.
        /// Default: no
        /// </summary>
        [TikProperty("enabled", DefaultValue = "no")]
        public bool Enabled { get; set; }

        /// <summary>
        /// keepalive-timeout — if the server during the keepalive period does not receive any packet,
        /// it will send keepalive packets every second five times. If none of the keepalive packets are
        /// answered, the client is disconnected. Unit: seconds.
        /// Default: 30
        /// </summary>
        // router default 30; omitted on add when left 0
        [TikProperty("keepalive-timeout")]
        public int KeepaliveTimeout { get; set; }

        /// <summary>
        /// max-mru — maximum receive unit for PPTP tunnel interfaces, in bytes.
        /// Max packet size that the PPTP interface will be able to receive without packet fragmentation.
        /// Default: 1450
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mru")]
        public int MaxMru { get; set; }

        /// <summary>
        /// max-mtu — maximum transmit unit for PPTP tunnel interfaces, in bytes.
        /// Max packet size that the PPTP interface will be able to send without packet fragmentation.
        /// Default: 1450
        /// </summary>
        // router default 1450; omitted on add when left 0
        [TikProperty("max-mtu")]
        public int MaxMtu { get; set; }

        /// <summary>
        /// mrru — maximum packet size that can be received on the link. If a packet is bigger than
        /// tunnel MTU, it will be split into multiple packets, allowing full-size IP or Ethernet
        /// packets to be sent over the tunnel. Set to <c>disabled</c> to turn off MRRU negotiation.
        /// Valid integer range: 512..65535.
        /// Default: disabled
        /// </summary>
        [TikProperty("mrru", DefaultValue = "disabled")]
        public string/*integer or "disabled"*/ Mrru { get; set; }

        /// <summary>Human-readable summary of the PPTP server configuration.</summary>
        public override string ToString() => string.Format("pptp-server enabled={0} max-mtu={1} max-mru={2} auth={3}", Enabled, MaxMtu, MaxMru, Authentication);
    }
}
