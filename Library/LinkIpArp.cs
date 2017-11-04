using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkIpArp {
        private readonly Link Link;

        internal LinkIpArp(Link link) {
            Link = link;
        }

        public IList<IpArp> List(string[] properties = null, Dictionary<string, string> query = null) {
            return Link.List<IpArp>(properties, query);
        }

        public IpArp Get(string id, string[] properties = null) {
            return Link.Get<IpArp>(id, properties);
        }

        public void Put(IpArp record, string[] properties = null) {
            Link.Put(record, properties);
        }
    }
}
