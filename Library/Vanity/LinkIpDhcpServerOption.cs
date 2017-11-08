using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.Vanity {
    public class LinkIpDhcpServerOption {
        private readonly Link Link;

        internal LinkIpDhcpServerOption(Link link) {
            Link = link;
        }

        public IList<IpDhcpServerOption> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDhcpServerOption>(properties, filter);
        }

        public IpDhcpServerOption Get(string id, string[] properties = null) {
            return Link.Get<IpDhcpServerOption>(id, properties);
        }

        public void Create(IpDhcpServerOption record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpDhcpServerOption record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDhcpServerOption>(id);
        }

        public void Delete(IpDhcpServerOption record) {
            Link.Delete(record);
        }
    }
}
