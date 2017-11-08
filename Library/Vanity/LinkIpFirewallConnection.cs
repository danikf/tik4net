using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpFirewallConnection {
        public readonly LinkIpFirewallConnectionTracking Tracking;

        private readonly Link Link;

        internal LinkIpFirewallConnection(Link link) {
            Link = link;

            Tracking = new LinkIpFirewallConnectionTracking(Link);
        }

        public IList<IpFirewallConnection> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpFirewallConnection>(properties, filter);
        }

        public IpFirewallConnection Get(string id, string[] properties = null) {
            return Link.Get<IpFirewallConnection>(id, properties);
        }

        public void Create(IpFirewallConnection record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpFirewallConnection record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpFirewallConnection>(id);
        }

        public void Delete(IpFirewallConnection record) {
            Link.Delete(record);
        }
    }
}
