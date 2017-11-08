using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpAccounting {
        public LinkIpAccountingSnapshot Snapshot;
        public LinkIpAccountingUncounted Uncounted;
        public LinkIpAccountingWebAccess WebAccess;

        private readonly Link Link;

        internal LinkIpAccounting(Link link) {
            Link = link;

            Snapshot = new LinkIpAccountingSnapshot(Link);
            Uncounted = new LinkIpAccountingUncounted(Link);
            WebAccess = new LinkIpAccountingWebAccess(Link);
        }

        public IpAccounting Get(string[] properties = null) {
            return Link.Get<IpAccounting>(properties);
        }

        public void Update(IpAccounting record, string[] properties = null) {
            Link.Update(record, properties);
        }
    }
}
