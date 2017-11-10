using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpHotspotActive : SetRecordHandlerBase<IpHotspotActive> {
        internal LinkIpHotspotActive(Link link) : base(link) { }

        public override void Update(IpHotspotActive record, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
