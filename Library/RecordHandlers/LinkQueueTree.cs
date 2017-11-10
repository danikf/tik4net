using InvertedTomato.TikLink.Records;
using System.Collections.Generic;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkQueueTree : SetRecordHandlerBase<QueueTree> {
        internal LinkQueueTree(Link link) : base(link) { }
    }
}
