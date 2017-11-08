using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpDnsCache {
        public readonly LinkIpDnsCacheAll All;

        private readonly Link Link;

        internal LinkIpDnsCache(Link link) {
            Link = link;

            All = new LinkIpDnsCacheAll(Link);
        }

        public IList<IpDnsCache> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDnsCache>(properties, filter);
        }

        public IpDnsCache Get(string id, string[] properties = null) {
            return Link.Get<IpDnsCache>(id, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDnsCache>(id);
        }

        public void Delete(IpDnsCache record) {
            Link.Delete(record);
        }
    }
}
