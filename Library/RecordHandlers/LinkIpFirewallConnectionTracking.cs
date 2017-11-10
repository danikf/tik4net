using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpFirewallConnectionTracking : SingleRecordHandlerBase<IpFirewallConnectionTracking> {
        internal LinkIpFirewallConnectionTracking(Link link) : base(link) { }
    }
}
