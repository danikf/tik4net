using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpAccountingSnapshot:SetRecordHandlerBase<IpAccountingSnapshot> {
        internal LinkIpAccountingSnapshot(Link link):base(link) {        }

        public override void Update(IpAccountingSnapshot record, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
