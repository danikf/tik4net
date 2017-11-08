using InvertedTomato.TikLink.RosRecords;
using System.Collections.Generic;

namespace InvertedTomato.TikLink {
    public class LinkIpDhcpServer {
        public readonly LinkIpDhcpServerAlert Alert;
        public readonly LinkIpDhcpServerConfig Config;
        public readonly LinkIpDhcpServerLease Lease;
        public readonly LinkIpDhcpServerNetwork Network;
        public readonly LinkIpDhcpServerOption Option;

        private readonly Link Link;

        internal LinkIpDhcpServer(Link link) {
            Link = link;

            Alert = new LinkIpDhcpServerAlert(Link);
            Config = new LinkIpDhcpServerConfig(Link);
            Lease = new LinkIpDhcpServerLease(Link);
            Network = new LinkIpDhcpServerNetwork(Link);
            Option = new LinkIpDhcpServerOption(Link);
        }

        public IList<IpDhcpServer> List(string[] properties = null, Dictionary<string, string> filter = null) {
            return Link.List<IpDhcpServer>(properties, filter);
        }

        public IpDhcpServer Get(string id, string[] properties = null) {
            return Link.Get<IpDhcpServer>(id, properties);
        }

        public void Create(IpDhcpServer record, string[] properties = null) {
            Link.Create(record, properties);
        }

        public void Update(IpDhcpServer record, string[] properties = null) {
            Link.Update(record, properties);
        }

        public void Delete(string id) {
            Link.Delete<IpDhcpServer>(id);
        }

        public void Delete(IpDhcpServer record) {
            Link.Delete(record);
        }
    }
}
