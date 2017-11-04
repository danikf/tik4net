using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkIpArp {
        private readonly Link Link;

        internal LinkIpArp(Link link) {
            Link = link;
        }

        public IList<IpArp> Scan(List<string> readProperties = null, List<string> query = null) {
            return Link.Scan<IpArp>(readProperties, query);
        }
    }
}
