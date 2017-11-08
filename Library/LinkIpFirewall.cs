namespace InvertedTomato.TikLink {
    public class LinkIpFirewall {
        public readonly LinkIpFirewallFilter Filter;

        private readonly Link Link;

        internal LinkIpFirewall(Link link) {
            Link = link;

            Filter = new LinkIpFirewallFilter(Link);
        }
    }
}
