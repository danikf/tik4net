using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpDnsCacheAll {
        private readonly Link Link;

        internal LinkIpDnsCacheAll(Link link) {
            Link = link;
        }

        public IList<IpDnsCacheAll> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDnsCacheAll>(properties, filter);
        }

        public IpDnsCacheAll Get(string id, string[] properties = null) {
            return Link.Get<IpDnsCacheAll>(id, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDnsCacheAll>(id);
        }

        public void Delete(IpDnsCacheAll record) {
            Link.Delete(record);
        }
    }
}
