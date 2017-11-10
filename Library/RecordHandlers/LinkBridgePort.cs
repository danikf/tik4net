using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkBridgePort : SetRecordHandlerBase<BridgePort> {
        public LinkBridgePort(Link link) : base(link) { }
    }
}
