using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpClient : SetRecordHandlerBase<IpDhcpClient> {
        internal LinkIpDhcpClient(Link link) : base(link) { }
    }
}
