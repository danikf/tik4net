using InvertedTomato.TikLink.Records;

namespace InvertedTomato.TikLink.RecordHandlers {
    public class LinkQueueType : SetRecordHandlerBase<QueueType> {
        internal LinkQueueType(Link link) : base(link) { }
    }
}
