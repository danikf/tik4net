using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServerNetwork : SetRecordHandlerBase<IpDhcpServerNetwork> {
        internal LinkIpDhcpServerNetwork(Link link) : base(link) { }
    }
}
