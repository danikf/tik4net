using InvertedTomato.TikLink.RosRecords;
using System;

namespace InvertedTomato.TikLink {
    public class LinkBridgeSettings {
        private readonly Link Link;

        internal LinkBridgeSettings(Link link) {
            Link = link;
        }

        public BridgeSettings Get(string[] properties = null) {
            return Link.Get<BridgeSettings>(properties);
        }

        public void Update(BridgeSettings record, string[] properties = null) {
            Link.Update(record, properties);
        }
    }
}
