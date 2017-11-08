namespace InvertedTomato.TikLink {
    public class LinkIpHotspot {
        public readonly LinkIpArp Arp;

        private readonly Link Link;

        internal LinkIpHotspot(Link link) {
            Link = link;

            Arp = new LinkIpArp(Link);
        }
    }
}
