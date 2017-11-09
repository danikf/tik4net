using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServerNetwork {
        private readonly Link Link;

        internal LinkIpDhcpServerNetwork(Link link) {
            Link = link;
        }

        public IList<IpDhcpServerNetwork> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDhcpServerNetwork>(properties, filter);
        }

        public IpDhcpServerNetwork Get(string id, string[] properties = null) {
            return Link.Get<IpDhcpServerNetwork>(id, properties);
        }

        public void Add(IpDhcpServerNetwork record, string[] properties = null) {
            Link.Add(record, properties);
        }

        public void Update(IpDhcpServerNetwork record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDhcpServerNetwork>(id);
        }

        public void Delete(IpDhcpServerNetwork record) {
            Link.Delete(record);
        }
    }
}
