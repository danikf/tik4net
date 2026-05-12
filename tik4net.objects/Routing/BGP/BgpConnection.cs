namespace tik4net.Objects.Routing.Bgp
{
    /// <summary>
    /// BGP connection (peer) configuration as provided by /routing/bgp/connection (RouterOS 7+).
    /// Replaces <see cref="BgpPeer"/> which was available in RouterOS 6 at /routing/bgp/peer.
    /// </summary>
    [TikEntity("/routing/bgp/connection")]
    public class BgpConnection
    {
        [TikProperty(".id", IsReadOnly = true, IsMandatory = true)]
        public string Id { get; private set; }

        [TikProperty("name", IsMandatory = true)]
        public string Name { get; set; }

        [TikProperty("remote.address")]
        public string RemoteAddress { get; set; }

        [TikProperty("remote.as")]
        public string RemoteAs { get; set; }

        [TikProperty("local.role")]
        public string LocalRole { get; set; }

        [TikProperty("templates")]
        public string Templates { get; set; }

        [TikProperty("disabled")]
        public bool Disabled { get; set; }
    }
}
