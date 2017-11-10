using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpPool :SetRecordHandlerBase<IpPool>{
        internal LinkIpPool(Link link) : base(link) { }
    }
}
