using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkBridgeFilter {
        private readonly Link Link;

        internal LinkBridgeFilter(Link link) {
            Link = link;
        }

        public IList<BridgeFilter> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<BridgeFilter>(properties, filter);
        }

        public BridgeFilter Get(string id, string[] properties = null) {
            return Link.Get<BridgeFilter>(id, properties);
        }

        public void Put(BridgeFilter record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<BridgeFilter>(id);
        }

        public void Delete(BridgeFilter record) {
            Link.Delete(record);
        }
    }
}
