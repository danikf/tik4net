namespace InvertedTomato.TikLink {
    public class LinkHotspot {
        public readonly LinkIpArp Arp;

        private readonly Link Link;

        internal LinkHotspot(Link link) {
            Link = link;

            Arp = new LinkIpArp(Link);
        }
    }
}
