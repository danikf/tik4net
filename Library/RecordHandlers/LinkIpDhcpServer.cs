using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServer : SetRecordHandlerBase<IpDhcpServer> {
        public readonly LinkIpDhcpServerAlert Alert;
        public readonly LinkIpDhcpServerConfig Config;
        public readonly LinkIpDhcpServerLease Lease;
        public readonly LinkIpDhcpServerNetwork Network;
        public readonly LinkIpDhcpServerOption Option;

        internal LinkIpDhcpServer(Link link) : base(link) {
            Alert = new LinkIpDhcpServerAlert(Link);
            Config = new LinkIpDhcpServerConfig(Link);
            Lease = new LinkIpDhcpServerLease(Link);
            Network = new LinkIpDhcpServerNetwork(Link);
            Option = new LinkIpDhcpServerOption(Link);
        }
    }
}
