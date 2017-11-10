using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpAccounting : SingleRecordHandlerBase<IpAccounting> {
        public LinkIpAccountingSnapshot Snapshot;
        public LinkIpAccountingUncounted Uncounted;
        public LinkIpAccountingWebAccess WebAccess;

        internal LinkIpAccounting(Link link) : base(link) {
            Snapshot = new LinkIpAccountingSnapshot(Link);
            Uncounted = new LinkIpAccountingUncounted(Link);
            WebAccess = new LinkIpAccountingWebAccess(Link);
        }
    }
}
