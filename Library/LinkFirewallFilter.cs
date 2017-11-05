using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkFirewallFilter {
        private readonly Link Link;

        internal LinkFirewallFilter(Link link) {
            Link = link;
        }

        public IList<FirewallFilter> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<FirewallFilter>(properties, filter);
        }

        public FirewallFilter Get(string id, string[] properties = null) {
            return Link.Get<FirewallFilter>(id, properties);
        }

        public void Put(FirewallFilter record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<FirewallFilter>(id);
        }
    }
}
