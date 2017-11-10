using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDnsStatic : SetRecordHandlerBase<IpDnsStatic> {
        internal LinkIpDnsStatic(Link link) : base(link) { }
    }
}
