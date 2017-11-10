using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpAddress : SetRecordHandlerBase<IpAddress> {
        internal LinkIpAddress(Link link) : base(link) { }
    }
}
