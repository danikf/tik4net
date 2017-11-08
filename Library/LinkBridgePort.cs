using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkBridgePort {
        private readonly Link Link;

        internal LinkBridgePort(Link link) {
            Link = link;
        }

        public IList<BridgePort> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<BridgePort>(properties, filter);
        }

        public BridgePort Get(string id, string[] properties = null) {
            return Link.Get<BridgePort>(id, properties);
        }

        public void Put(BridgePort record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<BridgePort>(id);
        }

        public void Delete(BridgePort record) {
            Link.Delete(record);
        }
    }
}
