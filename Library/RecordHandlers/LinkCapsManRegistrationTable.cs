using InvertedTomato.TikLink.Records;
using System.Collections.Generic;
using System;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkCapsManRegistrationTable : FixedSetRecordHandlerBase<CapsManRegistrationTable> {
        public LinkCapsManRegistrationTable(Link link) : base(link) { }

        public override void Update(CapsManRegistrationTable record, string[] properties = null) {
            throw new NotSupportedException();
        }
    }
}
