namespace InvertedTomato.TikLink {
    public class LinkFirewall {
        public readonly LinkFirewallFilter Filter;

        private readonly Link Link;

        internal LinkFirewall(Link link) {
            Link = link;

            Filter = new LinkFirewallFilter(Link);
        }
    }
}
