using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkIpService:SetRecordHandlerBase<IpService> {
        internal LinkIpService(Link link) : base(link) { }

        public override void Delete(IpService record) {
            throw new NotSupportedException();
        }
    }
}
