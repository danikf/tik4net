using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpFirewallNat : OrderedSetRecordHandlerBase<IpFirewallNat> {
        internal LinkIpFirewallNat(Link link) : base(link) { }
    }
}
