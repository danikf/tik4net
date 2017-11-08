using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkBridgeSettings {
        private readonly Link Link;

        internal LinkBridgeSettings(Link link) {
            Link = link;
        }

        public BridgeFilter Get(string[] properties = null) {
            throw new NotImplementedException();
            //return Link.Get<BridgeFilter>(id, properties);
        }

        public void Put(BridgeFilter record, string[] properties = null) {
            Link.Put(record, properties);
        }
    }
}
