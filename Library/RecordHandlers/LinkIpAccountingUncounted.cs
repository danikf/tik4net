using InvertedTomato.TikLink.Records;
using System;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpAccountingUncounted : SingleRecordHandlerBase<IpAccountingUncounted> {
        internal LinkIpAccountingUncounted(Link link) : base(link) { }
    }
}
