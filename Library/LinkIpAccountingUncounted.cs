using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpAccountingUncounted {
        private readonly Link Link;

        internal LinkIpAccountingUncounted(Link link) {
            Link = link;
        }

        public IpAccountingUncounted Get(string[] properties = null) {
            throw new NotImplementedException(); // TODO
            //return Link.Get<BridgeFilter>(id, properties);
        }

        public void Put(IpAccountingUncounted record, string[] properties = null) {
            throw new NotImplementedException(); // TODO
            //Link.Put(record, properties);
        }
    }
}
