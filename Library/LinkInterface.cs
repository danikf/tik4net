using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkInterface {
        private readonly Link Link;

        internal LinkInterface(Link link) {
            Link = link;
        }

        public IList<Interface> List(string[] properties = null, Dictionary<string, string> query = null) {
            return Link.List<Interface>(properties, query);
        }

        public Interface Get(string id, string[] properties = null) {
            return Link.Get<Interface>(id, properties);
        }

        public void Put(Interface record, string[] properties = null) {
            Link.Put(record, properties);
        }
    }
}
