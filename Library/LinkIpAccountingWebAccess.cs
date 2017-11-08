using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpAccountingWebAccess {
        private readonly Link Link;

        internal LinkIpAccountingWebAccess(Link link) {
            Link = link;
        }

        public IpAccountingWebAccess Get(string[] properties = null) {
            throw new NotImplementedException(); // TODO
            //return Link.Get<BridgeFilter>(id, properties);
        }

        public void Put(IpAccountingWebAccess record, string[] properties = null) {
            throw new NotImplementedException(); // TODO
            //Link.Put(record, properties);
        }
    }
}
