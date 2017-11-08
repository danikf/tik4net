using InvertedTomato.TikLink.RosRecords;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkIpAccountingUncounted {
        private readonly Link Link;

        internal LinkIpAccountingUncounted(Link link) {
            Link = link;
        }

        public IpAccountingUncounted Get(string[] properties = null) {
            return Link.Get<IpAccountingUncounted>(properties);
        }

        public void Update(IpAccountingUncounted record, string[] properties = null) {
            Link.Update(record, properties);
        }
    }
}
