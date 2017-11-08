using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpFirewallMangle {
        private readonly Link Link;

        internal LinkIpFirewallMangle(Link link) {
            Link = link;
        }

        public IList<IpFirewallMangle> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpFirewallMangle>(properties, filter);
        }

        public IpFirewallMangle Get(string id, string[] properties = null) {
            return Link.Get<IpFirewallMangle>(id, properties);
        }

        public void Create(IpFirewallMangle record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpFirewallMangle record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpFirewallMangle>(id);
        }

        public void Delete(IpFirewallMangle record) {
            Link.Delete(record);
        }
    }
}
