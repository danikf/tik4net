using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkIpHotspotIpBindings {
        private readonly Link Link;

        internal LinkIpHotspotIpBindings(Link link) {
            Link = link;
        }

        /*
        public IList<HotspotIp> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<QueueSimple>(properties, filter);
        }

        public QueueSimple Get(string id, string[] properties = null) {
            return Link.Get<QueueSimple>(id, properties);
        }

        public void Put(QueueSimple record, string[] properties = null) {
            Link.Put(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<QueueSimple>(id);
        }*/
    }
}
