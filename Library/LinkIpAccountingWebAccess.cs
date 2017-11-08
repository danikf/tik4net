using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpAccountingWebAccess {
        private readonly Link Link;

        internal LinkIpAccountingWebAccess(Link link) {
            Link = link;
        }

        public IpAccountingWebAccess Get(string[] properties = null) {
            return Link.Get<IpAccountingWebAccess>(properties);
        }

        public void Update(IpAccountingWebAccess record, string[] properties = null) {
            Link.Update(record, properties);
        }
    }
}
