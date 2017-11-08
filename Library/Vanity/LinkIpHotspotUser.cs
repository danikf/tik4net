using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpHotspotUser {
        private readonly Link Link;

        internal LinkIpHotspotUser(Link link) {
            Link = link;
        }

        public IList<IpHotspotUser> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpHotspotUser>(properties, filter);
        }

        public IpHotspotUser Get(string id, string[] properties = null) {
            return Link.Get<IpHotspotUser>(id, properties);
        }

        public void Add(IpHotspotUser record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(IpHotspotUser record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpHotspotUser>(id);
        }

        public void Delete(IpHotspotUser record) {
            Link.Delete(record);
        }
    }
}
