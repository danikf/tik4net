using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDnsCacheAll : SetRecordHandlerBase<IpDnsCacheAll> {
        public readonly LinkIpDnsCacheAll All;

        internal LinkIpDnsCacheAll(Link link) : base(link) { }

        public override void Update(IpDnsCacheAll record, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
