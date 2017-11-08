namespace InvertedTomato.TikLink {
    public class LinkIp {
        public readonly LinkIpAccounting Accounting;
        public readonly LinkIpArp Arp;
        public readonly LinkIpDhcpServer DhcpServer;

        public readonly LinkIpHotspot Hotspot;


        private readonly Link Link;

        internal LinkIp(Link link) {
            Link = link;

            Accounting = new LinkIpAccounting(Link);
            Arp = new LinkIpArp(Link);
            DhcpServer = new LinkIpDhcpServer(Link);
            Hotspot = new LinkIpHotspot(Link);
        }
    }
}
