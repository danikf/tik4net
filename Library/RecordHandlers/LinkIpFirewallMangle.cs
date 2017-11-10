using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpFirewallMangle : OrderedSetRecordHandlerBase<IpFirewallMangle> {
        internal LinkIpFirewallMangle(Link link) : base(link) { }
    }
}
