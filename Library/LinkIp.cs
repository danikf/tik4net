namespace InvertedTomato.TikLink {
    public class LinkIp {
        public readonly LinkIpAccounting Accounting;
        public readonly LinkIpAddress Address;
        public readonly LinkIpArp Arp;
        public readonly LinkIpDhcpClient DhcpClient;
        public readonly LinkIpDhcpServer DhcpServer;
        public readonly LinkIpDns Dns;
        public readonly LinkIpHotspot Hotspot;
        public readonly LinkIpPool Pool;

        private readonly Link Link;

        internal LinkIp(Link link) {
            Link = link;

            Accounting = new LinkIpAccounting(Link);
            Address = new LinkIpAddress(Link);
            Arp = new LinkIpArp(Link);
            DhcpClient = new LinkIpDhcpClient(Link);
            DhcpServer = new LinkIpDhcpServer(Link);
            Dns = new LinkIpDns(Link);
            Hotspot = new LinkIpHotspot(Link);
            Pool = new LinkIpPool(Link);
        }
    }
}
