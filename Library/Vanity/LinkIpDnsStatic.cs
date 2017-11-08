using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpDnsStatic {
        private readonly Link Link;

        internal LinkIpDnsStatic(Link link) {
            Link = link;
        }

        public IList<IpDnsStatic> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDnsStatic>(properties, filter);
        }

        public IpDnsStatic Get(string id, string[] properties = null) {
            return Link.Get<IpDnsStatic>(id, properties);
        }

        public void Create(IpDnsStatic record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpDnsStatic record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDnsStatic>(id);
        }

        public void Delete(IpDnsStatic record) {
            Link.Delete(record);
        }
    }
}
