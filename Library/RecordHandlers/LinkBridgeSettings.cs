using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkBridgeSettings : SingleRecordHandlerBase<BridgeSettings> {
        public LinkBridgeSettings(Link link) : base(link) { }
    }
}
