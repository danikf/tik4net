using InvertedTomato.TikLink.Records;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkSystemResource: SingleRecordHandlerBase<SystemResource> {
        internal LinkSystemResource(Link link) : base(link) { }

        public override void Update(SystemResource record, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
