using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDnsCache : SetRecordHandlerBase<IpDnsCache> {
        public readonly LinkIpDnsCacheAll All;

        internal LinkIpDnsCache(Link link) : base(link) {
            All = new LinkIpDnsCacheAll(Link);
        }

        public override void Update(IpDnsCache record, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
