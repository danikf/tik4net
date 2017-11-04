namespace InvertedTomato.TikLink {
    public class LinkIp {
        public readonly LinkIpArp Arp;

        private readonly Link Link;

        internal LinkIp(Link link) {
            Link = link;

            Arp = new LinkIpArp(Link);
        }
    }
}
