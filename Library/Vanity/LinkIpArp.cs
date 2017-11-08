using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpArp {
        private readonly Link Link;

        internal LinkIpArp(Link link) {
            Link = link;
        }

        public IList<IpArp> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpArp>(properties, filter);
        }

        public IpArp Get(string id, string[] properties = null) {
            return Link.Get<IpArp>(id, properties);
        }

        public void Add(IpArp record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(IpArp record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpArp>(id);
        }

        public void Delete(IpArp record) {
            Link.Delete(record);
        }
    }
}
