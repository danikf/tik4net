using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpFirewallFilter : OrderedSetRecordHandlerBase<IpFirewallFilter> {
        internal LinkIpFirewallFilter(Link link) : base(link) { }
    }
}
