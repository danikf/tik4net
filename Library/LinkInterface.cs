using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkInterface {
        private readonly Link Link;

        internal LinkInterface(Link link) {
            Link = link;
        }

        public IList<Interface> Scan(List<string> readProperties = null, List<string> query = null) {
            return Link.Scan<Interface>(readProperties, query);
        }
    }
}
