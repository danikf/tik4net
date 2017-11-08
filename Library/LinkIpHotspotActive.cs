using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink {
    public class LinkIpHotspotActive {
        private readonly Link Link;

        internal LinkIpHotspotActive(Link link) {
            Link = link;
        }

        public IList<IpHotspotActive> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpHotspotActive>(properties, filter);
        }

        public IpHotspotActive Get(string id, string[] properties = null) {
            return Link.Get<IpHotspotActive>(id, properties);
        }
        
        public void Delete(string id) {
            Link.Delete<IpHotspotActive>(id);
        }

        public void Delete(IpHotspotActive record) {
            Link.Delete(record);
        }
    }
}
