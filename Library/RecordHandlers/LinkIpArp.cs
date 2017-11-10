using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpArp : SetRecordHandlerBase<IpArp> {
        internal LinkIpArp(Link link) : base(link) { }
    }
}
