using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkInterfaceBridge : SetRecordHandlerBase<InterfaceBridge> {
        public LinkInterfaceBridge(Link link) : base(link) { }
    }
}
