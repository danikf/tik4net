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
            throw new NotImplementedException(); // TODO
            //return Link.Get<BridgeFilter>(id, properties);
        }

        public void Put(IpAccounting record, string[] properties = null) {
            throw new NotImplementedException(); // TODO
            //Link.Put(record, properties);
        }
    }
}
