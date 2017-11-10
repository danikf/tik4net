using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServerConfig : SingleRecordHandlerBase<IpDhcpServerConfig> {
        internal LinkIpDhcpServerConfig(Link link) : base(link) { }
    }
}
