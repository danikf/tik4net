using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkBridgeNat {
        private readonly Link Link;

        internal LinkBridgeNat(Link link) {
            Link = link;
        }

        public IList<BridgeNat> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<BridgeNat>(properties, filter);
        }

        public BridgeNat Get(string id, string[] properties = null) {
            return Link.Get<BridgeNat>(id, properties);
        }

        public void Create(BridgeNat record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(BridgeNat record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<BridgeNat>(id);
        }

        public void Delete(BridgeNat record) {
            Link.Delete(record);
        }
    }
}
