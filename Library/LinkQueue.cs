namespace InvertedTomato.TikLink {
    public class LinkQueue {
        public readonly LinkQueueSimple Simple;

        private readonly Link Link;

        internal LinkQueue(Link link) {
            Link = link;

            Simple = new LinkQueueSimple(Link);
        }
    }
}
