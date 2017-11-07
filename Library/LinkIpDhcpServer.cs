namespace InvertedTomato.TikLink {
    public class LinkIpDhcpServer {
        public readonly LinkIpDhcpServerLease Lease;
        public readonly LinkIpDhcpServerNetwork Network;

        private readonly Link Link;

        internal LinkIpDhcpServer(Link link) {
            Link = link;

            Lease = new LinkIpDhcpServerLease(Link);
            Network = new LinkIpDhcpServerNetwork(Link);
        }
    }
}
