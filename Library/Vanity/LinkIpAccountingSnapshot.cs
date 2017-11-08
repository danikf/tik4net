using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpAccountingSnapshot {
        private readonly Link Link;

        internal LinkIpAccountingSnapshot(Link link) {
            Link = link;
        }

        public IList<IpAccountingSnapshot> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpAccountingSnapshot>(properties, filter);
        }

        public IpAccountingSnapshot Get(string id, string[] properties = null) {
            return Link.Get<IpAccountingSnapshot>(id, properties);
        }
    }
}
