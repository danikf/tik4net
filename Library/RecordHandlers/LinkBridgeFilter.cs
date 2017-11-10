using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkBridgeFilter : SetRecordHandlerBase<BridgeFilter> {
        public LinkBridgeFilter(Link link) : base(link) { }
    }
}
