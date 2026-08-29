namespace tik4net.Objects.Routing.Bgp
{
    /// <summary>
    /// BGP connection (peer) configuration as provided by /routing/bgp/connection (RouterOS 7+).
    /// Replaces <see cref="BgpPeer"/> which was available in RouterOS 6 at /routing/bgp/peer.
    /// </summary>
    [TikEntity("/routing/bgp/connection")]
    public class BgpConnection
    {
        /// <summary>.id — primary key</summary>
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string? Id { get; private set; }

        /// <summary>name — Name of the BGP connection.</summary>
        [TikProperty("name", IsMandatory = true)]
        public string? Name { get; set; }

        /// <summary>remote.address — Address (or address list) of the remote BGP peer.</summary>
        [TikProperty("remote.address")]
        public string? RemoteAddress { get; set; }

        /// <summary>remote.as — Autonomous System number of the remote BGP peer.</summary>
        [TikProperty("remote.as")]
        public string? RemoteAs { get; set; }

        /// <summary>local.role — Local BGP role used to negotiate the session (e.g. ibgp, ebgp).</summary>
        [TikProperty("local.role")]
        public string? LocalRole { get; set; }

        /// <summary>templates — Names of BGP templates applied to this connection (comma-separated).</summary>
        [TikProperty("templates")]
        public string? Templates { get; set; }

        /// <summary>disabled — Whether the connection is disabled.</summary>
        [TikProperty("disabled")]
        public bool? Disabled { get; set; }
    }
}
