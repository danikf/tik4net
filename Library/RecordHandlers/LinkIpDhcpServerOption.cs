using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServerOption : SetRecordHandlerBase<IpDhcpServerOption> {
        internal LinkIpDhcpServerOption(Link link) : base(link) { }
    }
}
