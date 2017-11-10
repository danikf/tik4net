using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkQueueSimple : SetRecordHandlerBase<QueueSimple> {
        internal LinkQueueSimple(Link link) : base(link) { }
    }
}
