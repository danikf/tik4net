using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpDhcpServerAlert:SetRecordHandlerBase<IpDhcpServerAlert> {
        internal LinkIpDhcpServerAlert(Link link) : base(link) { }
    }
}
