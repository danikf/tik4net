namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpFirewall {
        public readonly LinkIpFirewallAddressList AddressList;
        public readonly LinkIpFirewallConnection Connection;
        public readonly LinkIpFirewallFilter Filter;
        public readonly LinkIpFirewallMangle Mangle;
        public readonly LinkIpFirewallNat Nat;

        private readonly Link Link;

        internal LinkIpFirewall(Link link) {
            Link = link;

            AddressList = new LinkIpFirewallAddressList(Link);
            Connection = new LinkIpFirewallConnection(Link);
            Filter = new LinkIpFirewallFilter(Link);
            Mangle = new LinkIpFirewallMangle(Link);
            Nat = new LinkIpFirewallNat(Link);
        }
    }
}
