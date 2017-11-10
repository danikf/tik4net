using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDns : SingleRecordHandlerBase<IpDns> {
        public readonly LinkIpDnsCache Cache;
        public readonly LinkIpDnsStatic Static;

        internal LinkIpDns(Link link) : base(link) {
            Cache = new LinkIpDnsCache(Link);
            Static = new LinkIpDnsStatic(Link);
        }
    }
}
