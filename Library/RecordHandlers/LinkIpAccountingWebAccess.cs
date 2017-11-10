using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpAccountingWebAccess : SingleRecordHandlerBase<IpAccountingWebAccess> {
        internal LinkIpAccountingWebAccess(Link link) : base(link) { }
    }
}
