using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpFirewallAddressList {
        private readonly Link Link;

        internal LinkIpFirewallAddressList(Link link) {
            Link = link;
        }

        public IList<IpFirewallAddressList> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpFirewallAddressList>(properties, filter);
        }

        public IpFirewallAddressList Get(string id, string[] properties = null) {
            return Link.Get<IpFirewallAddressList>(id, properties);
        }

        public void Add(IpFirewallAddressList record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(IpFirewallAddressList record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpFirewallAddressList>(id);
        }

        public void Delete(IpFirewallAddressList record) {
            Link.Delete(record);
        }
    }
}
